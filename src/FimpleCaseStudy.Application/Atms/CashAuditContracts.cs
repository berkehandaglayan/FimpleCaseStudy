using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed record ReconciliationRequest(IReadOnlyList<CassetteCount>? Cassettes);
public sealed record CurrencyTotals(Currency Currency, int OnHandNotes, int ReservedNotes, int AvailableNotes,
    decimal OnHandAmount, decimal ReservedAmount, decimal AvailableAmount);
public sealed record CassetteReport(CassetteView Cassette, IReadOnlyList<CurrencyTotals> Totals);
public sealed record AtmCashReport(Guid AtmId, string Name, AtmState State,
    IReadOnlyList<CassetteReport> Cassettes, IReadOnlyList<BalanceView> Notes, IReadOnlyList<CurrencyTotals> Totals);
public sealed record ConsistencyLine(Guid CassetteId, int Slot, Currency Currency, int Denomination,
    int StoredOnHand, int StoredReserved, long LedgerOnHand, long LedgerReserved)
{
    public bool IsConsistent => StoredOnHand == LedgerOnHand && StoredReserved == LedgerReserved;
}
public sealed record ConsistencyReport(Guid AtmId, bool IsConsistent, IReadOnlyList<ConsistencyLine> Lines);

public interface ICashAuditService
{
    Task<ReconciliationReceipt> ReconcileAsync(Guid id, ReconciliationRequest request, string actor, CancellationToken ct);
    Task<ReconciliationReceipt> GetReconciliationAsync(Guid id, Guid operationId, CancellationToken ct);
    Task<AtmCashReport> ReportAsync(Guid id, CancellationToken ct);
    Task<ConsistencyReport> CheckConsistencyAsync(Guid id, CancellationToken ct);
}
