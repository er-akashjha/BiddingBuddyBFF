using BiddingBuddy.Bff.Core.DTOs.Integrations;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController(IGemIntegrationService gemService) : BffControllerBase
{
    /// <summary>GET /api/integrations/gem</summary>
    [HttpGet("gem")]
    public async Task<IActionResult> GetGem(CancellationToken ct)
    {
        var integration = await gemService.GetAsync(CurrentOrgId, ct);
        if (integration is null) return NotFound(new { error = "No GEM integration configured for this organization." });
        return Ok(integration);
    }

    /// <summary>PUT /api/integrations/gem</summary>
    [HttpPut("gem")]
    public async Task<IActionResult> UpsertGem([FromBody] UpsertGemIntegrationDto dto, CancellationToken ct)
    {
        var integration = await gemService.UpsertAsync(CurrentOrgId, dto, ct);
        return Ok(integration);
    }
}
