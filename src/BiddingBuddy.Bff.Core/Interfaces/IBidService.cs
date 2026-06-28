using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IBidService
{
    Task<PagedResult<BidListItemDto>> ListAsync(Guid orgId, Guid currentUserId, BidListQuery query, CancellationToken ct = default);

    /// <summary>Checklist items assigned to <paramref name="userId"/> across all bids in the org,
    /// each bucketed (overdue/today/week/later/done). <paramref name="status"/> = open|done|null(all).</summary>
    Task<IReadOnlyList<MyTaskDto>> GetMyTasksAsync(Guid orgId, Guid userId, string? status, CancellationToken ct = default);

    /// <summary>
    /// For the given tender ids, return which already have a bid in this org — one entry per
    /// tender (newest bid wins). Tenders with no bid are simply omitted. Used to flag
    /// already-in-pipeline tenders on the list + detail pages without N per-row lookups.
    /// </summary>
    Task<IReadOnlyList<BidByTenderDto>> GetByTenderIdsAsync(Guid orgId, IReadOnlyCollection<Guid> tenderIds, CancellationToken ct = default);

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
    Task<ChecklistItemDto> CreateChecklistItemAsync(Guid bidId, Guid orgId, Guid actorId, CreateChecklistItemDto dto, CancellationToken ct = default);
    Task<ChecklistItemDto> UpdateChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, Guid actorId, UpdateChecklistItemDto dto, CancellationToken ct = default);
    Task DeleteChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, CancellationToken ct = default);

    /// <summary>Close a task: requires a non-empty note (saved as a task-completion comment),
    /// optionally linking a previously-registered attachment to that note.</summary>
    Task<CompleteChecklistResultDto> CompleteChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, Guid actorId, CompleteChecklistItemDto dto, CancellationToken ct = default);
}
