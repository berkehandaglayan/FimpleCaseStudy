using FimpleCaseStudy.Application.Atms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FimpleCaseStudy.Infrastructure.Persistence;

internal sealed class LoggingLowCashAlertSink(ILogger<LoggingLowCashAlertSink> logger) : ILowCashAlertSink
{
    public Task PublishAsync(LowCashAlertView alert, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        logger.LogWarning("Low-cash alert {EventId}: ATM {AtmId}, cassette {Slot}, {Currency} {Denomination}, available {Available}, threshold {Threshold}, operation {OperationId}.",
            alert.Id, alert.AtmId, alert.Slot, alert.Currency, alert.Denomination, alert.Available, alert.Threshold, alert.OperationId);
        return Task.CompletedTask;
    }
}

internal sealed class OutboxDispatcher(CashManagementDbContext db, ILowCashAlertSink sink, TimeProvider clock) : IOutboxDispatcher
{
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var processed = 0;
        for (var i = 0; i < batchSize; i++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = Now();
            // Each instance holds a row lock while publishing; other workers skip it.
            var alert = await db.LowCashAlerts.FromSqlInterpolated($"""
                SELECT * FROM cash.outbox WHERE "SentAt" IS NULL AND "NextAttemptAt" <= {now}
                ORDER BY "OccurredAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (alert is null) break;
            alert.Attempt();
            try
            {
                await sink.PublishAsync(alert.ToView(), ct);
                alert.MarkSent(Now());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Never persist/log exception messages that could contain sensitive publisher data.
                alert.RetryAt(Now());
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            processed++;
        }
        return processed;
    }

    private DateTimeOffset Now() => new(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero);
}
