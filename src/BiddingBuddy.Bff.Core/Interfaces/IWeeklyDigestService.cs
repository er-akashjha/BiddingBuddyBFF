namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Builds and sends the per-org weekly summary (open bids, due-this-week, overdue, won) to
/// owners + admins. Deduped to once per ISO week per org via the notification_reminders ledger,
/// so the driving worker can tick more often than weekly without double-sending.
/// </summary>
public interface IWeeklyDigestService
{
    Task<int> RunAsync(CancellationToken ct = default);
}
