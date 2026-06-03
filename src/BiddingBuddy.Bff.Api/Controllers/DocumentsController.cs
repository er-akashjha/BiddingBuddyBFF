using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
[Produces("application/json")]
public class DocumentsController(
    IDocumentService documentService,
    IR2Storage r2Storage,
    IOptions<R2Options> r2Options) : BffControllerBase
{
    // ── MIME allowlist ────────────────────────────────────────────────────────
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/csv",
        // Images
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/tiff",
        // Archives
        "application/zip",
        "application/x-zip-compressed",
    };

    // ── A3: Presign endpoint ──────────────────────────────────────────────────

    /// <summary>
    /// Request a presigned PUT URL for direct-to-R2 upload.
    /// Upload the file using the returned URL, then call POST /api/documents
    /// with the returned objectKey to register it in the vault.
    /// </summary>
    [HttpPost("upload-url")]
    [ProducesResponseType(typeof(UploadUrlResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestUploadUrl(
        [FromBody] RequestUploadUrlDto dto, CancellationToken ct)
    {
        var cfg = r2Options.Value;

        // ── Validate fileName ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dto.FileName))
            return BadRequest(ProblemOf("fileName is required."));

        var sanitized = FileNameSanitizer.Sanitize(dto.FileName);
        if (string.IsNullOrWhiteSpace(sanitized))
            return BadRequest(ProblemOf("fileName is invalid after sanitization."));

        // ── Validate mimeType ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dto.MimeType))
            return BadRequest(ProblemOf("mimeType is required."));

        if (!AllowedMimeTypes.Contains(dto.MimeType))
            return BadRequest(ProblemOf(
                $"mimeType '{dto.MimeType}' is not allowed. " +
                $"Allowed: {string.Join(", ", AllowedMimeTypes.Order())}"));

        // ── Validate fileSizeKb ───────────────────────────────────────────────
        if (dto.FileSizeKb < 1)
            return BadRequest(ProblemOf("fileSizeKb must be at least 1."));

        if (dto.FileSizeKb > cfg.MaxUploadSizeKb)
            return BadRequest(ProblemOf(
                $"fileSizeKb {dto.FileSizeKb} exceeds the maximum of {cfg.MaxUploadSizeKb} KB ({cfg.MaxUploadSizeKb / 1024} MB)."));

        // ── Build object key — server-controlled, never client-supplied ───────
        var objectKey = $"orgs/{CurrentOrgId}/docs/{Guid.NewGuid()}/{sanitized}";

        var presigned = await r2Storage.CreatePresignedPutAsync(
            objectKey,
            dto.MimeType,
            (long)dto.FileSizeKb * 1024,
            ct);

        return Ok(new UploadUrlResponseDto(
            presigned.UploadUrl,
            presigned.ObjectKey,
            presigned.Headers,
            presigned.ExpiresAt));
    }

    private static ProblemDetails ProblemOf(string detail) => new()
    {
        Title  = "Validation Error",
        Status = StatusCodes.Status400BadRequest,
        Detail = detail,
    };


    // ── Folders ───────────────────────────────────────────────────────────────

    /// <summary>List folders at root or under a specific parent.</summary>
    [HttpGet("folders")]
    [ProducesResponseType(typeof(IReadOnlyList<FolderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFolders([FromQuery] Guid? parentId, CancellationToken ct)
    {
        var folders = await documentService.ListFoldersAsync(CurrentOrgId, parentId, ct);
        return Ok(folders);
    }

    /// <summary>Get a folder with its children and documents.</summary>
    [HttpGet("folders/{id:guid}")]
    [ProducesResponseType(typeof(FolderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken ct)
    {
        var folder = await documentService.GetFolderAsync(id, CurrentOrgId, ct);
        return Ok(folder);
    }

    /// <summary>Create a new document folder (optionally nested).</summary>
    [HttpPost("folders")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto dto, CancellationToken ct)
    {
        var folder = await documentService.CreateFolderAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(folder);
    }

    /// <summary>Rename or move a folder to a different parent.</summary>
    [HttpPatch("folders/{id:guid}")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] UpdateFolderDto dto, CancellationToken ct)
    {
        var folder = await documentService.UpdateFolderAsync(id, CurrentOrgId, dto, ct);
        return Ok(folder);
    }

    /// <summary>Delete a folder (folder must be empty).</summary>
    [HttpDelete("folders/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        await documentService.DeleteFolderAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Documents ────────────────────────────────────────────────────────────

    /// <summary>List documents optionally filtered by folder or document type.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? folderId,
        [FromQuery] string? documentType,
        CancellationToken ct)
    {
        var docs = await documentService.ListDocumentsAsync(CurrentOrgId, folderId, documentType, ct);
        return Ok(docs);
    }

    /// <summary>Get a single document with its version history.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var doc = await documentService.GetDocumentAsync(id, CurrentOrgId, ct);
        return Ok(doc);
    }

    /// <summary>Register a document already uploaded to S3.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentDto dto, CancellationToken ct)
    {
        var doc = await documentService.CreateDocumentAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = doc.Id }, doc);
    }

    /// <summary>Update document metadata (name, folder, type, expiry, tags).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentDto dto, CancellationToken ct)
    {
        var doc = await documentService.UpdateDocumentAsync(id, CurrentOrgId, dto, ct);
        return Ok(doc);
    }

    /// <summary>Delete a document and all its versions.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await documentService.DeleteDocumentAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Versions ─────────────────────────────────────────────────────────────

    /// <summary>List all versions of a document, newest first.</summary>
    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions(Guid id, CancellationToken ct)
    {
        var versions = await documentService.GetVersionsAsync(id, CurrentOrgId, ct);
        return Ok(versions);
    }

    /// <summary>Add a new version of a document already uploaded to S3.</summary>
    [HttpPost("{id:guid}/versions")]
    [ProducesResponseType(typeof(DocumentVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddVersion(Guid id, [FromBody] AddDocumentVersionDto dto, CancellationToken ct)
    {
        var version = await documentService.AddVersionAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(version);
    }
}
