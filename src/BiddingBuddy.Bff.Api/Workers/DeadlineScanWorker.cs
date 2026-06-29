using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Workers;

/// <summary>
/// Periodically scans org-scoped entities for approaching / passed deadlines (bid
/// submissions, invoices, compliance certificates, delivery milestones, stuck EMDs) and
/// fires one-time in-app + email reminders. Modelled on <see cref="TenderMatchScanWorker"/>:
/// in-process, idempotent (the <c>notification_reminders</c> ledger dedups), and resilient —
/// a bad run (e.g. migration not applied yet) never kills the worker.
/// </summary>
public class DeadlineScanWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DeadlineScanOptions> options,
    ILogger<DeadlineScanWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            log.LogInformation("[DeadlineScan] Scheduled deadline scan is disabled (DeadlineScan:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, opt.ScanIntervalSeconds));
        log.LogInformation("[DeadlineScan] Scheduled deadline scan started — every {Interval}.", interval);

        // Small startup grace so the host + DB connection are fully ready.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var scanner = scope.ServiceProvider.GetRequiredService<IDeadlineScanService>();
                    await scanner.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[DeadlineScan] Scan run failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — normal.
        }

        log.LogInformation("[DeadlineScan] Scheduled deadline scan stopped.");
    }
}
