using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Bid attachments stored in Cloudflare R2 (bucket bidding-buddy), mirroring the document
/// vault's presign → PUT → register flow. Object key: orgs/{orgId}/bids/{bidId}/{guid}/{file}.
/// </summary>
public class BidAttachmentService(
    BffDbContext db,
    IR2Storage r2,
    IOptions<R2Options> r2Options) : IBidAttachmentService
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain", "text/csv",
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/zip", "application/x-zip-compressed",
    };

    public async Task<UploadUrlResponseDto> CreateUploadUrlAsync(
        Guid orgId, Guid bidId, RequestUploadUrlDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        if (string.IsNullOrWhiteSpace(dto.FileName))
            throw new ArgumentException("fileName is required.");
        var sanitized = FileNameSanitizer.Sanitize(dto.FileName);
        if (string.IsNullOrWhiteSpace(sanitized))
            throw new ArgumentException("fileName is invalid after sanitization.");

        if (string.IsNullOrWhiteSpace(dto.MimeType) || !AllowedMimeTypes.Contains(dto.MimeType))
            throw new ArgumentException($"mimeType '{dto.MimeType}' is not allowed.");

        var max = r2Options.Value.MaxUploadSizeKb;
        if (dto.FileSizeKb < 1 || dto.FileSizeKb > max)
            throw new ArgumentException($"fileSizeKb must be between 1 and {max}.");

        var objectKey = $"orgs/{orgId}/bids/{bidId}/{Guid.NewGuid()}/{sanitized}";
        var presigned = await r2.CreatePresignedPutAsync(objectKey, dto.MimeType, dto.FileSizeKb * 1024L, ct);

        return new UploadUrlResponseDto(presigned.UploadUrl, presigned.ObjectKey, presigned.Headers, presigned.ExpiresAt);
    }

    public async Task<BidAttachmentDto> RegisterAsync(
        Guid orgId, Guid bidId, Guid userId, RegisterBidAttachmentDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        if (string.IsNullOrWhiteSpace(dto.StorageKey))
            throw new ArgumentException("storageKey is required.");

        // Guard against a client registering an object outside this bid's key prefix.
        var prefix = $"orgs/{orgId}/bids/{bidId}/";
        if (!dto.StorageKey.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("storageKey does not belong to this bid.");

        var attachment = new BidAttachment
        {
            Id              = Guid.NewGuid(),
            OrgId           = orgId,
            BidId           = bidId,
            ChecklistItemId = dto.ChecklistItemId,
            FileName        = FileNameSanitizer.Sanitize(dto.FileName),
            ContentType     = dto.ContentType,
            SizeBytes       = dto.SizeBytes,
            StorageKey      = dto.StorageKey,
            UploadedBy      = userId,
        };
        db.BidAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        var uploaderName = await db.Users.Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync(ct);
        return new BidAttachmentDto(
            attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
            attachment.UploadedBy, uploaderName, attachment.CreatedAt);
    }

    public async Task<DocumentUrlResponseDto> CreateDownloadUrlAsync(
        Guid orgId, Guid bidId, Guid attachmentId, CancellationToken ct = default)
    {
        var att = await db.BidAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.BidId == bidId && a.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Attachment not found.");

        var presigned = await r2.CreatePresignedGetAsync(att.StorageKey, att.FileName, inline: false, ct);
        return new DocumentUrlResponseDto(presigned.Url, presigned.ExpiresAt);
    }

    private async Task EnsureBidBelongsAsync(Guid bidId, Guid orgId, CancellationToken ct)
    {
        var exists = await db.Bids.AnyAsync(b => b.Id == bidId && b.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Bid not found.");
    }
}
