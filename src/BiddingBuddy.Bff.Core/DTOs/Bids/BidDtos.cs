namespace BiddingBuddy.Bff.Core.DTOs.Bids;

public record BidListItemDto(
    Guid Id,
    string Title,
    string Stage,
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
    DateTime UpdatedAt
);

public record BidDetailDto(
    Guid Id,
    Guid OrgId,
    Guid? TenderId,
    string? GemTenderId,
    string Title,
    string? Description,
    string Stage,
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
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record AddCommentDto(string Body);

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
