namespace BiddingBuddy.Bff.Core.Entities;

public class ComplianceDocument
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid RequirementId { get; set; }
    public Guid? DocumentId { get; set; }
    public string Status { get; set; } = "pending";  // pending|valid|expiring_soon|expired
    public DateOnly? ExpiryDate { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public ComplianceRequirement Requirement { get; set; } = default!;
    public Document? Document { get; set; }
}
