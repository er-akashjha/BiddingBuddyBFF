using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Competitors;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/competitors")]
[Authorize]
[Produces("application/json")]
public class CompetitorsController(ICompetitorService competitorService) : BffControllerBase
{
    /// <summary>Paginated competitor list. Filter by threat level (high|medium|low).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CompetitorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? threatLevel,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await competitorService.ListAsync(CurrentOrgId, threatLevel, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Get competitor detail with recent bid observations.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CompetitorDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var competitor = await competitorService.GetAsync(id, CurrentOrgId, ct);
        return Ok(competitor);
    }

    /// <summary>Update competitor tier or threat level classification.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(CompetitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompetitorDto dto, CancellationToken ct)
    {
        var competitor = await competitorService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(competitor);
    }

    /// <summary>List bid observations for a competitor (newest first).</summary>
    [HttpGet("{id:guid}/observations")]
    [ProducesResponseType(typeof(IReadOnlyList<BidObservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetObservations(Guid id, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var observations = await competitorService.GetObservationsAsync(id, CurrentOrgId, limit, ct);
        return Ok(observations);
    }
}
