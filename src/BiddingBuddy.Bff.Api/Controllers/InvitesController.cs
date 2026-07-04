using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Invite acceptance flow. Deliberately OUTSIDE the org-scoped routes: the caller
/// is not (yet) a member of the inviting org, so these endpoints must not require
/// the X-Org-Id header — the path prefix is exempted in <c>OrgContextMiddleware</c>.
/// </summary>
[ApiController]
[Route("api/invites")]
[Produces("application/json")]
public class InvitesController(IOrganizationService orgService) : BffControllerBase
{
    /// <summary>
    /// What the accept page shows before the invitee decides: org, inviter, role,
    /// invited email, and whether that email already has an account. Anonymous —
    /// the single-use high-entropy token in the emailed link is the credential.
    /// </summary>
    [HttpGet("preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(InvitePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromQuery] string token, CancellationToken ct)
    {
        try
        {
            return Ok(await orgService.GetInvitePreviewAsync(token, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_INVALID")
        {
            return NotFound(new { error = "INVITE_INVALID" });
        }
    }

    /// <summary>
    /// Accept an invite as the logged-in user → membership is created (or a suspended
    /// one reactivated) and the token is consumed. The caller's account email must
    /// match the invited email — a forwarded link can't join someone else.
    /// </summary>
    [HttpPost("accept")]
    [Authorize]
    [ProducesResponseType(typeof(AcceptInviteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept([FromBody] InviteTokenDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await orgService.AcceptInviteAsync(CurrentUserId, dto.Token, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_INVALID")
        {
            return NotFound(new { error = "INVITE_INVALID" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_EMAIL_MISMATCH")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "INVITE_EMAIL_MISMATCH" });
        }
    }

    /// <summary>
    /// Pending invites addressed to the logged-in user's email — powers the onboarding
    /// "join your team" branch for social signups (who never received a signup-time token
    /// exchange). Carries no tokens; accept is via <c>POST /api/invites/{id}/accept</c>.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<MyInviteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => Ok(await orgService.GetMyPendingInvitesAsync(CurrentUserId, ct));

    /// <summary>
    /// Accept a pending invite by id, without the emailed token. Being authenticated as
    /// the invited email is the credential — the same ownership proof the token path
    /// enforces via <c>RequireInviteeMatch</c>, minus the emailed secret.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize]
    [ProducesResponseType(typeof(AcceptInviteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptById(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await orgService.AcceptInviteByIdAsync(CurrentUserId, id, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_INVALID")
        {
            return NotFound(new { error = "INVITE_INVALID" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_EMAIL_MISMATCH")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "INVITE_EMAIL_MISMATCH" });
        }
    }

    /// <summary>Decline an invite — consumes the token without creating a membership.</summary>
    [HttpPost("decline")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Decline([FromBody] InviteTokenDto dto, CancellationToken ct)
    {
        try
        {
            await orgService.DeclineInviteAsync(CurrentUserId, dto.Token, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_INVALID")
        {
            return NotFound(new { error = "INVITE_INVALID" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITE_EMAIL_MISMATCH")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "INVITE_EMAIL_MISMATCH" });
        }
    }
}
