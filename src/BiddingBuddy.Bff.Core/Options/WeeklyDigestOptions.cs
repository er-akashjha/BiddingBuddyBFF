namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Config for the weekly org-summary digest worker. Bound from the "WeeklyDigest" section.
/// The digest itself is deduped per ISO week per org, so the interval only controls how
/// promptly a new week's digest goes out — not how often orgs are emailed.
/// </summary>
public class WeeklyDigestOptions
{
    public const string Section = "WeeklyDigest";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker checks whether the current week's digest is due. Floored at 60s. Default 6h.</summary>
    public int ScanIntervalSeconds { get; set; } = 21_600;
}
