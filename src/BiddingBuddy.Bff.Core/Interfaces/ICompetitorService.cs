using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Competitors;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ICompetitorService
{
    Task<PagedResult<CompetitorDto>> ListAsync(Guid orgId, string? threatLevel, int page, int pageSize, CancellationToken ct = default);
    Task<CompetitorDetailDto> GetAsync(Guid competitorId, Guid orgId, CancellationToken ct = default);
    Task<CompetitorDto> UpdateAsync(Guid competitorId, Guid orgId, UpdateCompetitorDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<BidObservationDto>> GetObservationsAsync(Guid competitorId, Guid orgId, int limit, CancellationToken ct = default);
}
