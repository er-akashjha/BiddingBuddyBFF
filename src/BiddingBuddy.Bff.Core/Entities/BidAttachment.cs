namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A file attached to a bid — typically the optional attachment on a task-completion note.
/// Bytes live in Cloudflare R2 (bucket bidding-buddy); only the object key is stored here.
/// </summary>
public class BidAttachment
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid BidId { get; set; }
    public Guid? ChecklistItemId { get; set; }
    public Guid? CommentId { get; set; }
    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = default!;
    public Guid UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
    public User Uploader { get; set; } = default!;
}
