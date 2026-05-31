namespace BiddingBuddy.Bff.Core.Entities;

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrgId { get; set; }
    public string Description { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? HsnCode { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order Order { get; set; } = default!;
}
