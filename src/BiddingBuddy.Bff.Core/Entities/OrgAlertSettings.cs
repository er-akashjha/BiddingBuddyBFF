namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Per-org delivery preferences for tender-match digests. One row per org.
/// <see cref="DigestSize"/> is the batch threshold — matches accumulate in
/// <see cref="TenderMatch"/> (status=pending) and are flushed as a single
/// notification once this many are buffered (or by the daily time-fallback).
/// </summary>
public class OrgAlertSettings
{
    public Guid OrgId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DigestSize { get; set; } = 10;
    public string[] NotifyChannels { get; set; } = ["Email", "InApp"];
    public string[] NotifyRoles { get; set; } = ["owner", "admin", "bid_manager"];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
