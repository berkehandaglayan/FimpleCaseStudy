using FimpleCaseStudy.Application.Withdrawals;

namespace FimpleCaseStudy.Api;

internal sealed class QuoteExpiryWorker(IServiceScopeFactory scopes, ILogger<QuoteExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IWithdrawalService>().ExpireAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Never include request data or SQL parameters in withdrawal logs.
                logger.LogWarning("Quote expiry failed; the next worker tick will retry.");
            }
        }
    }
}
