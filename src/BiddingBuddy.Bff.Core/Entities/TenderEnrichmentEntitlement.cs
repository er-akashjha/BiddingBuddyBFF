namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A single org's entitlement to view a tender's AI enrichment. Enrichment data is
/// global (computed once, in Mongo); this row is the per-org access grant that the
/// read path checks before revealing AI fields. UNIQUE(org_id, gem_tender_id) makes
/// the unlock idempotent.
/// </summary>
public class TenderEnrichmentEntitlement
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    /// <summary>GeM bid number (the tender's platform id) this entitlement unlocks.</summary>
    public string GemTenderId { get; set; } = default!;

    /// <summary>pending (paid, enrichment in progress) | unlocked (AI visible) | failed.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>How the entitlement was granted: grant | purchase | credit | plan.</summary>
    public string Source { get; set; } = "grant";

    /// <summary>Payment gateway reference, once a real gateway is wired (null for grants).</summary>
    public string? PaymentRef { get; set; }

    /// <summary>User who initiated the unlock (audit).</summary>
    public Guid? UnlockedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the entitlement became <c>unlocked</c> (enrichment completed / already enriched).</summary>
    public DateTime? UnlockedAt { get; set; }
}
