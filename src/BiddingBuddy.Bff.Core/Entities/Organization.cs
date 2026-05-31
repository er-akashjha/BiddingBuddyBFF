namespace BiddingBuddy.Bff.Core.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public Guid OwnedBy { get; set; }
    public string Name { get; set; } = default!;
    public string? Slug { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Website { get; set; }
    public string? GemSellerId { get; set; }
    public string? PrimaryCategory { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User Owner { get; set; } = default!;
    public ICollection<OrgMember> Members { get; set; } = [];
    public ICollection<Bid> Bids { get; set; } = [];
    public ICollection<EmdPayment> EmdPayments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Competitor> Competitors { get; set; } = [];
}
