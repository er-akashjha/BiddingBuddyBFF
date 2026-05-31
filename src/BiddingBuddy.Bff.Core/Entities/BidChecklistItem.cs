namespace BiddingBuddy.Bff.Core.Entities;

public class BidChecklistItem
{
    public Guid Id { get; set; }
    public Guid BidId { get; set; }
    public Guid OrgId { get; set; }
    public string Title { get; set; } = default!;
    public bool IsDone { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? AssignedTo { get; set; }
    public DateTime? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
}
