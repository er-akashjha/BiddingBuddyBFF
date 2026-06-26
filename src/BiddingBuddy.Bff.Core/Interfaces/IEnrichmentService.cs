using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Pay-gated, on-demand AI enrichment. Enrichment data is global (computed once, in
/// Mongo); access is sold per org via <c>tender_enrichment_entitlements</c>. This service
/// owns the grant + atomic claim + publish, and the read-path unlock checks.
/// </summary>
public interface IEnrichmentService
{
    /// <summary>True if the org has an <c>unlocked</c> entitlement for this tender (AI visible).</summary>
    Task<bool> IsUnlockedAsync(Guid orgId, string gemTenderId, CancellationToken ct = default);

    /// <summary>
    /// The subset of the given gem tender ids the org has unlocked — batched, for masking
    /// a list page in one query.
    /// </summary>
    Task<HashSet<string>> GetUnlockedSetAsync(
        Guid orgId, IReadOnlyCollection<string> gemTenderIds, CancellationToken ct = default);

    /// <summary>
    /// Pay-gated on-demand enrichment request. Idempotently grants the org an entitlement
    /// (anti-double-charge via UNIQUE(org_id, gem_tender_id)); if the tender isn't already
    /// enriched, atomically claims the Mongo state machine and enqueues a single AI run
    /// (anti-double-enrich). If already enriched globally, unlocks the org immediately at
    /// zero AI cost.
    /// </summary>
    Task<EnrichmentStatusDto> RequestEnrichmentAsync(
        Guid orgId, Guid userId, Guid tenderId, CancellationToken ct = default);

    /// <summary>Resolved per-org status for the UI badge / paywall.</summary>
    Task<EnrichmentStatusDto> GetStatusAsync(Guid orgId, Guid tenderId, CancellationToken ct = default);
}
