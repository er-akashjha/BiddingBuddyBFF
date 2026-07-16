using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class DocumentService(BffDbContext db) : IDocumentService
{
    /// <summary>
    /// Depth cap for the ancestry walk in <see cref="EnsureNoCycleAsync"/>. Cycles could already
    /// exist in data written before that check, so the walk must not be able to spin forever.
    /// Real vault trees are a handful of levels deep.
    /// </summary>
    private const int MaxFolderDepth = 64;

    // ── Folders ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FolderDto>> ListFoldersAsync(Guid orgId, Guid? parentId, CancellationToken ct = default)
    {
        var folders = await db.DocumentFolders
            .Where(f => f.OrgId == orgId && f.ParentId == parentId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

        var docCounts = await db.Documents
            .Where(d => d.OrgId == orgId && d.FolderId != null)
            .GroupBy(d => d.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

        var childCounts = await db.DocumentFolders
            .Where(f => f.OrgId == orgId && f.ParentId != null)
            .GroupBy(f => f.ParentId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count, ct);

        return folders.Select(f => new FolderDto(
            f.Id, f.Name, f.ParentId,
            docCounts.GetValueOrDefault(f.Id),
            childCounts.GetValueOrDefault(f.Id),
            f.CreatedAt)).ToList();
    }

    public async Task<FolderDetailDto> GetFolderAsync(Guid folderId, Guid orgId, CancellationToken ct = default)
    {
        var folder = await db.DocumentFolders
            .Include(f => f.Children)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Folder not found.");

        var children = folder.Children.Select(c => new FolderDto(c.Id, c.Name, c.ParentId, 0, 0, c.CreatedAt)).ToList();
        var docs = folder.Documents.Select(d => MapDocument(d, null)).ToList();

        return new FolderDetailDto(folder.Id, folder.Name, folder.ParentId, folder.CreatedAt, children, docs);
    }

    public async Task<FolderDto> CreateFolderAsync(Guid orgId, Guid userId, CreateFolderDto dto, CancellationToken ct = default)
    {
        await EnsureFolderBelongsAsync(dto.ParentId, orgId, ct);

        var folder = new DocumentFolder
        {
            OrgId     = orgId,
            Name      = dto.Name,
            ParentId  = dto.ParentId,
            CreatedBy = userId,
        };
        db.DocumentFolders.Add(folder);
        await db.SaveChangesAsync(ct);
        return new FolderDto(folder.Id, folder.Name, folder.ParentId, 0, 0, folder.CreatedAt);
    }

    public async Task<FolderDto> UpdateFolderAsync(Guid folderId, Guid orgId, UpdateFolderDto dto, CancellationToken ct = default)
    {
        var folder = await db.DocumentFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Folder not found.");

        await EnsureFolderBelongsAsync(dto.ParentId, orgId, ct);
        await EnsureNoCycleAsync(folderId, dto.ParentId, orgId, ct);

        folder.Name     = dto.Name;
        folder.ParentId = dto.ParentId;
        await db.SaveChangesAsync(ct);
        return new FolderDto(folder.Id, folder.Name, folder.ParentId, 0, 0, folder.CreatedAt);
    }

    public async Task DeleteFolderAsync(Guid folderId, Guid orgId, CancellationToken ct = default)
    {
        var folder = await db.DocumentFolders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Folder not found.");
        db.DocumentFolders.Remove(folder);
        await db.SaveChangesAsync(ct);
    }

    // ── Documents ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentDto>> ListDocumentsAsync(Guid orgId, Guid? folderId, string? documentType, CancellationToken ct = default)
    {
        var query = db.Documents.Where(d => d.OrgId == orgId);
        if (folderId.HasValue)
            query = query.Where(d => d.FolderId == folderId);
        if (!string.IsNullOrWhiteSpace(documentType))
            query = query.Where(d => d.DocumentType == documentType);

        var docs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        var uploaderIds = docs.Select(d => d.UploadedBy).Distinct().ToList();
        var uploaders   = await db.Users
            .Where(u => uploaderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return docs.Select(d => MapDocument(d, uploaders.GetValueOrDefault(d.UploadedBy))).ToList();
    }

    public async Task<DocumentDto> GetDocumentAsync(Guid documentId, Guid orgId, CancellationToken ct = default)
    {
        var doc = await db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Document not found.");

        var uploader = await db.Users.FindAsync([doc.UploadedBy], ct);
        return MapDocument(doc, uploader?.Name, doc.Versions.Count);
    }

    public async Task<DocumentDto> CreateDocumentAsync(Guid orgId, Guid userId, CreateDocumentDto dto, CancellationToken ct = default)
    {
        EnsureKeyBelongsToOrg(dto.S3Key, orgId);
        await EnsureFolderBelongsAsync(dto.FolderId, orgId, ct);

        var doc = new Document
        {
            OrgId        = orgId,
            FolderId     = dto.FolderId,
            Name         = dto.Name,
            FileName     = dto.FileName,
            S3Key        = dto.S3Key,
            S3VersionId  = dto.S3VersionId,
            FileSizeKb   = dto.FileSizeKb,
            MimeType     = dto.MimeType,
            DocumentType = dto.DocumentType,
            ExpiryDate   = dto.ExpiryDate,
            Tags         = dto.Tags,
            UploadedBy   = userId,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);

        var uploader = await db.Users.FindAsync([userId], ct);
        return MapDocument(doc, uploader?.Name);
    }

    public async Task<DocumentDto> UpdateDocumentAsync(Guid documentId, Guid orgId, UpdateDocumentDto dto, CancellationToken ct = default)
    {
        var doc = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Document not found.");

        await EnsureFolderBelongsAsync(dto.FolderId, orgId, ct);

        if (dto.Name         is not null) doc.Name         = dto.Name;
        if (dto.FolderId.HasValue)        doc.FolderId     = dto.FolderId;
        if (dto.DocumentType is not null) doc.DocumentType = dto.DocumentType;
        if (dto.ExpiryDate.HasValue)      doc.ExpiryDate   = dto.ExpiryDate;
        if (dto.Tags         is not null) doc.Tags         = dto.Tags;

        await db.SaveChangesAsync(ct);
        var uploader = await db.Users.FindAsync([doc.UploadedBy], ct);
        return MapDocument(doc, uploader?.Name);
    }

    public async Task DeleteDocumentAsync(Guid documentId, Guid orgId, CancellationToken ct = default)
    {
        var doc = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Document not found.");
        db.Documents.Remove(doc);
        await db.SaveChangesAsync(ct);
    }

    // ── Versions ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentVersionDto>> GetVersionsAsync(Guid documentId, Guid orgId, CancellationToken ct = default)
    {
        var exists = await db.Documents.AnyAsync(d => d.Id == documentId && d.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Document not found.");

        var versions = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNum)
            .ToListAsync(ct);

        var uploaderIds = versions.Select(v => v.UploadedBy).Distinct().ToList();
        var uploaders   = await db.Users
            .Where(u => uploaderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return versions.Select(v => new DocumentVersionDto(
            v.Id, v.S3Key, v.S3VersionId, v.FileSizeKb, null,
            v.UploadedBy, uploaders.GetValueOrDefault(v.UploadedBy), v.CreatedAt)).ToList();
    }

    public async Task<DocumentVersionDto> AddVersionAsync(Guid documentId, Guid orgId, Guid userId, AddDocumentVersionDto dto, CancellationToken ct = default)
    {
        EnsureKeyBelongsToOrg(dto.S3Key, orgId);

        var exists = await db.Documents.AnyAsync(d => d.Id == documentId && d.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Document not found.");

        var nextNum = await db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNum, ct) + 1 ?? 1;

        var version = new DocumentVersion
        {
            DocumentId  = documentId,
            VersionNum  = nextNum,
            S3Key       = dto.S3Key,
            S3VersionId = dto.S3VersionId,
            FileSizeKb  = dto.FileSizeKb,
            UploadedBy  = userId,
        };
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(ct);

        var uploader = await db.Users.FindAsync([userId], ct);
        return new DocumentVersionDto(
            version.Id, version.S3Key, version.S3VersionId,
            version.FileSizeKb, dto.Notes, userId, uploader?.Name, version.CreatedAt);
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Vault object keys are always server-generated by POST /api/documents/upload-url as
    /// orgs/{orgId}/docs/{guid}/{file}. Registration hands the key back from the client, so the
    /// org prefix has to be re-checked here: without it a member of org A can register org B's
    /// key and then presign a read of B's file through /view-url or /download-url.
    /// Mirrors the guard in <see cref="BidAttachmentService.RegisterAsync"/>.
    /// </summary>
    private static void EnsureKeyBelongsToOrg(string? s3Key, Guid orgId)
    {
        if (string.IsNullOrWhiteSpace(s3Key))
            throw new ArgumentException("s3Key is required.");

        if (!s3Key.StartsWith($"orgs/{orgId}/docs/", StringComparison.Ordinal))
            throw new ArgumentException("s3Key does not belong to this organization.");

        // orgs/{mine}/docs/../../{theirs}/docs/x clears the prefix test on its own.
        if (s3Key.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("s3Key must not contain relative path segments.");
    }

    /// <summary>
    /// A folder id crossing an org boundary is reported as not-found rather than forbidden, so the
    /// response can't be used to probe which folder ids exist in other orgs.
    /// </summary>
    private async Task EnsureFolderBelongsAsync(Guid? folderId, Guid orgId, CancellationToken ct)
    {
        if (!folderId.HasValue) return;

        var exists = await db.DocumentFolders.AnyAsync(f => f.Id == folderId.Value && f.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Folder not found.");
    }

    /// <summary>
    /// Walks up from the proposed parent. Reaching <paramref name="folderId"/> means the move would
    /// make the folder its own ancestor, detaching the whole subtree from the root listing.
    /// </summary>
    private async Task EnsureNoCycleAsync(Guid folderId, Guid? newParentId, Guid orgId, CancellationToken ct)
    {
        var cursor = newParentId;

        for (var depth = 0; cursor.HasValue; depth++)
        {
            if (cursor.Value == folderId)
                throw new ArgumentException("A folder cannot be moved into itself or one of its own descendants.");

            if (depth >= MaxFolderDepth)
                throw new InvalidOperationException("Folder nesting is too deep to validate.");

            // Null once the walk reaches a root folder — or a row outside this org, which
            // ListFolders would never surface anyway.
            cursor = await db.DocumentFolders
                .Where(f => f.Id == cursor.Value && f.OrgId == orgId)
                .Select(f => f.ParentId)
                .FirstOrDefaultAsync(ct);
        }
    }

    private static DocumentDto MapDocument(Document d, string? uploaderName, int versionCount = 0)
        => new(d.Id, d.Name, d.FileName, d.S3Key, d.FolderId, null,
            d.FileSizeKb, d.MimeType, d.DocumentType, d.ExpiryDate, d.Tags,
            d.HealthScore, d.UploadedBy, uploaderName, d.CreatedAt, d.UpdatedAt, versionCount);
}
