namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Per-org AI metering. The UNIQUE(org, feature, resource, period_month) constraint IS
/// the consume operation: insert-on-conflict, rowcount 0 = already unlocked this month
/// = free re-view. `Feature`/`ResourceId` are generic so future per-resource unlocks
/// (per-tender enrichment purchase, document Q&A) reuse this table.
/// </summary>
public class AiUsageRecord
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? UserId { get; set; }
    public string Feature { get; set; } = default!;      // v1: "ai_summary"
    public string ResourceId { get; set; } = default!;   // v1: tender Mongo id
    /// <summary>First day of the IST calendar month the usage counts against.</summary>
    public DateOnly PeriodMonth { get; set; }
    public DateTime OccurredAt { get; set; }
}
