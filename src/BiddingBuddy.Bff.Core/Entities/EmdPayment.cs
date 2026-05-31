namespace BiddingBuddy.Bff.Core.Entities;

public class EmdPayment
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? BidId { get; set; }
    public Guid? TenderId { get; set; }
    public string? GemTenderId { get; set; }
    public string? TenderTitle { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string? PaymentMode { get; set; }
    public string? TransactionRef { get; set; }
    public string? BankName { get; set; }
    public string Status { get; set; } = "held";  // held|refunded|forfeited
    public DateOnly? RefundDate { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundRef { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Bid? Bid { get; set; }
    public Tender? Tender { get; set; }
}
