namespace BiddingBuddy.Bff.Core.Entities;

public class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNum { get; set; }
    public string S3Key { get; set; } = default!;
    public string? S3VersionId { get; set; }
    public int? FileSizeKb { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Document Document { get; set; } = default!;
}
