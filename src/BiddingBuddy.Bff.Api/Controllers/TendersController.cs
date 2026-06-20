using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/tenders")]
[Authorize]
[Produces("application/json")]
public class TendersController(
    ITenderService tenderService,
    IBiddingBuddyServicesClient servicesClient,
    ITenderFileStorage tenderFileStorage) : BffControllerBase
{
    /// <summary>Tender list from BiddingBuddyServices (MongoDB). Only provided filters are forwarded.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TenderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        var result = await servicesClient.SearchTendersAsync(query, ct);
        return Ok(result);
    }

    /// <summary>Paged tender list from BiddingBuddyServices (MongoDB). Forwards pagination metadata to the client.</summary>
    [HttpGet("paged")]
    [ProducesResponseType(typeof(PagedTenderListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPaged([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        var result = await servicesClient.SearchTendersPagedAsync(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Distinct filter option values (categories, states) present in the tender data.
    /// Drives the UI filter dropdowns so they stay in sync with the scraped data.
    /// </summary>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(TenderFacetsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Facets([FromQuery] int limit = 15, CancellationToken ct = default)
    {
        var result = await servicesClient.GetTenderFacetsAsync(limit, ct);
        return Ok(result);
    }

    /// <summary>
    /// Type-ahead options for a single facet field ("category" or "state").
    /// Empty <c>search</c> → default top-<c>limit</c> set; a non-empty search returns
    /// every matching value (pass <c>limit=0</c> for no cap). Backs the multi-select filters.
    /// </summary>
    [HttpGet("facet-options")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FacetOptions(
        [FromQuery] string field,
        [FromQuery] string? search,
        [FromQuery] int limit = 15,
        CancellationToken ct = default)
    {
        var values = await servicesClient.GetTenderFacetOptionsAsync(field, search, limit, ct);
        return Ok(values);
    }

    /// <summary>Full tender detail by ID from BiddingBuddyServices.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TenderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tender = await servicesClient.GetTenderAsync(id.ToString(), ct);
        return Ok(tender);
    }

    /// <summary>
    /// Generate a short-lived presigned URL for a scraped tender document (PDF) in S3.
    /// The bytes never flow through the BFF. Uses the document's stored s3Key when
    /// present, otherwise reconstructs it from the platform tender id + document id
    /// (for tenders enriched before s3Key was persisted).
    /// Pass <c>inline=true</c> to open in-browser (view); default is an attachment (download).
    /// </summary>
    [HttpGet("{id:guid}/documents/{documentId}/download-url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDocumentDownloadUrl(
        Guid id, string documentId, CancellationToken ct, [FromQuery] bool inline = false)
    {
        var tender = await servicesClient.GetRawTenderAsync(id.ToString(), ct);
        if (tender is null) return NotFound();

        var doc = tender.Documents?.FirstOrDefault(d => d.DocumentId == documentId);
        if (doc is null) return NotFound();

        var bucket = !string.IsNullOrWhiteSpace(doc.S3Bucket)
            ? doc.S3Bucket!
            : tenderFileStorage.DefaultBucket;

        var key = !string.IsNullOrWhiteSpace(doc.S3Key)
            ? doc.S3Key!
            : tenderFileStorage.ReconstructKey(tender.Source?.PlatformTenderId ?? string.Empty, documentId);

        var fileName = !string.IsNullOrWhiteSpace(doc.FileName) ? doc.FileName! : $"{documentId}.pdf";

        var presigned = await tenderFileStorage.CreatePresignedGetAsync(bucket, key, fileName, inline: inline, ct);
        return Ok(new { url = presigned.Url, expiresAt = presigned.ExpiresAt });
    }

    /// <summary>Save a tender to the org with optional notes, tags and custom score.</summary>
    [HttpPost("{id:guid}/save")]
    [ProducesResponseType(typeof(OrgTenderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.SaveAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(settings);
    }

    /// <summary>Remove a tender from the org's saved list.</summary>
    [HttpDelete("{id:guid}/save")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unsave(Guid id, CancellationToken ct)
    {
        await tenderService.UnsaveAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>Update org-specific notes, tags and custom score for a saved tender.</summary>
    [HttpPatch("{id:guid}/settings")]
    [ProducesResponseType(typeof(OrgTenderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.UpdateSettingsAsync(id, CurrentOrgId, dto, ct);
        return Ok(settings);
    }
}
