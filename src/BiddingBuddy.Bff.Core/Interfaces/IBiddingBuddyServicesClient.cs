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

    /// <summary>
    /// Cursor (keyset) enumeration of every tender (id, title, updatedAt), ordered by id.
    /// Pass the last id as <paramref name="afterId"/> to page forward; an empty list = end.
    /// Used to build the full sitemap without deep pagination.
    /// </summary>
    Task<List<TenderEnumerationDto>> EnumerateTendersAsync(
        string? afterId, int limit, CancellationToken ct = default);

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

    /// <summary>The award result for a tender (winner + ladder), or null if not awarded yet.</summary>
    Task<TenderResultDto?> GetTenderResultAsync(string platform, string platformTenderId, CancellationToken ct = default);

    /// <summary>Aggregate market pricing/competition stats over awards for a slice.</summary>
    Task<MarketPricingStatsDto> GetMarketPricingAsync(MarketFilterDto filter, CancellationToken ct = default);

    /// <summary>Winning-price stats bucketed by category|state|month|seller|buyer.</summary>
    Task<List<MarketGroupBucketDto>> GetMarketGroupedAsync(
        MarketFilterDto filter, string groupBy, int limit, CancellationToken ct = default);

    /// <summary>Top sellers by wins for the slice.</summary>
    Task<List<SellerStatsDto>> GetTopSellersAsync(MarketFilterDto filter, int limit, CancellationToken ct = default);

    /// <summary>Award history for one seller (by display name — normalized in Services).</summary>
    Task<SellerStatsDto?> GetSellerStatsAsync(string seller, CancellationToken ct = default);

    /// <summary>Head-to-head records between one seller and every rival it has faced.</summary>
    Task<List<HeadToHeadRecordDto>> GetHeadToHeadAsync(
        string seller, MarketFilterDto filter, int limit, CancellationToken ct = default);

    /// <summary>Award behaviour for one buyer.</summary>
    Task<BuyerProfileDto?> GetBuyerProfileAsync(string buyer, CancellationToken ct = default);

    /// <summary>Comparable past awards for a live tender.</summary>
    Task<List<TenderResultDto>> GetComparableAwardsAsync(
        string? category, string? state, decimal? estimatedValue, int limit, CancellationToken ct = default);
}
