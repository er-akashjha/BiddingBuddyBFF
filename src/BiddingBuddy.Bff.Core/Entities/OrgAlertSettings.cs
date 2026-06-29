namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Per-org delivery preferences for tender-match digests. One row per org.
/// Matches accumulate in <see cref="TenderMatch"/> (status=pending) and are flushed
/// as a single grouped digest no more often than every
/// <see cref="MinSendIntervalMinutes"/> (a cooldown); <see cref="LastDigestSentAt"/>
/// records the most recent send. <see cref="DigestSize"/> is retained for the UI but
/// no longer gates sending — the cooldown does.
/// </summary>
public class OrgAlertSettings
{
    public Guid OrgId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DigestSize { get; set; } = 10;

    /// <summary>Smallest gap between two digest emails for this org (default 6 h).</summary>
    public int MinSendIntervalMinutes { get; set; } = 360;

    /// <summary>Server-managed timestamp of the most recent digest send (NULL = never sent).</summary>
    public DateTime? LastDigestSentAt { get; set; }

    public string[] NotifyChannels { get; set; } = ["Email", "InApp"];
    public string[] NotifyRoles { get; set; } = ["owner", "admin", "bid_manager"];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
