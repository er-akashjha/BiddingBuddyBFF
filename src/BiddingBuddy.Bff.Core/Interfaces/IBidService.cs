using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IBidService
{
    Task<PagedResult<BidListItemDto>> ListAsync(Guid orgId, string? stage, string? priority, int page, int pageSize, CancellationToken ct = default);
    Task<BidDetailDto> GetAsync(Guid bidId, Guid orgId, CancellationToken ct = default);
    Task<BidDetailDto> CreateAsync(Guid orgId, Guid userId, CreateBidDto dto, CancellationToken ct = default);
    Task<BidDetailDto> UpdateAsync(Guid bidId, Guid orgId, UpdateBidDto dto, CancellationToken ct = default);
    Task<BidDetailDto> ChangeStageAsync(Guid bidId, Guid orgId, Guid actorId, ChangeStageDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid bidId, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<BidActivityDto>> GetActivitiesAsync(Guid bidId, Guid orgId, CancellationToken ct = default);
    Task<BidActivityDto> AddNoteAsync(Guid bidId, Guid orgId, Guid actorId, AddNoteDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<BidCommentDto>> GetCommentsAsync(Guid bidId, Guid orgId, CancellationToken ct = default);
    Task<BidCommentDto> AddCommentAsync(Guid bidId, Guid orgId, Guid authorId, AddCommentDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<ChecklistItemDto>> GetChecklistAsync(Guid bidId, Guid orgId, CancellationToken ct = default);
    Task<ChecklistItemDto> CreateChecklistItemAsync(Guid bidId, Guid orgId, CreateChecklistItemDto dto, CancellationToken ct = default);
    Task<ChecklistItemDto> UpdateChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, Guid actorId, UpdateChecklistItemDto dto, CancellationToken ct = default);
    Task DeleteChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, CancellationToken ct = default);
}
