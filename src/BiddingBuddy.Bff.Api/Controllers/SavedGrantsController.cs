using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// A user's org "saved / tracked" grant opportunities. Membership-only (any org member), like bids —
/// no capability gate. Own route prefix (not under <c>api/grants</c>) so the literal segments never
/// collide with <c>GrantsController</c>'s <c>{id}</c> route.
/// </summary>
[ApiController]
[Route("api/saved-grants")]
[Authorize]
[Produces("application/json")]
public class SavedGrantsController(ISavedGrantService savedGrants) : BffControllerBase
{
    /// <summary>The org's saved grants, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SavedGrantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await savedGrants.ListAsync(CurrentOrgId, ct));

    /// <summary>Just the saved grants' Mongo ids — the discovery list uses this to mark saved rows.</summary>
    [HttpGet("ids")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIds(CancellationToken ct)
        => Ok(await savedGrants.ListIdsAsync(CurrentOrgId, ct));

    /// <summary>Save (or re-save) a grant. Idempotent on (org, grant); refreshes the snapshot.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SavedGrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save([FromBody] SaveGrantRequest dto, CancellationToken ct)
        => Ok(await savedGrants.SaveAsync(CurrentOrgId, CurrentUserId, dto, ct));

    /// <summary>Remove a saved grant by its Mongo id. Idempotent.</summary>
    [HttpDelete("{mongoGrantId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unsave(string mongoGrantId, CancellationToken ct)
    {
        await savedGrants.UnsaveAsync(CurrentOrgId, mongoGrantId, ct);
        return NoContent();
    }
}
