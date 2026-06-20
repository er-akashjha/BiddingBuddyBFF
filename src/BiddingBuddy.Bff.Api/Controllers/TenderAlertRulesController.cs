using BiddingBuddy.Bff.Core.DTOs.Alerts;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Client "interests" — the criteria that buffer matched tenders for a digest.
/// Org-scoped (Bearer JWT + X-Org-Id). See <c>POST /internal/digests/flush</c> and
/// the matching engine for how rules turn into notifications.
/// </summary>
[ApiController]
[Route("api/tender-alert-rules")]
[Authorize]
[Produces("application/json")]
public class TenderAlertRulesController(ITenderAlertRuleService service) : BffControllerBase
{
    /// <summary>List this org's alert rules (newest first).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TenderAlertRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await service.ListAsync(CurrentOrgId, ct));

    /// <summary>Create a new alert rule.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TenderAlertRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateTenderAlertRuleDto dto, CancellationToken ct)
    {
        var rule = await service.CreateAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return StatusCode(201, rule);
    }

    /// <summary>Update an alert rule.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(TenderAlertRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenderAlertRuleDto dto, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, CurrentOrgId, dto, ct));

    /// <summary>Delete an alert rule.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>Get this org's digest delivery settings (defaults if never set).</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(OrgAlertSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => Ok(await service.GetSettingsAsync(CurrentOrgId, ct));

    /// <summary>Update this org's digest delivery settings (batch size, channels, roles).</summary>
    [HttpPatch("settings")]
    [ProducesResponseType(typeof(OrgAlertSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateOrgAlertSettingsDto dto, CancellationToken ct)
        => Ok(await service.UpdateSettingsAsync(CurrentOrgId, dto, ct));
}
