namespace BiddingBuddy.Bff.Core.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? BidId { get; set; }
    public Guid? TenderId { get; set; }
    public string? GemOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? BuyerOrg { get; set; }
    public DateOnly? OrderDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public decimal? TotalValue { get; set; }
    public string Status { get; set; } = "received";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Bid? Bid { get; set; }
    public Tender? Tender { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<DeliveryMilestone> Milestones { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}
