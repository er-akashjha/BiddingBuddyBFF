namespace BiddingBuddy.Bff.Core.DTOs.Bids;

/// <summary>
/// Filter / sort / page options for the bids list. <c>AssignedTo</c> is already resolved
/// (the controller turns the <c>"me"</c> sentinel into the current user id).
/// </summary>
public record BidListQuery(
    string? Stage = null,
    string? Priority = null,
    string? StatusCategory = null,
    string? Q = null,
    Guid? AssignedTo = null,
    DateOnly? DueBefore = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 20
);

public record BidListItemDto(
    Guid Id,
    string Title,
    string Stage,
    string StatusCategory,
    string Priority,
    DateOnly? DueDate,
    decimal? TenderValue,
    decimal? OurBidValue,
    decimal? WinProbability,
    int ProgressPct,
    Guid? AssignedTo,
    string? AssignedToName,
    Guid? TenderId,
    string? GemTenderId,
    DateTime UpdatedAt,
    TaskSummaryDto Tasks
);

/// <summary>
/// Per-bid checklist roll-up shown on each list row / board card. <c>MineOpen</c> is the
/// count of incomplete tasks assigned to the requesting user (drives the "assigned to me"
/// highlight). <c>Overdue</c> counts incomplete tasks past their due date.
/// </summary>
public record TaskSummaryDto(int Total, int Done, int Overdue, int MineOpen);

public record BidDetailDto(
    Guid Id,
    Guid OrgId,
    Guid? TenderId,
    string? GemTenderId,
    // The linked tender's MongoDB _id (the system of record for full tender content).
    // The UI re-fetches rich tender context by this id — TenderId is the local Postgres
    // PK, a different id-space the tender-detail endpoint can't resolve. NULL until the
    // pipeline backfills mongo_tender_id (migration 0010).
    string? MongoTenderId,
    string Title,
    string? Description,
    string Stage,
    string StatusCategory,
    string Priority,
    Guid? AssignedTo,
    string? AssignedToName,
    DateOnly? DueDate,
    decimal? TenderValue,
    decimal? OurBidValue,
    decimal? WinProbability,
    int ProgressPct,
    string? LossReason,
    decimal? WonValue,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<BidActivityDto> RecentActivities,
    int ChecklistTotal,
    int ChecklistDone
);

public record CreateBidDto(
    Guid? TenderId,
    string Title,
    string? Description,
    string Stage = "identified",
    string Priority = "medium",
    Guid? AssignedTo = null,
    DateOnly? DueDate = null,
    decimal? TenderValue = null,
    decimal? OurBidValue = null,
    decimal? WinProbability = null
);

public record UpdateBidDto(
    string? Title,
    string? Description,
    string? Stage,
    string? Priority,
    Guid? AssignedTo,
    DateOnly? DueDate,
    decimal? TenderValue,
    decimal? OurBidValue,
    decimal? WinProbability,
    int? ProgressPct,
    string? LossReason,
    decimal? WonValue
);

public record ChangeStageDto(string Stage, string? Note);

/// <summary>
/// Lightweight "is this tender already in the pipeline?" record — one entry per tender
/// that has a bid in the org (newest bid wins when several exist). Returned in a batch by
/// GET /api/bids/by-tender so the tender list + detail pages can show the existing owner /
/// stage and stop users adding the same tender twice.
/// </summary>
public record BidByTenderDto(
    Guid TenderId,
    Guid BidId,
    Guid? AssignedTo,
    string? AssignedToName,
    string Stage
);

public record BidActivityDto(
    Guid Id,
    Guid ActorId,
    string ActorName,
    string Action,
    string? FromValue,
    string? ToValue,
    string? Note,
    DateTime CreatedAt
);

public record AddNoteDto(string Note);

public record BidCommentDto(
    Guid Id,
    Guid BidId,
    Guid AuthorId,
    string AuthorName,
    string Body,
    string Kind,                       // comment | task_completion
    Guid? ChecklistItemId,             // set for task-completion notes
    string? ChecklistItemTitle,        // resolved title of that task, for display
    IReadOnlyList<BidAttachmentDto> Attachments,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record AddCommentDto(string Body);

// ── Attachments (BID-303) ────────────────────────────────────────────────────

public record BidAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedBy,
    string? UploadedByName,
    DateTime CreatedAt
);

/// <summary>Register an already-uploaded R2 object as a bid attachment.</summary>
public record RegisterBidAttachmentDto(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid? ChecklistItemId = null
);

public record ChecklistItemDto(
    Guid Id,
    string Title,
    bool IsDone,
    DateOnly? DueDate,
    Guid? AssignedTo,
    string? AssignedToName,
    DateTime? DoneAt,
    int SortOrder
);

public record CreateChecklistItemDto(
    string Title,
    DateOnly? DueDate = null,
    Guid? AssignedTo = null,
    int SortOrder = 0
);

public record UpdateChecklistItemDto(
    string? Title,
    bool? IsDone,
    DateOnly? DueDate,
    Guid? AssignedTo,
    int? SortOrder
);

/// <summary>
/// Close a task. A non-empty <see cref="Note"/> is mandatory (recorded as a
/// task-completion comment in the Notes feed). <see cref="AttachmentId"/> optionally links
/// a previously-registered attachment to that note.
/// </summary>
public record CompleteChecklistItemDto(string Note, Guid? AttachmentId = null);

public record CompleteChecklistResultDto(ChecklistItemDto Item, BidCommentDto Note);

// ── My tasks (BID-201) ───────────────────────────────────────────────────────

/// <summary>One checklist item assigned to the current user, with its bid context + bucket.</summary>
public record MyTaskDto(
    Guid ItemId,
    Guid BidId,
    string BidTitle,
    string Stage,
    string Title,
    DateOnly? DueDate,
    bool IsDone,
    string Bucket                      // overdue | today | week | later | done
);
