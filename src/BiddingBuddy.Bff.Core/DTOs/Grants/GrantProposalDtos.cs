namespace BiddingBuddy.Bff.Core.DTOs.Grants;

// ── Narrative ─────────────────────────────────────────────────────────────────

public record GrantNarrativeSectionDto(
    Guid Id,
    string SectionKey,
    string Title,
    string? Content,
    int WordCount,
    int? TargetWords,
    string Status,            // not_started | drafting | complete
    int SortOrder,
    DateTime UpdatedAt
);

/// <summary>Patch a narrative section. Content recomputes the word count; status is optional.</summary>
public record UpdateNarrativeSectionDto(
    string? Title = null,
    string? Content = null,
    string? Status = null,
    int? TargetWords = null
);

// ── Budget ────────────────────────────────────────────────────────────────────

public record GrantBudgetLineDto(
    Guid Id,
    string Category,          // personnel | fringe | travel | equipment | supplies | contractual | indirect | other
    string Description,
    decimal Amount,
    int SortOrder
);

public record CreateBudgetLineDto(string Category, string Description, decimal Amount = 0, int SortOrder = 0);

public record UpdateBudgetLineDto(
    string? Category = null,
    string? Description = null,
    decimal? Amount = null,
    int? SortOrder = null
);

// ── Reviews ───────────────────────────────────────────────────────────────────

public record GrantReviewDto(
    Guid Id,
    Guid? ReviewerId,
    string? ReviewerName,
    string Status,           // pending | in_progress | approved | changes_requested
    string? Comments,
    DateTime? ReviewedAt,
    DateTime CreatedAt
);

public record CreateReviewDto(Guid? ReviewerId = null, string? Comments = null);

public record UpdateReviewDto(string? Status = null, string? Comments = null);

// ── Submission ────────────────────────────────────────────────────────────────

public record GrantSubmissionDto(
    Guid Id,
    string Portal,           // grants_gov | foundation | submittable | fluxx | other
    string Status,           // draft | submitted | under_review | awarded | declined | more_info
    DateTime? SubmittedAt,
    string? ConfirmationNumber,
    Guid? SubmittedBy,
    string? SubmittedByName,
    decimal? AmountAwarded,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateSubmissionDto(
    string Portal = "grants_gov",
    string Status = "draft",
    string? ConfirmationNumber = null,
    decimal? AmountAwarded = null,
    string? Notes = null
);

public record UpdateSubmissionDto(
    string? Portal = null,
    string? Status = null,
    string? ConfirmationNumber = null,
    decimal? AmountAwarded = null,
    string? Notes = null
);
