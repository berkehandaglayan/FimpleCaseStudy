using System.Data;
using FimpleCaseStudy.Application.Atms;
using FimpleCaseStudy.Domain;
using FimpleCaseStudy.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace FimpleCaseStudy.Infrastructure.Persistence;

internal sealed class CashAuditService(CashManagementDbContext db, TimeProvider clock) : ICashAuditService
{
    public async Task<ReconciliationReceipt> ReconcileAsync(Guid id, ReconciliationRequest request, string actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CashTransactionGate.AcquireAsync(db, ct);
        var atm = await db.Atms.FromSqlInterpolated($"SELECT * FROM cash.atms WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw MissingAtm();
        await db.Entry(atm).Collection(a => a.Cassettes).Query().Include(c => c.Balances).LoadAsync(ct);
        var change = atm.Reconcile(request.Cassettes, actor, clock.GetUtcNow());
        db.Reconciliations.Add(ReconciliationRecord.From(change.Receipt));
        db.Movements.AddRange(change.Movements);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return change.Receipt;
    }

    public async Task<ReconciliationReceipt> GetReconciliationAsync(Guid id, Guid operationId, CancellationToken ct)
    {
        var record = await db.Reconciliations.AsNoTracking().SingleOrDefaultAsync(r => r.AtmId == id && r.Id == operationId, ct)
            ?? throw new DomainException("RECONCILIATION_NOT_FOUND", "Reconciliation does not exist for this ATM.", ErrorKind.NotFound);
        return record.ToReceipt();
    }

    public async Task<AtmCashReport> ReportAsync(Guid id, CancellationToken ct)
    {
        var atm = await ReadAtmAsync(id, ct);
        var view = AtmView.From(atm);
        var notes = view.Cassettes.SelectMany(c => c.Balances).GroupBy(b => (b.Currency, b.Denomination))
            .OrderBy(g => g.Key.Currency).ThenBy(g => g.Key.Denomination)
            .Select(g => new BalanceView(g.Key.Currency, g.Key.Denomination,
                g.Sum(b => b.OnHand), g.Sum(b => b.Reserved), g.Sum(b => b.Available))).ToArray();
        return new(id, atm.Name, atm.State, view.Cassettes.Select(c => new CassetteReport(c, Totals(c.Balances))).ToArray(),
            notes, Totals(notes));
    }

    public async Task<ConsistencyReport> CheckConsistencyAsync(Guid id, CancellationToken ct)
    {
        // Both queries see the same committed PostgreSQL snapshot despite concurrent cash changes.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var atm = await ReadAtmAsync(id, ct);
        var ledger = await db.Movements.AsNoTracking().Where(m => m.AtmId == id)
            .GroupBy(m => new { m.CassetteId, m.Currency, m.Denomination })
            .Select(g => new { g.Key, OnHand = g.Sum(m => (long)m.PhysicalDelta), Reserved = g.Sum(m => (long)m.ReservedDelta) })
            .ToArrayAsync(ct);
        var lines = new List<ConsistencyLine>();
        foreach (var cassette in atm.Cassettes.OrderBy(c => c.Slot))
        {
            var keys = cassette.Balances.Select(b => (b.Currency, b.Denomination))
                .Union(ledger.Where(m => m.Key.CassetteId == cassette.Id).Select(m => (m.Key.Currency, m.Key.Denomination)))
                .OrderBy(k => k.Currency).ThenBy(k => k.Denomination);
            foreach (var key in keys)
            {
                var balance = cassette.Balances.SingleOrDefault(b => b.Currency == key.Currency && b.Denomination == key.Denomination);
                var summed = ledger.SingleOrDefault(m => m.Key.CassetteId == cassette.Id && m.Key.Currency == key.Currency && m.Key.Denomination == key.Denomination);
                lines.Add(new(cassette.Id, cassette.Slot, key.Currency, key.Denomination,
                    balance?.OnHand ?? 0, balance?.Reserved ?? 0, summed?.OnHand ?? 0, summed?.Reserved ?? 0));
            }
        }
        await transaction.CommitAsync(ct);
        return new(id, lines.All(l => l.IsConsistent), lines.AsReadOnly());
    }

    private async Task<Atm> ReadAtmAsync(Guid id, CancellationToken ct) =>
        await db.Atms.AsNoTracking().Include(a => a.Cassettes).ThenInclude(c => c.Balances)
            .AsSingleQuery().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw MissingAtm();

    private static CurrencyTotals[] Totals(IEnumerable<BalanceView> balances)
    {
        var notes = balances.ToArray();
        return Enum.GetValues<Currency>().Select(currency =>
        {
            var matching = notes.Where(n => n.Currency == currency).ToArray();
            return new CurrencyTotals(currency, matching.Sum(n => n.OnHand), matching.Sum(n => n.Reserved), matching.Sum(n => n.Available),
                matching.Sum(n => (decimal)n.Denomination * n.OnHand), matching.Sum(n => (decimal)n.Denomination * n.Reserved),
                matching.Sum(n => (decimal)n.Denomination * n.Available));
        }).ToArray();
    }
    private static DomainException MissingAtm() => new("ATM_NOT_FOUND", "ATM does not exist.", ErrorKind.NotFound);
}
