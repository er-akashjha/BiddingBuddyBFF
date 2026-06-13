namespace BiddingBuddy.Bff.Core.Entities;

public class Bid
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? TenderId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Stage { get; set; } = "identified";
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Tender? Tender { get; set; }
    public User? AssignedUser { get; set; }
    public ICollection<BidActivity> Activities { get; set; } = [];
    public ICollection<BidChecklistItem> ChecklistItems { get; set; } = [];
    public ICollection<BidComment> Comments { get; set; } = [];
}
