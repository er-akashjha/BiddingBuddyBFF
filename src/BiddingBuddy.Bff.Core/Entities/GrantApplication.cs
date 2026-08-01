namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// An org's grant application — the grants analog of <see cref="Bid"/>, moving through a GRANT stage
/// vocabulary (Qualifying → … → Awarded). Org-scoped. <see cref="MongoGrantId"/> links back to the
/// source opportunity (null for a manually-started one); the title/agency snapshot is copied at
/// create time so the application survives the opportunity being archived or re-enriched.
/// </summary>
public class GrantApplication
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string? MongoGrantId { get; set; }

    public string Title { get; set; } = default!;
    public string? AgencyName { get; set; }
    public string? OpportunityNumber { get; set; }

    public string Stage { get; set; } = "Qualifying";

    /// <summary>Derived (open|closed) — a generated, read-only column in Postgres. EF reads it back.</summary>
    public string StatusCategory { get; private set; } = "open";

    public decimal? AmountRequested { get; set; }
    public DateOnly? Deadline { get; set; }
    public Guid? AssignedTo { get; set; }

    /// <summary>Proposal-readiness 0..100, recomputed by the service from checklist / narrative / budget / reviews.</summary>
    public int Readiness { get; set; }
    public decimal? CostSharePct { get; set; }
    public bool TribalSetAside { get; set; }
    public string[]? Tags { get; set; }
    public string? SourceUrl { get; set; }
    public string? Notes { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User? AssignedUser { get; set; }
    public ICollection<GrantApplicationActivity> Activities { get; set; } = [];
    public ICollection<GrantApplicationChecklistItem> ChecklistItems { get; set; } = [];
    public ICollection<GrantNarrativeSection> NarrativeSections { get; set; } = [];
    public ICollection<GrantBudgetLineItem> BudgetLineItems { get; set; } = [];
    public ICollection<GrantReview> Reviews { get; set; } = [];
    public ICollection<GrantSubmission> Submissions { get; set; } = [];
}
