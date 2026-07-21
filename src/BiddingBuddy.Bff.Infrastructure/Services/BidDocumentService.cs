using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// A bid's document folder. Reads are the union of two stores that predate each other:
/// bid_documents (links to the org vault) and bid_attachments (files owned by a
/// task-completion note). Writes only ever touch the link table — attachments are still
/// created by the task-close flow in <see cref="BidService"/>.
/// </summary>
public class BidDocumentService(BffDbContext db, ILogger<BidDocumentService> logger) : IBidDocumentService
{
    public async Task<IReadOnlyList<BidDocumentDto>> ListAsync(
        Guid orgId, Guid bidId, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var linked = await db.BidDocuments
            .Where(l => l.BidId == bidId && l.OrgId == orgId)
            .Select(l => new
            {
                l.Id,
                l.DocumentId,
                l.Document.Name,
                l.Document.FileName,
                l.Document.MimeType,
                l.Document.FileSizeKb,
                l.Document.DocumentType,
                FolderName = l.Document.Folder != null ? l.Document.Folder.Name : null,
                l.Document.ExpiryDate,
                l.LinkedBy,
                LinkedByName = l.Linker.Name,
                l.CreatedAt,
            })
            .ToListAsync(ct);

        // Every attachment on the bid, not just the comment-linked ones the Notes feed shows.
        // An attachment registered without the task-close call that would set comment_id is
        // otherwise unreachable — surfacing it here is the only way it's ever seen again.
        var attachments = await db.BidAttachments
            .Where(a => a.BidId == bidId && a.OrgId == orgId)
            .Select(a => new
            {
                a.Id,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.UploadedBy,
                UploadedByName = a.Uploader.Name,
                a.CreatedAt,
                ChecklistItemTitle = db.BidChecklistItems
                    .Where(c => c.Id == a.ChecklistItemId)
                    .Select(c => c.Title)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var rows = new List<BidDocumentDto>(linked.Count + attachments.Count);

        rows.AddRange(linked.Select(l => new BidDocumentDto(
            Id:                 l.Id,
            Source:             "vault",
            DocumentId:         l.DocumentId,
            Name:               l.Name,
            FileName:           l.FileName,
            ContentType:        l.MimeType,
            SizeBytes:          l.FileSizeKb.HasValue ? l.FileSizeKb.Value * 1024L : null,
            DocumentType:       l.DocumentType,
            FolderName:         l.FolderName,
            ExpiryDate:         l.ExpiryDate,
            AddedBy:            l.LinkedBy,
            AddedByName:        l.LinkedByName,
            CreatedAt:          l.CreatedAt,
            ChecklistItemTitle: null)));

        rows.AddRange(attachments.Select(a => new BidDocumentDto(
            Id:                 a.Id,
            Source:             "attachment",
            DocumentId:         null,
            Name:               a.FileName,
            FileName:           a.FileName,
            ContentType:        a.ContentType,
            SizeBytes:          a.SizeBytes,
            DocumentType:       null,
            FolderName:         null,
            ExpiryDate:         null,
            AddedBy:            a.UploadedBy,
            AddedByName:        a.UploadedByName,
            CreatedAt:          a.CreatedAt,
            ChecklistItemTitle: a.ChecklistItemTitle)));

        return rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<BidDocumentDto> LinkAsync(
        Guid orgId, Guid bidId, Guid userId, LinkBidDocumentDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        // Scoping the lookup to the org is what prevents linking another org's document —
        // an id from outside the org is indistinguishable from one that doesn't exist.
        var doc = await db.Documents
            .Include(d => d.Folder)
            .FirstOrDefaultAsync(d => d.Id == dto.DocumentId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Document not found.");

        var link = await db.BidDocuments
            .FirstOrDefaultAsync(l => l.BidId == bidId && l.DocumentId == doc.Id, ct);

        if (link is null)
        {
            link = new BidDocument
            {
                Id         = Guid.NewGuid(),
                OrgId      = orgId,
                BidId      = bidId,
                DocumentId = doc.Id,
                LinkedBy   = userId,
                CreatedAt  = DateTime.UtcNow,
            };
            db.BidDocuments.Add(link);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                var pg = ex.InnerException as PostgresException;

                // DbUpdateException's own message names only the DbContext ("An exception
                // occurred in the database while saving changes for context type
                // 'BffDbContext'") — every fact that identifies the failure lives on the inner
                // PostgresException, which is exactly the part a log export truncates. That is
                // what left the 2026-07-19 production 500 on this endpoint undiagnosable, so
                // the SQLSTATE and constraint go in the message template as their own
                // structured properties, where truncation can't reach them and Loki can
                // filter on them.
                logger.LogError(ex,
                    "bid_documents insert failed → SQLSTATE {SqlState}, constraint {ConstraintName}, " +
                    "table {PgTable}: {PgMessage} (detail: {PgDetail}) " +
                    "[org {OrgId}, bid {BidId}, document {DocumentId}, user {UserId}]",
                    pg?.SqlState       ?? "<not-a-PostgresException>",
                    pg?.ConstraintName ?? "<none>",
                    pg?.TableName      ?? "<none>",
                    pg?.MessageText    ?? ex.InnerException?.Message ?? ex.Message,
                    pg?.Detail         ?? "<none>",
                    orgId, bidId, doc.Id, userId);

                // 23505 only. Anything else — a missing relation (42P01), a foreign key the
                // pre-checks didn't cover (23503) — is a real fault and must keep bubbling to
                // the 500 it deserves, now with the SQLSTATE above to name it.
                if (pg?.SqlState != PostgresErrorCodes.UniqueViolation) throw;

                // Lost the check-then-act race: the existence check above and this insert are
                // not atomic, so a concurrent link of the same (bid, document) can commit
                // in between. Linking a document twice is meant to be a no-op that returns the
                // existing row — that is the endpoint's documented contract — so adopt the
                // winner's row rather than failing a request that asked for a state the
                // database is already in.
                //
                // The failed insert stays tracked in Added state after SaveChanges throws;
                // leaving it attached would make it a candidate for re-insert on any later
                // SaveChanges on this scoped context.
                db.Entry(link).State = EntityState.Detached;

                link = await db.BidDocuments
                    .FirstOrDefaultAsync(l => l.BidId == bidId && l.DocumentId == doc.Id, ct);

                // A unique violation whose row then isn't there means the winner rolled back
                // between our insert and this re-read. Nothing to adopt — let the original
                // exception stand rather than inventing a link that doesn't exist.
                if (link is null) throw;

                logger.LogInformation(
                    "Concurrent link of document {DocumentId} to bid {BidId} resolved to the existing row {LinkId}",
                    doc.Id, bidId, link.Id);
            }
        }

        var linkerName = await db.Users
            .Where(u => u.Id == link.LinkedBy)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);

        return new BidDocumentDto(
            Id:                 link.Id,
            Source:             "vault",
            DocumentId:         doc.Id,
            Name:               doc.Name,
            FileName:           doc.FileName,
            ContentType:        doc.MimeType,
            SizeBytes:          doc.FileSizeKb.HasValue ? doc.FileSizeKb.Value * 1024L : null,
            DocumentType:       doc.DocumentType,
            FolderName:         doc.Folder?.Name,
            ExpiryDate:         doc.ExpiryDate,
            AddedBy:            link.LinkedBy,
            AddedByName:        linkerName,
            CreatedAt:          link.CreatedAt,
            ChecklistItemTitle: null);
    }

    public async Task UnlinkAsync(Guid orgId, Guid bidId, Guid documentId, CancellationToken ct = default)
    {
        var link = await db.BidDocuments
            .FirstOrDefaultAsync(l => l.BidId == bidId && l.DocumentId == documentId && l.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Document is not linked to this bid.");

        db.BidDocuments.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureBidBelongsAsync(Guid bidId, Guid orgId, CancellationToken ct)
    {
        var exists = await db.Bids.AnyAsync(b => b.Id == bidId && b.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Bid not found.");
    }
}
