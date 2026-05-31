namespace BiddingBuddy.Bff.Core.Entities;

public class TenderDocument
{
    public Guid Id { get; set; }
    public Guid TenderId { get; set; }
    public string FileName { get; set; } = default!;
    public string S3Key { get; set; } = default!;
    public string? DocumentType { get; set; }
    public int? FileSizeKb { get; set; }
    public string? ExtractedText { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tender Tender { get; set; } = default!;
}
