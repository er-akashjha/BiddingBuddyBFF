namespace BiddingBuddy.Bff.Core.DTOs.Grants;

// ── List / board ──────────────────────────────────────────────────────────────

public record GrantApplicationListQuery(
    string? Stage = null,
    string? StatusCategory = null,
    Guid? AssignedTo = null,
    string? Q = null,
    string? Sort = null
);

public record GrantApplicationListItemDto(
    Guid Id,
    string? MongoGrantId,
    string Title,
    string? AgencyName,
    string? OpportunityNumber,
    string Stage,
    string StatusCategory,
    decimal? AmountRequested,
    DateOnly? Deadline,
    Guid? AssignedTo,
    string? AssignedToName,
    int Readiness,
    decimal? CostSharePct,
    bool TribalSetAside,
    IReadOnlyList<string> Tags,
    DateTime UpdatedAt
);

// ── Detail ──────────────────────────────────────────────────────────────────────

public record GrantApplicationActivityDto(
    Guid Id,
    Guid? ActorId,
    string? ActorName,
    string Action,
    string? FromValue,
    string? ToValue,
    string? Note,
    DateTime CreatedAt
);

public record GrantChecklistItemDto(
    Guid Id,
    string Title,
    bool IsDone,
    DateOnly? DueDate,
    Guid? AssignedTo,
    string? AssignedToName,
    DateTime? DoneAt,
    int SortOrder
);

public record GrantApplicationDetailDto(
    Guid Id,
    Guid OrgId,
    string? MongoGrantId,
    string Title,
    string? AgencyName,
    string? OpportunityNumber,
    string Stage,
    string StatusCategory,
    decimal? AmountRequested,
    DateOnly? Deadline,
    Guid? AssignedTo,
    string? AssignedToName,
    int Readiness,
    decimal? CostSharePct,
    bool TribalSetAside,
    IReadOnlyList<string> Tags,
    string? SourceUrl,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<GrantApplicationActivityDto> RecentActivities,
    IReadOnlyList<GrantChecklistItemDto> Checklist
);

// ── Writes ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Start an application. With <see cref="MongoGrantId"/> set it's started from an opportunity (and is
/// idempotent — an existing OPEN application for that grant is returned instead of a duplicate);
/// without it, it's a manual application. Snapshot fields are the grant's display values the client
/// already holds.
/// </summary>
public record CreateGrantApplicationDto(
    string Title,
    string? MongoGrantId = null,
    string? AgencyName = null,
    string? OpportunityNumber = null,
    string Stage = "Qualifying",
    decimal? AmountRequested = null,
    DateOnly? Deadline = null,
    Guid? AssignedTo = null,
    decimal? CostSharePct = null,
    bool TribalSetAside = false,
    IReadOnlyList<string>? Tags = null,
    string? SourceUrl = null,
    string? Notes = null
);

public record UpdateGrantApplicationDto(
    string? Title = null,
    string? AgencyName = null,
    string? Stage = null,
    decimal? AmountRequested = null,
    DateOnly? Deadline = null,
    Guid? AssignedTo = null,
    decimal? CostSharePct = null,
    bool? TribalSetAside = null,
    IReadOnlyList<string>? Tags = null,
    string? Notes = null
);

public record ChangeGrantApplicationStageDto(string Stage, string? Note = null);

public record AddGrantApplicationNoteDto(string Note);

public record CreateGrantChecklistItemDto(
    string Title,
    DateOnly? DueDate = null,
    Guid? AssignedTo = null,
    int SortOrder = 0
);

public record UpdateGrantChecklistItemDto(
    string? Title = null,
    bool? IsDone = null,
    DateOnly? DueDate = null,
    Guid? AssignedTo = null,
    int? SortOrder = null
);
