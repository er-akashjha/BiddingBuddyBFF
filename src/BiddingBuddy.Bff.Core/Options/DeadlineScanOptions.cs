namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Config for the scheduled deadline / expiry notification scan (the background job
/// that turns approaching/passed dates on bids, invoices, compliance docs, delivery
/// milestones and EMDs into in-app + email reminders). Bound from the "DeadlineScan" section.
/// </summary>
public class DeadlineScanOptions
{
    public const string Section = "DeadlineScan";

    /// <summary>Master switch for the background scan worker.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker runs. Floored at 60s. Default hourly — deadlines move in days.</summary>
    public int ScanIntervalSeconds { get; set; } = 3600;

    /// <summary>Lead window (days) for the "bid submission due soon" reminder.</summary>
    public int BidDueLeadDays { get; set; } = 3;

    /// <summary>Lead window (days) for the "invoice due soon" reminder.</summary>
    public int InvoiceDueLeadDays { get; set; } = 7;

    /// <summary>Lead window (days) for the "compliance document expiring" reminder.</summary>
    public int ComplianceExpiryLeadDays { get; set; } = 30;

    /// <summary>An EMD held at least this many days (since payment) is flagged as stuck working capital.</summary>
    public int EmdStuckDays { get; set; } = 90;
}
