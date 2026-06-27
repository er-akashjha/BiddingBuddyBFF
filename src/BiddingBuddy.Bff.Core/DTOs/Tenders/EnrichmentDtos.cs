namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>
/// Resolved per-org AI-enrichment status for a tender (the enrich + status endpoints).
/// <see cref="Status"/> is what the UI renders:
/// <list type="bullet">
/// <item><c>locked</c> — this org has no entitlement → show the "Unlock AI" CTA.</item>
/// <item><c>queued</c> — paid, enrichment enqueued.</item>
/// <item><c>processing</c> — paid, AI running.</item>
/// <item><c>enriched</c> — unlocked + AI available.</item>
/// <item><c>failed</c> — enrichment failed (re-requestable).</item>
/// </list>
/// </summary>
public record EnrichmentStatusDto(
    string Status,
    bool Entitled,
    /// <summary>Global tender enrichment lifecycle (none|extracted|queued|processing|enriched|failed).</summary>
    string GlobalStatus,
    DateTime? UnlockedAt
);
