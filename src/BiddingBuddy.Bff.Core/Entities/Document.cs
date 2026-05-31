namespace BiddingBuddy.Bff.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string S3Key { get; set; } = default!;
    public string? S3VersionId { get; set; }
    public int? FileSizeKb { get; set; }
    public string? MimeType { get; set; }
    public string? DocumentType { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string[]? Tags { get; set; }
    public int? HealthScore { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public DocumentFolder? Folder { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = [];
}
