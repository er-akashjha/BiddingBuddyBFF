using BiddingBuddy.Bff.Core.DTOs.Grants;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IGrantApplicationService
{
    Task<IReadOnlyList<GrantApplicationListItemDto>> ListAsync(Guid orgId, GrantApplicationListQuery query, CancellationToken ct = default);
    Task<GrantApplicationDetailDto> GetAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantApplicationDetailDto> CreateAsync(Guid orgId, Guid userId, CreateGrantApplicationDto dto, CancellationToken ct = default);
    Task<GrantApplicationDetailDto> UpdateAsync(Guid id, Guid orgId, Guid userId, UpdateGrantApplicationDto dto, CancellationToken ct = default);
    Task<GrantApplicationDetailDto> ChangeStageAsync(Guid id, Guid orgId, Guid userId, ChangeGrantApplicationStageDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<GrantApplicationActivityDto>> GetActivitiesAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantApplicationActivityDto> AddNoteAsync(Guid id, Guid orgId, Guid userId, AddGrantApplicationNoteDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<GrantChecklistItemDto>> GetChecklistAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantChecklistItemDto> CreateChecklistItemAsync(Guid id, Guid orgId, Guid userId, CreateGrantChecklistItemDto dto, CancellationToken ct = default);
    Task<GrantChecklistItemDto> UpdateChecklistItemAsync(Guid itemId, Guid id, Guid orgId, Guid userId, UpdateGrantChecklistItemDto dto, CancellationToken ct = default);
    Task DeleteChecklistItemAsync(Guid itemId, Guid id, Guid orgId, CancellationToken ct = default);

    // ── Proposal authoring ──────────────────────────────────────────────────────
    Task<IReadOnlyList<GrantNarrativeSectionDto>> GetNarrativeAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantNarrativeSectionDto> UpdateNarrativeSectionAsync(Guid sectionId, Guid id, Guid orgId, Guid userId, UpdateNarrativeSectionDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<GrantBudgetLineDto>> GetBudgetAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantBudgetLineDto> AddBudgetLineAsync(Guid id, Guid orgId, CreateBudgetLineDto dto, CancellationToken ct = default);
    Task<GrantBudgetLineDto> UpdateBudgetLineAsync(Guid lineId, Guid id, Guid orgId, UpdateBudgetLineDto dto, CancellationToken ct = default);
    Task DeleteBudgetLineAsync(Guid lineId, Guid id, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<GrantReviewDto>> GetReviewsAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantReviewDto> AddReviewAsync(Guid id, Guid orgId, Guid userId, CreateReviewDto dto, CancellationToken ct = default);
    Task<GrantReviewDto> UpdateReviewAsync(Guid reviewId, Guid id, Guid orgId, Guid userId, UpdateReviewDto dto, CancellationToken ct = default);
    Task DeleteReviewAsync(Guid reviewId, Guid id, Guid orgId, CancellationToken ct = default);

    Task<IReadOnlyList<GrantSubmissionDto>> GetSubmissionsAsync(Guid id, Guid orgId, CancellationToken ct = default);
    Task<GrantSubmissionDto> AddSubmissionAsync(Guid id, Guid orgId, Guid userId, CreateSubmissionDto dto, CancellationToken ct = default);
    Task<GrantSubmissionDto> UpdateSubmissionAsync(Guid submissionId, Guid id, Guid orgId, Guid userId, UpdateSubmissionDto dto, CancellationToken ct = default);
    Task DeleteSubmissionAsync(Guid submissionId, Guid id, Guid orgId, CancellationToken ct = default);
}
