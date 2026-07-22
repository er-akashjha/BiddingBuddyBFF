using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// The requester's half of "my company is already on TendersAgent, let me in".
///
/// <para>Deliberately OUTSIDE the org-scoped routes, for the same reason
/// <c>InvitesController</c> is: the caller is by definition not a member of the org they
/// are pointing at, so these must not require the X-Org-Id header. The prefix is exempted
/// in <c>OrgContextMiddleware</c>.</para>
///
/// <para>The decision half (list / approve / reject) lives on
/// <c>OrganizationsController</c>, where org context and the owner/admin gate do apply.</para>
/// </summary>
[ApiController]
[Route("api/join-requests")]
[Authorize]
[Produces("application/json")]
public class JoinRequestsController(IJoinRequestService joinRequests) : BffControllerBase
{
    /// <summary>
    /// Ask to join an existing organization. Idempotent — asking twice returns the same
    /// pending request rather than stacking rows in the approver's queue.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(JoinRequestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    // Named Create, not Request: `Request` would hide ControllerBase.Request (the HttpRequest),
    // which compiles with a warning and quietly breaks anything in this class reaching for it.
    public async Task<IActionResult> Create([FromBody] CreateJoinRequestDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await joinRequests.RequestAsync(CurrentUserId, dto, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ALREADY_MEMBER")
        {
            // Not an error the user caused — they may simply have been added while the
            // onboarding tab was open. The client reads this as "you're already in, go on through".
            return Conflict(new { error = "ALREADY_MEMBER" });
        }
    }

    /// <summary>
    /// The caller's own requests: live ones plus decisions from the last 30 days. Powers the
    /// onboarding waiting state and the "your request was declined" message.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<MyJoinRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => Ok(await joinRequests.GetMineAsync(CurrentUserId, ct));

    /// <summary>Withdraw one's own pending request.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await joinRequests.CancelAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}
