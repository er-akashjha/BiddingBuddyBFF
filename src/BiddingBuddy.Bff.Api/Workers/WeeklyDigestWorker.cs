using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Workers;

/// <summary>
/// Drives the weekly org-summary digest. Ticks a few times a day; the digest itself is deduped
/// to once per ISO week per org inside <see cref="IWeeklyDigestService"/>, so the cadence only
/// affects how promptly the new week's digest goes out. Resilient — a bad run never kills the worker.
/// </summary>
public class WeeklyDigestWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WeeklyDigestOptions> options,
    ILogger<WeeklyDigestWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            log.LogInformation("[WeeklyDigest] Disabled (WeeklyDigest:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, opt.ScanIntervalSeconds));
        log.LogInformation("[WeeklyDigest] Weekly digest worker started — checking every {Interval}.", interval);

        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IWeeklyDigestService>();
                    await svc.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[WeeklyDigest] Run failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }

        log.LogInformation("[WeeklyDigest] Weekly digest worker stopped.");
    }
}
