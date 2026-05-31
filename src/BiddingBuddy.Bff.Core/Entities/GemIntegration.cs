namespace BiddingBuddy.Bff.Core.Entities;

public class GemIntegration
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string GemSellerId { get; set; } = default!;
    public string? GemUsername { get; set; }
    public bool SyncEnabled { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string SyncStatus { get; set; } = "idle";  // idle|running|failed
    public string? SyncError { get; set; }
    public string? Preferences { get; set; }  // JSON: {categories, states, minValue}
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
