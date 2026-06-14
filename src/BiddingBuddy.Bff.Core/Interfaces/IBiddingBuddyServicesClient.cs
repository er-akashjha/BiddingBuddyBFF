using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Typed HTTP client for the internal BiddingBuddyServices API (MongoDB-backed).
/// Uses Basic auth (admin / admin123).
/// </summary>
public interface IBiddingBuddyServicesClient
{

    Task<TenderDetailDto> GetTenderAsync(string tenderId, CancellationToken ct = default);
    Task<List<TenderListItemDto>> SearchTendersAsync(TenderSearchQueryDto query, CancellationToken ct = default);
    Task<PagedTenderListDto> SearchTendersPagedAsync(TenderSearchQueryDto query, CancellationToken ct = default);
}
