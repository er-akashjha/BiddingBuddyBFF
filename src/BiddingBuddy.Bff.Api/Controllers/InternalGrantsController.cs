using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Internal grant-ingest endpoints called by the BidProcessor pipeline.
/// Protected by the X-Api-Key header (<c>Pipeline:ApiKey</c>). Not for client use.
///
/// <para>Its own controller and route prefix rather than more actions on
/// <c>InternalController</c> — matching how <c>InternalMatchingController</c> and
/// <c>InternalDigestsController</c> are split, and keeping the grant product line's surface
/// separable if the deployment is ever split (PLAN §0.2).</para>
/// </summary>
[ApiController]
[Route("internal/grants")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalGrantsController(IInternalGrantPipelineService pipeline) : ControllerBase
{
    /// <summary>Upsert a grant from pipeline enrichment. Returns 201 on create, 200 on update.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UpsertGrantResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(UpsertGrantResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpsertGrant([FromBody] UpsertGrantDto dto, CancellationToken ct)
    {
        // Without the natural key there is nothing to upsert against, and a blank id would collapse
        // every grant from the source onto one row.
        if (string.IsNullOrWhiteSpace(dto.PlatformGrantId))
            return BadRequest(new ProblemDetails { Title = "platformGrantId is required." });

        var result = await pipeline.UpsertGrantAsync(dto, ct);
        return result.Created ? StatusCode(201, result) : Ok(result);
    }
}
