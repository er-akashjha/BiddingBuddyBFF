namespace BiddingBuddy.Bff.Core.Entities;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? OrderId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? BuyerOrg { get; set; }
    public decimal Amount { get; set; }
    public decimal? GstAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? PaidDate { get; set; }
    public decimal? PaidAmount { get; set; }
    public string Status { get; set; } = "pending";  // pending|paid|overdue|partial
    public string? PaymentRef { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Order? Order { get; set; }
}
