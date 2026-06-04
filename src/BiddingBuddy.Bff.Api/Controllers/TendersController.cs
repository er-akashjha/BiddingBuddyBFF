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
    IBiddingBuddyServicesClient servicesClient) : BffControllerBase
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
