namespace BiddingBuddy.Bff.Core.Entities;

public class BidComment
{
    public Guid Id { get; set; }
    public Guid BidId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = default!;

    /// <summary>Set when this comment is the mandatory note recorded on completing a task.</summary>
    public Guid? ChecklistItemId { get; set; }

    /// <summary><c>comment</c> | <c>task_completion</c>.</summary>
    public string Kind { get; set; } = "comment";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
    public User Author { get; set; } = default!;
}
