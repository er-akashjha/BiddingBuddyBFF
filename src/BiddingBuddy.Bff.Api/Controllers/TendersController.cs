using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/tenders")]
[Authorize]
public class TendersController(ITenderService tenderService) : BffControllerBase
{
    /// <summary>GET /api/tenders?search=&amp;category=&amp;state=&amp;status=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TenderFiltersDto filters, CancellationToken ct)
    {
        var result = await tenderService.ListAsync(CurrentOrgId, filters, ct);
        return Ok(result);
    }

    /// <summary>GET /api/tenders/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tender = await tenderService.GetAsync(id, CurrentOrgId, ct);
        return Ok(tender);
    }

    /// <summary>POST /api/tenders/{id}/save</summary>
    [HttpPost("{id:guid}/save")]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.SaveAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(settings);
    }

    /// <summary>DELETE /api/tenders/{id}/save</summary>
    [HttpDelete("{id:guid}/save")]
    public async Task<IActionResult> Unsave(Guid id, CancellationToken ct)
    {
        await tenderService.UnsaveAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>PATCH /api/tenders/{id}/settings</summary>
    [HttpPatch("{id:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.UpdateSettingsAsync(id, CurrentOrgId, dto, ct);
        return Ok(settings);
    }
}
