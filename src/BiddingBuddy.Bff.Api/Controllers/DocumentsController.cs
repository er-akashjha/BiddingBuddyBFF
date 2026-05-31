using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController(IDocumentService documentService) : BffControllerBase
{
    // ── Folders ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/documents/folders?parentId=</summary>
    [HttpGet("folders")]
    public async Task<IActionResult> ListFolders([FromQuery] Guid? parentId, CancellationToken ct)
    {
        var folders = await documentService.ListFoldersAsync(CurrentOrgId, parentId, ct);
        return Ok(folders);
    }

    /// <summary>GET /api/documents/folders/{id}</summary>
    [HttpGet("folders/{id:guid}")]
    public async Task<IActionResult> GetFolder(Guid id, CancellationToken ct)
    {
        var folder = await documentService.GetFolderAsync(id, CurrentOrgId, ct);
        return Ok(folder);
    }

    /// <summary>POST /api/documents/folders</summary>
    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto dto, CancellationToken ct)
    {
        var folder = await documentService.CreateFolderAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(folder);
    }

    /// <summary>PATCH /api/documents/folders/{id}</summary>
    [HttpPatch("folders/{id:guid}")]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] UpdateFolderDto dto, CancellationToken ct)
    {
        var folder = await documentService.UpdateFolderAsync(id, CurrentOrgId, dto, ct);
        return Ok(folder);
    }

    /// <summary>DELETE /api/documents/folders/{id}</summary>
    [HttpDelete("folders/{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        await documentService.DeleteFolderAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Documents ────────────────────────────────────────────────────────────

    /// <summary>GET /api/documents?folderId=&amp;documentType=</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? folderId,
        [FromQuery] string? documentType,
        CancellationToken ct)
    {
        var docs = await documentService.ListDocumentsAsync(CurrentOrgId, folderId, documentType, ct);
        return Ok(docs);
    }

    /// <summary>GET /api/documents/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var doc = await documentService.GetDocumentAsync(id, CurrentOrgId, ct);
        return Ok(doc);
    }

    /// <summary>POST /api/documents</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDocumentDto dto, CancellationToken ct)
    {
        var doc = await documentService.CreateDocumentAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = doc.Id }, doc);
    }

    /// <summary>PATCH /api/documents/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentDto dto, CancellationToken ct)
    {
        var doc = await documentService.UpdateDocumentAsync(id, CurrentOrgId, dto, ct);
        return Ok(doc);
    }

    /// <summary>DELETE /api/documents/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await documentService.DeleteDocumentAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Versions ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/documents/{id}/versions</summary>
    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> GetVersions(Guid id, CancellationToken ct)
    {
        var versions = await documentService.GetVersionsAsync(id, CurrentOrgId, ct);
        return Ok(versions);
    }

    /// <summary>POST /api/documents/{id}/versions</summary>
    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> AddVersion(Guid id, [FromBody] AddDocumentVersionDto dto, CancellationToken ct)
    {
        var version = await documentService.AddVersionAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(version);
    }
}
