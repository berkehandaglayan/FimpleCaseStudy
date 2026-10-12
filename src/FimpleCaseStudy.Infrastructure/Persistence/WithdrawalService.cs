using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FimpleCaseStudy.Application.Withdrawals;
using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace FimpleCaseStudy.Infrastructure.Persistence;

public sealed class CardIdentity
{
    private readonly byte[] key;
    public CardIdentity(string secret)
    {
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Withdrawals:HmacKey must contain at least 32 UTF-8 bytes.");
        key = Encoding.UTF8.GetBytes(secret);
    }
    public string Hash(string? card)
    {
        // Strict canonical input prevents alternate representations bypassing the card limit.
        if (card is null || card.Length is < 12 or > 19 || card.Any(c => c is < '0' or > '9'))
            throw new DomainException("INVALID_CARD_NUMBER", "Card number must contain 12 to 19 ASCII digits.");
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(card)));
    }
}

internal sealed class WithdrawalService(CashManagementDbContext db, TimeProvider clock, CardIdentity identity)
    : IWithdrawalService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, false) }
    };
    private static readonly TimeZoneInfo BusinessZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<QuoteView> QuoteAsync(Guid atmId, QuoteRequest request, CancellationToken ct)
    {
        var hash = identity.Hash(request.CardNumber);
        Banknotes.Validate(request.Currency, 20);
        // Cleanup commits separately, so later rejected business requests cannot undo it.
        await ExpireAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CashTransactionGate.AcquireAsync(db, ct);
        var now = clock.GetUtcNow();
        await ReleaseExpiredInsideAsync(now, ct);
        try
        {
            var atm = await LoadAtmAsync(atmId, ct);
            var remaining = await RemainingAsync(hash, request.Currency, now, ct);
            var selection = BanknoteSelector.Select(atm, request.Currency, request.Amount, remaining);
            var options = new[] { ("exact", selection.Exact), ("lower", selection.Lower), ("upper", selection.Upper) }
                .Where(x => x.Item2 is not null).Select(x => new QuoteOption(x.Item1, x.Item2!)).ToArray();
            if (options.Length == 0)
                throw Conflict("INSUFFICIENT_CASH", "No positive payable amount is available.");
            var reservations = options.SelectMany(o => o.Plan.Lines).GroupBy(l => l.CassetteId)
                .Select(g => g.First() with { Count = g.Max(l => l.Count) }).OrderBy(l => l.Slot).ToArray();
            var quote = new WithdrawalQuote
            {
                Id = Guid.NewGuid(), AtmId = atmId, CardHash = hash, Currency = request.Currency,
                CreatedAt = now, ExpiresAt = now.AddSeconds(60), LimitReservation = options.Max(o => o.Plan.Amount),
                OptionsJson = JsonSerializer.Serialize(options, Json), ReservationsJson = JsonSerializer.Serialize(reservations, Json)
            };
            db.WithdrawalQuotes.Add(quote);
            db.Movements.AddRange(atm.ChangeWithdrawalCash(quote.Currency, reservations, null, false, quote.Id, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(quote);
        }
        catch (DomainException)
        {
            // Business validation is completed before new quote/cash mutations.
            // Preserve any expiry that occurred between the two gate acquisitions.
            await tx.CommitAsync(ct);
            throw;
        }
    }

    public async Task<QuoteView> GetAsync(Guid atmId, Guid quoteId, CancellationToken ct)
    {
        await ExpireAsync(ct);
        return View(await db.WithdrawalQuotes.AsNoTracking().SingleOrDefaultAsync(q => q.Id == quoteId && q.AtmId == atmId, ct)
            ?? throw Missing());
    }

    public async Task<ConfirmationResult> ConfirmAsync(Guid atmId, Guid quoteId, ConfirmRequest request, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(c => c < 33 || c > 126))
            throw new DomainException("INVALID_IDEMPOTENCY_KEY", "Provide an Idempotency-Key of 1 to 128 printable ASCII characters.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { atmId, quoteId, request.Option }, Json))));
        await ExpireAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CashTransactionGate.AcquireAsync(db, ct);
        var replay = await db.IdempotencyRecords.FindAsync([key], ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) throw Conflict("IDEMPOTENCY_CONFLICT", "The key was used for a different request.");
            return new(replay.StatusCode, replay.ResponseJson);
        }
        var now = clock.GetUtcNow();
        await ReleaseExpiredInsideAsync(now, ct);
        var quote = await db.WithdrawalQuotes.SingleOrDefaultAsync(q => q.Id == quoteId && q.AtmId == atmId, ct);
        ConfirmationResult result;
        if (quote is null)
            result = Problem(atmId, quoteId, "QUOTE_NOT_FOUND", "Quote does not exist at this ATM.", 404);
        else if (quote.Status != "Open")
            result = Problem(atmId, quoteId, "QUOTE_" + quote.Status.ToUpperInvariant(), "Quote is no longer open.");
        else
        {
            var option = Options(quote).SingleOrDefault(o => o.Name == request.Option);
            if (option is null) result = Problem(atmId, quoteId, "INVALID_QUOTE_OPTION", "Select an offered option.", 400);
            else
            {
                var atm = await LoadAtmAsync(atmId, ct);
                var remaining = await RemainingAsync(quote.CardHash, quote.Currency, now, ct, quote.Id);
                if (atm.State != AtmState.InService) result = Problem(atmId, quoteId, "INVALID_ATM_STATE", "ATM is not in service.");
                else if (option.Plan.Amount > remaining) result = Problem(atmId, quoteId, "DAILY_LIMIT_EXCEEDED", "Selected amount exceeds the daily limit.");
                else
                {
                    db.Movements.AddRange(atm.ChangeWithdrawalCash(quote.Currency, Reservations(quote), option.Plan, true, quote.Id, now));
                    quote.Status = "Confirmed";
                    quote.ConfirmedAt = now;
                    quote.ConfirmedAmount = option.Plan.Amount;
                    quote.SelectedOption = option.Name;
                    result = new(200, JsonSerializer.Serialize(View(quote), Json));
                }
            }
        }
        db.IdempotencyRecords.Add(new() { Key = key, Fingerprint = fingerprint, StatusCode = result.StatusCode,
            ResponseJson = result.Json, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<QuoteView> CancelAsync(Guid atmId, Guid quoteId, CancellationToken ct)
    {
        await ExpireAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CashTransactionGate.AcquireAsync(db, ct);
        var now = clock.GetUtcNow();
        await ReleaseExpiredInsideAsync(now, ct);
        try
        {
            var quote = await FindAsync(atmId, quoteId, ct);
            if (quote.Status == "Open") await ReleaseAsync(quote, "Cancelled", now, ct);
            else if (quote.Status == "Confirmed") throw Conflict("QUOTE_CONFIRMED", "Confirmed withdrawal cannot be cancelled.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(quote);
        }
        catch (DomainException)
        {
            await tx.CommitAsync(ct);
            throw;
        }
    }

    public async Task ExpireAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CashTransactionGate.AcquireAsync(db, ct);
        await ReleaseExpiredInsideAsync(clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Cleanup entities must not survive a later transaction rollback in the same request scope.
        db.ChangeTracker.Clear();
    }

    private async Task ReleaseExpiredInsideAsync(DateTimeOffset now, CancellationToken ct)
    {
        var expired = await db.WithdrawalQuotes.Where(q => q.Status == "Open" && q.ExpiresAt <= now)
            .OrderBy(q => q.AtmId).ThenBy(q => q.Id).ToArrayAsync(ct);
        foreach (var q in expired) await ReleaseAsync(q, "Expired", now, ct);
        // Flush terminal statuses so limit queries in this transaction see them.
        await db.SaveChangesAsync(ct);
    }

    internal async Task CancelAtmInsideAsync(Guid atmId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var quotes = await db.WithdrawalQuotes.Where(q => q.AtmId == atmId && q.Status == "Open").ToArrayAsync(ct);
        foreach (var q in quotes) await ReleaseAsync(q, q.ExpiresAt <= now ? "Expired" : "Cancelled", now, ct);
    }

    private async Task ReleaseAsync(WithdrawalQuote q, string status, DateTimeOffset now, CancellationToken ct)
    {
        var atm = await LoadAtmAsync(q.AtmId, ct);
        db.Movements.AddRange(atm.ChangeWithdrawalCash(q.Currency, Reservations(q), null, true, q.Id, now));
        q.Status = status;
    }

    private async Task<int> RemainingAsync(string hash, Currency currency, DateTimeOffset now, CancellationToken ct, Guid? excluding = null)
    {
        var local = TimeZoneInfo.ConvertTime(now, BusinessZone);
        var dayStart = new DateTimeOffset(local.Date, BusinessZone.GetUtcOffset(local.Date)).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);
        var used = await db.WithdrawalQuotes.Where(q => q.CardHash == hash && q.Currency == currency &&
            q.Status == "Confirmed" && q.ConfirmedAt >= dayStart && q.ConfirmedAt < dayEnd).SumAsync(q => q.ConfirmedAmount, ct);
        // A live quote spanning midnight consumes the current day's capacity until it closes.
        var reserved = await db.WithdrawalQuotes.Where(q => q.CardHash == hash && q.Currency == currency &&
            q.Status == "Open" && q.ExpiresAt > now && q.Id != excluding).SumAsync(q => q.LimitReservation, ct);
        return Math.Max(0, (currency == Currency.TRY ? 20_000 : 1_000) - used - reserved);
    }

    internal async Task<Atm> LoadAtmAsync(Guid id, CancellationToken ct)
    {
        var atm = await db.Atms.FromSqlInterpolated($"SELECT * FROM cash.atms WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new DomainException("ATM_NOT_FOUND", "ATM does not exist.", ErrorKind.NotFound);
        await db.Entry(atm).Collection(a => a.Cassettes).Query().Include(c => c.Balances).LoadAsync(ct);
        return atm;
    }
    private async Task<WithdrawalQuote> FindAsync(Guid atmId, Guid quoteId, CancellationToken ct) =>
        await db.WithdrawalQuotes.SingleOrDefaultAsync(q => q.Id == quoteId && q.AtmId == atmId, ct) ?? throw Missing();
    private static DomainException Missing() => new("QUOTE_NOT_FOUND", "Quote does not exist at this ATM.", ErrorKind.NotFound);
    private static DomainException Conflict(string code, string detail) => new(code, detail, ErrorKind.Conflict);
    private static QuoteOption[] Options(WithdrawalQuote q) => JsonSerializer.Deserialize<QuoteOption[]>(q.OptionsJson, Json)!;
    private static DispenseLine[] Reservations(WithdrawalQuote q) => JsonSerializer.Deserialize<DispenseLine[]>(q.ReservationsJson, Json)!;
    private static QuoteView View(WithdrawalQuote q) => new(q.Id, q.AtmId, q.Currency, q.Status.ToUpperInvariant(), q.CreatedAt, q.ExpiresAt, Options(q), q.SelectedOption);
    private static ConfirmationResult Problem(Guid atmId, Guid quoteId, string code, string detail, int status = 409) =>
        new(status, JsonSerializer.Serialize(new { type = "about:blank", title = status switch { 400 => "Invalid request", 404 => "Not found", _ => "Conflict" }, status, detail, code,
            instance = $"/api/atms/{atmId}/withdrawals/quotes/{quoteId}/confirm", traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N") }, Json));
}
