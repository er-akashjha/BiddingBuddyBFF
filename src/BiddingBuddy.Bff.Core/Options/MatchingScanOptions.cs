namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Config for the scheduled tender-alert scan (the cron that turns newly-added
/// tenders into per-org digest emails). Bound from the "Matching" section.
/// </summary>
public class MatchingScanOptions
{
    public const string Section = "Matching";

    /// <summary>Master switch for the background scan worker.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker scans for not-yet-evaluated tenders. Floored at 60s.</summary>
    public int ScanIntervalSeconds { get; set; } = 900;   // 15 min

    /// <summary>How many tenders to pull + evaluate per DB round-trip inside one run.</summary>
    public int BatchSize { get; set; } = 200;
}
