namespace BiddingBuddy.Bff.Core.Entities;

public class ComplianceRequirement
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsMandatory { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public ICollection<ComplianceDocument> Documents { get; set; } = [];
}
