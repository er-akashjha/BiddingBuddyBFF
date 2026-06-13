namespace BiddingBuddy.Bff.Core.Entities;

public class BidComment
{
    public Guid Id { get; set; }
    public Guid BidId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
    public User Author { get; set; } = default!;
}
