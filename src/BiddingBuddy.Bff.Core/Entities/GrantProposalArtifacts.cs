namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>One section of an application's proposal narrative (need statement, project description, …).</summary>
public class GrantNarrativeSection
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public string SectionKey { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Content { get; set; }
    public int WordCount { get; set; }
    public int? TargetWords { get; set; }
    public string Status { get; set; } = "not_started";
    public int SortOrder { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
}

/// <summary>One budget line on an application (personnel, travel, indirect, …).</summary>
public class GrantBudgetLineItem
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public string Category { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
}

/// <summary>An internal reviewer's verdict on an application before submission.</summary>
public class GrantReview
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public Guid? ReviewerId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Comments { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
    public User? Reviewer { get; set; }
}

/// <summary>An application's submission record (portal, confirmation, status, awarded amount).</summary>
public class GrantSubmission
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public string Portal { get; set; } = "grants_gov";
    public string Status { get; set; } = "draft";
    public DateTime? SubmittedAt { get; set; }
    public string? ConfirmationNumber { get; set; }
    public Guid? SubmittedBy { get; set; }
    public decimal? AmountAwarded { get; set; }
    public string? Notes { get; set; }
    public string? FileManifest { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
}
