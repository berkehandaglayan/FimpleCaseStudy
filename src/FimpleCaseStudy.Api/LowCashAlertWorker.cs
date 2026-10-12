using FimpleCaseStudy.Application.Atms;

namespace FimpleCaseStudy.Api;

public sealed class LowCashAlertWorker(IServiceScopeFactory scopes, ILogger<LowCashAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().ProcessBatchAsync(20, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Low-cash outbox processing failed; the next worker tick will retry."); }
        }
    }
}
