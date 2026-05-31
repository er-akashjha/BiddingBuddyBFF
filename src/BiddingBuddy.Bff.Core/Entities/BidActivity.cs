namespace BiddingBuddy.Bff.Core.Entities;

public class BidActivity
{
    public Guid Id { get; set; }
    public Guid BidId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = default!;   // stage_change|note_added|assigned|document_added
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
    public User Actor { get; set; } = default!;
}
