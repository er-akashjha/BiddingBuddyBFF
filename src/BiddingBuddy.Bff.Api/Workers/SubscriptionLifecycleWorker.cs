using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Workers;

/// <summary>
/// Drives trial-ending nudges, renewal reminders and subscription status transitions.
/// Each email is deduped by a stamp column on the subscription row, so an hourly tick
/// does not mean hourly email. Resilient — a bad run never kills the worker.
/// </summary>
public class SubscriptionLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionLifecycleOptions> options,
    ILogger<SubscriptionLifecycleWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            log.LogInformation("[Subscriptions] Disabled (SubscriptionLifecycle:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, opt.ScanIntervalSeconds));
        log.LogInformation("[Subscriptions] Lifecycle worker started — scanning every {Interval}.", interval);

        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ISubscriptionLifecycleService>();
                    await svc.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[Subscriptions] Run failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }

        log.LogInformation("[Subscriptions] Lifecycle worker stopped.");
    }
}
