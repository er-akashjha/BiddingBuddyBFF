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
    /// Tender counts grouped by state, for the public coverage map.
    /// </summary>
    Task<List<StateTenderCountDto>> GetStateTenderCountsAsync(CancellationToken ct = default);
}
