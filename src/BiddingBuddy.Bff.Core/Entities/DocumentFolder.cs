namespace BiddingBuddy.Bff.Core.Entities;

public class DocumentFolder
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public DocumentFolder? Parent { get; set; }
    public ICollection<DocumentFolder> Children { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
}
