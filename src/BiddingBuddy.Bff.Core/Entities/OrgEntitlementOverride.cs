namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Manual per-org exceptions merged over the code catalog by PlanService.
/// Written by ops only (SQL / internal tooling). Keys: "seat_cap", "ai_quota",
/// "feature:&lt;name&gt;".
/// </summary>
public class OrgEntitlementOverride
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
    public string? Note { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
