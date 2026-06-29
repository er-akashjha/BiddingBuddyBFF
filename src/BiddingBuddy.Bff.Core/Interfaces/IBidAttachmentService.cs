using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Documents;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Bid file attachments, stored in Cloudflare R2 (bucket bidding-buddy) via presigned
/// upload, mirroring the document-vault flow. Used for the optional attachment on a
/// task-completion note.
/// </summary>
public interface IBidAttachmentService
{
    /// <summary>Presigned PUT URL for direct-to-R2 upload. Validates mime + size.</summary>
    Task<UploadUrlResponseDto> CreateUploadUrlAsync(Guid orgId, Guid bidId, RequestUploadUrlDto dto, CancellationToken ct = default);

    /// <summary>Register an uploaded object as an attachment row.</summary>
    Task<BidAttachmentDto> RegisterAsync(Guid orgId, Guid bidId, Guid userId, RegisterBidAttachmentDto dto, CancellationToken ct = default);

    /// <summary>Presigned GET URL to download an attachment (org-scoped).</summary>
    Task<DocumentUrlResponseDto> CreateDownloadUrlAsync(Guid orgId, Guid bidId, Guid attachmentId, CancellationToken ct = default);
}
