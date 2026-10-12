using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed record ReconciliationRequest(IReadOnlyList<CassetteCount>? Cassettes);
public sealed record ReconciliationView(Guid OperationId, Guid AtmId, string Actor, DateTimeOffset OccurredAt,
    IReadOnlyList<ReconciliationLine> Lines, ReconciliationStatus Status, decimal? TotalDifferenceTry,
    IReadOnlyList<ExchangeRateSnapshot>? ExchangeRates, string? ApprovedBy, DateTimeOffset? ApprovedAt);
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
    Task<ReconciliationView> ReconcileAsync(Guid id, ReconciliationRequest request, string actor, CancellationToken ct);
    Task<ReconciliationView> GetReconciliationAsync(Guid id, Guid operationId, CancellationToken ct);
    Task<ReconciliationView> ApproveAsync(Guid id, Guid operationId, string actor, CancellationToken ct);
    Task<IReadOnlyList<ExchangeRateSnapshot>> GetExchangeRatesAsync(CancellationToken ct);
    Task<AtmCashReport> ReportAsync(Guid id, CancellationToken ct);
    Task<ConsistencyReport> CheckConsistencyAsync(Guid id, CancellationToken ct);
}
