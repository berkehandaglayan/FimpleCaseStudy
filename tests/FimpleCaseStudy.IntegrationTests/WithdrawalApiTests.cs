using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FimpleCaseStudy.Application.Withdrawals;
using FimpleCaseStudy.Domain.Cash;
using FimpleCaseStudy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.IntegrationTests;

public sealed class WithdrawalApiTests : IAsyncLifetime
{
    private const string Card = "9900000000000050";
    private readonly AdjustableClock clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly ApiFixture fixture;
    public WithdrawalApiTests() => fixture = new(clock);
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private HttpClient Client => fixture.Client;
    private static string Path(Guid atm, Guid quote) => $"/api/atms/{atm}/withdrawals/quotes/{quote}";

    private async Task<Guid> SetupAsync(int hundred = 10, int fifty = 0, int twenty = 10,
        int foreign = 10, int firstDenomination = 100, string foreignCurrency = "USD")
    {
        var created = await Client.PostAsJsonAsync("/api/atms", new { name = "Withdrawal ATM" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var atm = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var configured = await Client.PutAsJsonAsync($"/api/atms/{atm}/configuration", new { cassettes = new[]
        {
            new { slot = 1, currency = "TRY", denomination = firstDenomination },
            new { slot = 2, currency = "TRY", denomination = 50 },
            new { slot = 3, currency = "TRY", denomination = 20 },
            new { slot = 4, currency = foreignCurrency, denomination = 100 }
        }});
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        var lines = new[]
        {
            new { slot = 1, currency = "TRY", denomination = firstDenomination, count = hundred },
            new { slot = 2, currency = "TRY", denomination = 50, count = fifty },
            new { slot = 3, currency = "TRY", denomination = 20, count = twenty },
            new { slot = 4, currency = foreignCurrency, denomination = 100, count = foreign }
        }.Where(l => l.count > 0).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"/api/atms/{atm}/load", new { lines })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(atm, "IN_SERVICE")).StatusCode);
        return atm;
    }
    private Task<HttpResponseMessage> StateAsync(Guid atm, string state) => Client.PostAsJsonAsync($"/api/atms/{atm}/state", new { state });
    private Task<HttpResponseMessage> QuoteAsync(Guid atm, int amount, string currency = "TRY", string card = Card) =>
        Client.PostAsJsonAsync($"/api/atms/{atm}/withdrawals/quotes", new { cardNumber = card, currency, amount });
    private async Task<JsonElement> QuoteBodyAsync(Guid atm, int amount, string currency = "TRY")
    {
        var response = await QuoteAsync(atm, amount, currency);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private Task<HttpResponseMessage> ConfirmAsync(Guid atm, Guid quote, string option = "exact", string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path(atm, quote) + "/confirm")
        { Content = JsonContent.Create(new { option }) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return Client.SendAsync(request);
    }
    private async Task<(int OnHand, int Reserved)> CashAsync(Guid atm, int slot)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var entity = await db.Atms.Include(a => a.Cassettes).ThenInclude(c => c.Balances).SingleAsync(a => a.Id == atm);
        var cassette = entity.Cassettes.Single(c => c.Slot == slot);
        var movements = await db.Movements.Where(m => m.AtmId == atm && m.CassetteId == cassette.Id).ToArrayAsync();
        Assert.Equal(cassette.OnHand, movements.Sum(m => m.PhysicalDelta));
        Assert.Equal(cassette.Reserved, movements.Sum(m => m.ReservedDelta));
        return (cassette.OnHand, cassette.Reserved);
    }
    private static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        Assert.True(json.TryGetProperty("instance", out _));
    }

    [Fact]
    public async Task Alternatives_reserve_per_cassette_maximum_and_confirm_releases_unused_cash()
    {
        var atm = await SetupAsync(hundred: 1, twenty: 2);
        var quote = await QuoteBodyAsync(atm, 130);
        Assert.Equal(60, (quote.GetProperty("expiresAt").GetDateTimeOffset() - quote.GetProperty("createdAt").GetDateTimeOffset()).TotalSeconds);
        var options = quote.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(new[] { 120, 140 }, options.Select(o => o.GetProperty("plan").GetProperty("amount").GetInt32()));
        Assert.Equal((1, 1), await CashAsync(atm, 1));
        Assert.Equal((2, 2), await CashAsync(atm, 3));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(atm, quote.GetProperty("id").GetGuid(), "lower")).StatusCode);
        Assert.Equal((0, 0), await CashAsync(atm, 1));
        Assert.Equal((1, 0), await CashAsync(atm, 3));
    }

    [Fact]
    public async Task Parallel_quotes_do_not_reserve_the_same_notes()
    {
        var atm = await SetupAsync(hundred: 1, twenty: 0, foreign: 0);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => QuoteAsync(atm, 100)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal((1, 1), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Parallel_confirms_with_distinct_keys_dispense_once()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ConfirmAsync(atm, quote)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal((9, 0), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Concurrent_same_key_replays_identical_bytes_and_restart_keeps_result()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ConfirmAsync(atm, quote, key: "persistent-key")));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        await fixture.RestartApiAsync();
        var replay = await ConfirmAsync(atm, quote, key: "persistent-key");
        Assert.Equal(bodies[0], await replay.Content.ReadAsStringAsync());
        var conflict = await ConfirmAsync(atm, quote, "upper", "persistent-key");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertCodeAsync(conflict, "IDEMPOTENCY_CONFLICT");
        Assert.Equal((9, 0), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Cancel_and_expiry_restore_stock_and_limit_without_physical_withdrawal()
    {
        var atm = await SetupAsync(hundred: 100, twenty: 0, firstDenomination: 500);
        var quote = (await QuoteBodyAsync(atm, 20_000)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await QuoteAsync(atm, 500)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync(Path(atm, quote) + "/cancel", null)).StatusCode);
        Assert.Equal((100, 0), await CashAsync(atm, 1));
        var second = (await QuoteBodyAsync(atm, 20_000)).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromSeconds(60));
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWithdrawalService>().ExpireAsync(default);
        Assert.Equal((100, 0), await CashAsync(atm, 1));
        var confirmed = await ConfirmAsync(atm, second);
        Assert.Equal(HttpStatusCode.Conflict, confirmed.StatusCode);
        await AssertCodeAsync(confirmed, "QUOTE_EXPIRED");
        Assert.Equal(HttpStatusCode.Created, (await QuoteAsync(atm, 20_000)).StatusCode);
    }

    [Theory]
    [InlineData("TRY", 20_000, 500)]
    [InlineData("USD", 1_000, 100)]
    [InlineData("EUR", 1_000, 100)]
    public async Task Global_card_limit_includes_active_quotes_at_other_atms(string currency, int limit, int denomination)
    {
        var a = await SetupAsync(hundred: 100, twenty: 0, foreign: 100, firstDenomination: denomination,
            foreignCurrency: currency == "TRY" ? "USD" : currency);
        var b = await SetupAsync(hundred: 100, twenty: 0, foreign: 100, firstDenomination: denomination,
            foreignCurrency: currency == "TRY" ? "USD" : currency);
        var responses = await Task.WhenAll(QuoteAsync(a, limit, currency), QuoteAsync(b, limit, currency));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var denied = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await AssertCodeAsync(denied, "DAILY_LIMIT_EXCEEDED");
        var successful = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var quote = await successful.Content.ReadFromJsonAsync<JsonElement>();
        var owner = quote.GetProperty("atmId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(owner, quote.GetProperty("id").GetGuid())).StatusCode);
        await AssertCodeAsync(await QuoteAsync(owner == a ? b : a, denomination, currency), "DAILY_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Midnight_keeps_sixty_seconds_and_charges_confirmation_to_Istanbul_business_day()
    {
        clock.Set(new DateTimeOffset(2026, 10, 12, 20, 59, 40, TimeSpan.Zero)); // 23:59:40 Istanbul
        var a = await SetupAsync(hundred: 100, twenty: 0, firstDenomination: 500);
        var b = await SetupAsync(hundred: 100, twenty: 0, firstDenomination: 500);
        var old = (await QuoteBodyAsync(a, 19_500)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(a, old)).StatusCode);
        var spanning = (await QuoteBodyAsync(a, 500)).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromSeconds(30));
        // Previous day's completed usage is gone; the still-live reservation remains.
        var today = (await QuoteBodyAsync(b, 19_500)).GetProperty("id").GetGuid();
        await AssertCodeAsync(await QuoteAsync(b, 500), "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(a, spanning)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(b, today)).StatusCode);
        await AssertCodeAsync(await QuoteAsync(b, 500), "DAILY_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Maintenance_cancels_open_quotes_and_releases_reservations_atomically()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 130)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await StateAsync(atm, "MAINTENANCE")).StatusCode);
        Assert.Equal((10, 0), await CashAsync(atm, 1));
        Assert.Equal((10, 0), await CashAsync(atm, 3));
        await AssertCodeAsync(await ConfirmAsync(atm, quote, "lower"), "QUOTE_CANCELLED");
    }

    [Fact]
    public async Task Confirm_waiting_for_database_lock_rechecks_expiry_after_acquisition()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(507050)");
        var pending = ConfirmAsync(atm, quote);
        await Task.Delay(100);
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(60));
        await tx.CommitAsync();
        var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCodeAsync(response, "QUOTE_EXPIRED");
        Assert.Equal((10, 0), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Expired_cleanup_survives_later_rejected_quote_and_new_quote_reuses_stock()
    {
        var atm = await SetupAsync(hundred: 1, twenty: 0, foreign: 0);
        await QuoteBodyAsync(atm, 100);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.Conflict, (await QuoteAsync(atm, 20_001)).StatusCode);
        Assert.Equal((1, 0), await CashAsync(atm, 1));
        Assert.Equal(HttpStatusCode.Created, (await QuoteAsync(atm, 100)).StatusCode);
    }

    [Fact]
    public async Task Confirm_vs_expiry_and_maintenance_races_have_one_terminal_result()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromSeconds(60));
        await using var expiryScope = fixture.Factory.Services.CreateAsyncScope();
        var expiry = expiryScope.ServiceProvider.GetRequiredService<IWithdrawalService>().ExpireAsync(default);
        var results = await Task.WhenAll(ConfirmAsync(atm, quote), StateAsync(atm, "MAINTENANCE"));
        await expiry;
        Assert.Equal(HttpStatusCode.Conflict, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
        Assert.Equal((10, 0), await CashAsync(atm, 1));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        Assert.Equal("Expired", (await db.WithdrawalQuotes.SingleAsync()).Status);
        Assert.Single(await db.Movements.Where(m => m.Type == MovementType.ReservationRelease).ToArrayAsync());
    }

    [Fact]
    public async Task Card_identity_is_HMAC_only_and_invalid_input_has_no_card_echo()
    {
        var atm = await SetupAsync();
        await QuoteBodyAsync(atm, 100);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        var quote = await db.WithdrawalQuotes.SingleAsync();
        Assert.Equal(scope.ServiceProvider.GetRequiredService<CardIdentity>().Hash(Card), quote.CardHash);
        Assert.Equal(64, quote.CardHash.Length);
        Assert.DoesNotContain(Card, JsonSerializer.Serialize(quote));
        Assert.DoesNotContain(Card, JsonSerializer.Serialize(await db.Movements.ToArrayAsync()));
        var invalid = await QuoteAsync(atm, 100, card: Card + "x");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(Card, await invalid.Content.ReadAsStringAsync());
        Assert.All(fixture.Logs.Messages, message => Assert.DoesNotContain(Card, message));
    }

    [Fact]
    public async Task Invalid_option_error_is_persisted_and_replayed_after_restart()
    {
        var atm = await SetupAsync();
        var quote = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        var result = await ConfirmAsync(atm, quote, "unknown", "invalid-option");
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        await AssertCodeAsync(result, "INVALID_QUOTE_OPTION");
        var first = await result.Content.ReadAsStringAsync();
        await fixture.RestartApiAsync();
        var replay = await ConfirmAsync(atm, quote, "unknown", "invalid-option");
        Assert.Equal(first, await replay.Content.ReadAsStringAsync());
        Assert.Equal((10, 1), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Hosted_worker_expires_quote_without_an_HTTP_request()
    {
        var atm = await SetupAsync();
        var id = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromSeconds(60));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string status;
        do
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
            status = await db.WithdrawalQuotes.Where(q => q.Id == id).Select(q => q.Status).SingleAsync();
            if (status == "Expired") break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal("Expired", status);
        Assert.Equal((10, 0), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Failed_withdrawal_ledger_insert_rolls_back_quote_cash_and_idempotency_then_retry_succeeds()
    {
        var atm = await SetupAsync();
        var id = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CashManagementDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cash.reject_withdrawal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Type" = 'Withdrawal' THEN RAISE EXCEPTION 'forced failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_withdrawal BEFORE INSERT ON cash.movements FOR EACH ROW EXECUTE FUNCTION cash.reject_withdrawal();
            """);
        var failure = await ConfirmAsync(atm, id, key: "rollback-key");
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("forced failure", await failure.Content.ReadAsStringAsync());
        Assert.Equal((10, 1), await CashAsync(atm, 1));
        Assert.Equal("Open", (await db.WithdrawalQuotes.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.IdempotencyRecords.ToArrayAsync());
        Assert.Empty(await db.Movements.Where(m => m.Type == MovementType.Withdrawal || m.Type == MovementType.ReservationRelease).ToArrayAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_withdrawal ON cash.movements; DROP FUNCTION cash.reject_withdrawal();");
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(atm, id, key: "rollback-key")).StatusCode);
        Assert.Equal((9, 0), await CashAsync(atm, 1));
        Assert.Single(await db.IdempotencyRecords.ToArrayAsync());
    }

    [Theory]
    [InlineData(59, HttpStatusCode.OK)]
    [InlineData(60, HttpStatusCode.Conflict)]
    public async Task Expiry_is_exclusive_at_exactly_sixty_seconds(int seconds, HttpStatusCode expected)
    {
        var atm = await SetupAsync();
        var id = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, (await ConfirmAsync(atm, id)).StatusCode);
        Assert.Equal(seconds == 59 ? (9, 0) : (10, 0), await CashAsync(atm, 1));
    }

    [Fact]
    public async Task Missing_quote_response_is_persisted_and_key_cannot_be_reused_for_another_quote()
    {
        var atm = await SetupAsync();
        var missing = Guid.NewGuid();
        var first = await ConfirmAsync(atm, missing, key: "missing-quote");
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        await AssertCodeAsync(first, "QUOTE_NOT_FOUND");
        var body = await first.Content.ReadAsStringAsync();
        await fixture.RestartApiAsync();
        Assert.Equal(body, await (await ConfirmAsync(atm, missing, key: "missing-quote")).Content.ReadAsStringAsync());
        var existing = (await QuoteBodyAsync(atm, 100)).GetProperty("id").GetGuid();
        var conflict = await ConfirmAsync(atm, existing, key: "missing-quote");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertCodeAsync(conflict, "IDEMPOTENCY_CONFLICT");
        Assert.Equal((10, 1), await CashAsync(atm, 1));
    }
}

internal sealed class AdjustableClock(DateTimeOffset initial) : TimeProvider
{
    private long utcTicks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
    public void Advance(TimeSpan amount) => Interlocked.Add(ref utcTicks, amount.Ticks);
    public void Set(DateTimeOffset value) => Interlocked.Exchange(ref utcTicks, value.UtcTicks);
}
