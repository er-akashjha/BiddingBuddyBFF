namespace BiddingBuddy.Bff.Core.Entities;

public class Bid
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? TenderId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Stage { get; set; } = "identified";

    /// <summary>
    /// Derived (open|closed) — a generated, read-only column in Postgres (migration 0013).
    /// Never assigned in app code; EF reads it back after save.
    /// </summary>
    public string StatusCategory { get; private set; } = "open";

    public string Priority { get; set; } = "medium";
    public Guid? AssignedTo { get; set; }
    public Guid CreatedBy { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? TenderValue { get; set; }
    public decimal? OurBidValue { get; set; }
    public decimal? WinProbability { get; set; }
    public int ProgressPct { get; set; }
    public string? LossReason { get; set; }
    public decimal? WonValue { get; set; }

    /// <summary>
    /// unknown|required|exempt|not_required (migration 0029). Seeded from the tender's EMD
    /// amount when the bid is created from a tender, then overridable — exemption is common
    /// for MSME/NSIC/Startup sellers. Gates the whole EMD surface: only <c>required</c> makes
    /// a missing instrument or an un-dispatched courier worth alerting about.
    /// </summary>
    public string EmdRequirement { get; set; } = "unknown";

    /// <summary>MSME | NSIC | Startup | Other — only meaningful when EmdRequirement is 'exempt'.</summary>
    public string? EmdExemptionBasis { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Tender? Tender { get; set; }
    public User? AssignedUser { get; set; }
    public ICollection<BidActivity> Activities { get; set; } = [];
    public ICollection<BidChecklistItem> ChecklistItems { get; set; } = [];
    public ICollection<BidComment> Comments { get; set; } = [];
    public ICollection<BidDispatch> Dispatches { get; set; } = [];
}
