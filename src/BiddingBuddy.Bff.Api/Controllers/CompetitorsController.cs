using BiddingBuddy.Bff.Core.DTOs.Competitors;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/competitors")]
[Authorize]
public class CompetitorsController(ICompetitorService competitorService) : BffControllerBase
{
    /// <summary>GET /api/competitors?threatLevel=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? threatLevel,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await competitorService.ListAsync(CurrentOrgId, threatLevel, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>GET /api/competitors/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var competitor = await competitorService.GetAsync(id, CurrentOrgId, ct);
        return Ok(competitor);
    }

    /// <summary>PATCH /api/competitors/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompetitorDto dto, CancellationToken ct)
    {
        var competitor = await competitorService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(competitor);
    }

    /// <summary>GET /api/competitors/{id}/observations?limit=50</summary>
    [HttpGet("{id:guid}/observations")]
    public async Task<IActionResult> GetObservations(Guid id, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var observations = await competitorService.GetObservationsAsync(id, CurrentOrgId, limit, ct);
        return Ok(observations);
    }
}
