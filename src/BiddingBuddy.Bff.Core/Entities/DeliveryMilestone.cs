namespace BiddingBuddy.Bff.Core.Entities;

public class DeliveryMilestone
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrgId { get; set; }
    public string Title { get; set; } = default!;
    public DateOnly? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order Order { get; set; } = default!;
}
