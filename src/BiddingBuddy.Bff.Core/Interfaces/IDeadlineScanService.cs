namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>Outcome of one deadline-scan run.</summary>
/// <param name="Sent">How many reminder notifications were dispatched this run.</param>
/// <param name="Skipped">True if another scan was already running and this run was a no-op.</param>
public record DeadlineScanResult(int Sent, bool Skipped = false);

/// <summary>
/// Scans org-scoped entities for due / overdue / expiring dates and publishes in-app +
/// email reminders. Each (entity, milestone) is deduped via the <c>notification_reminders</c>
/// ledger so a reminder fires exactly once, even though the scan runs on every tick.
/// Driven by <c>DeadlineScanWorker</c>; can also be invoked on demand.
/// </summary>
public interface IDeadlineScanService
{
    Task<DeadlineScanResult> RunAsync(CancellationToken ct = default);
}
