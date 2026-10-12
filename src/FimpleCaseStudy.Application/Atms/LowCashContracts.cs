using FimpleCaseStudy.Domain.Cash;

namespace FimpleCaseStudy.Application.Atms;

public sealed record LowCashAlertView(Guid Id, Guid AtmId, Guid CassetteId, int Slot, Currency Currency,
    int Denomination, int BeforeAvailable, int Available, int Threshold, Guid OperationId, string Reason,
    DateTimeOffset OccurredAt, int Attempts, DateTimeOffset? SentAt);

public interface ILowCashAlertSink
{
    Task PublishAsync(LowCashAlertView alert, CancellationToken ct);
}
public interface IOutboxDispatcher
{
    Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct);
}
