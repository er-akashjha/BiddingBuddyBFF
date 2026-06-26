using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Typed HTTP client for the internal BiddingBuddyServices API (MongoDB-backed).
/// Uses Basic auth (admin / admin123).
/// </summary>
public interface IBiddingBuddyServicesClient
{

    Task<TenderDetailDto> GetTenderAsync(string tenderId, CancellationToken ct = default);

    /// <summary>
    /// Fetch the raw (untranslated) tender item — includes document s3Bucket/s3Key,
    /// which the translated <see cref="TenderDetailDto"/> intentionally hides from
    /// clients. Used by the presign endpoint to locate the file. Null if not found.
    /// </summary>
    Task<TenderSearchItemDto?> GetRawTenderAsync(string tenderId, CancellationToken ct = default);
    Task<List<TenderListItemDto>> SearchTendersAsync(TenderSearchQueryDto query, CancellationToken ct = default);
    Task<PagedTenderListDto> SearchTendersPagedAsync(TenderSearchQueryDto query, CancellationToken ct = default);

    /// <summary>Default filter option values (top-N categories + states) for the initial dropdown render.</summary>
    Task<TenderFacetsDto> GetTenderFacetsAsync(int limit = 15, CancellationToken ct = default);

    /// <summary>
    /// Type-ahead options for a single facet field ("category" or "state"). Empty
    /// search → default top-N; non-empty search → all matches (pass limit=0 for no cap).
    /// </summary>
    Task<List<string>> GetTenderFacetOptionsAsync(
        string field, string? search, int limit, CancellationToken ct = default);

    /// <summary>
    /// Transition the Mongo tender's enrichment status (the authoritative state machine).
    /// <paramref name="status"/> / <paramref name="allowedCurrent"/> are enum member names
    /// (None/Extracted/Queued/Processing/Enriched/Failed). With <paramref name="allowedCurrent"/>
    /// the update is an atomic claim — returns true only if a tender matched (the claim was
    /// won), so the caller publishes the enrich message exactly once.
    /// </summary>
    Task<bool> SetEnrichmentStatusAsync(
        string platform, string platformTenderId, string status,
        IEnumerable<string>? allowedCurrent = null,
        string? paidByOrg = null,
        CancellationToken ct = default);
}
