using BiddingBuddy.Bff.Core.DTOs.Integrations;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
[Produces("application/json")]
public class IntegrationsController(IGemIntegrationService gemService) : BffControllerBase
{
    /// <summary>Get the GEM portal integration settings for the org.</summary>
    [HttpGet("gem")]
    [ProducesResponseType(typeof(GemIntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGem(CancellationToken ct)
    {
        var integration = await gemService.GetAsync(CurrentOrgId, ct);
        if (integration is null) return NotFound(new { error = "No GEM integration configured for this organization." });
        return Ok(integration);
    }

    /// <summary>Create or update GEM portal integration (seller ID, sync preferences).</summary>
    [HttpPut("gem")]
    [ProducesResponseType(typeof(GemIntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertGem([FromBody] UpsertGemIntegrationDto dto, CancellationToken ct)
    {
        var integration = await gemService.UpsertAsync(CurrentOrgId, dto, ct);
        return Ok(integration);
    }
}
