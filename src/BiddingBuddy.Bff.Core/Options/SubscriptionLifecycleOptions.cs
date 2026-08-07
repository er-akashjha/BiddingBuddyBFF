namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Config for the subscription lifecycle worker (trial-ending nudges, renewal reminders,
/// status transitions). Bound from the "SubscriptionLifecycle" section.
///
/// Each email is deduped by its own stamp column on org_subscriptions, so the interval
/// controls promptness only — running hourly does not email anyone hourly.
/// </summary>
public class SubscriptionLifecycleOptions
{
    public const string Section = "SubscriptionLifecycle";

    public bool Enabled { get; set; } = true;

    /// <summary>How often to scan. Floored at 60s. Default 1h.</summary>
    public int ScanIntervalSeconds { get; set; } = 3_600;
}
