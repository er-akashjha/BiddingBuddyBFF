using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Workers;

/// <summary>
/// Periodically scans tenders that haven't been evaluated for alerts yet
/// (<c>alerts_scanned_at IS NULL</c>), matches them against each org's interest
/// rules, and emails one digest per matched org. This is the scheduled half of the
/// tender-match feature — it replaced the old inline on-upsert matching so alerting
/// is decoupled from ingestion, batches naturally, and is idempotent across restarts.
///
/// The same logic is reachable on demand via <c>POST /internal/matching/scan</c>.
/// </summary>
public class TenderMatchScanWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MatchingScanOptions> options,
    ILogger<TenderMatchScanWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            log.LogInformation("[MatchScan] Scheduled tender-match scan is disabled (Matching:Enabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, opt.ScanIntervalSeconds));
        log.LogInformation("[MatchScan] Scheduled tender-match scan started — every {Interval}.", interval);

        // Small startup grace so the host + DB connection are fully ready.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var matching = scope.ServiceProvider.GetRequiredService<IMatchingService>();
                    var result = await matching.ScanNewTendersAsync(opt.BatchSize, rearmFirst: false, stoppingToken);

                    if (result.TendersScanned > 0 || result.OrgsNotified > 0)
                        log.LogInformation("[MatchScan] {Scanned} scanned, {Matches} matched, {Orgs} org-digests sent.",
                            result.TendersScanned, result.MatchesCreated, result.OrgsNotified);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A bad run (e.g. migration not applied yet) must not kill the worker.
                    log.LogError(ex, "[MatchScan] Scan run failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — normal.
        }

        log.LogInformation("[MatchScan] Scheduled tender-match scan stopped.");
    }
}
