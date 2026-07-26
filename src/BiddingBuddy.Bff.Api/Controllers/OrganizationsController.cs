using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/organizations")]
[Authorize]
[Produces("application/json")]
public class OrganizationsController(
    IOrganizationService orgService,
    IJoinRequestService joinRequests) : BffControllerBase
{
    /// <summary>
    /// Create a new organization. The calling user becomes the owner.
    ///
    /// <para>409 <c>ORG_EXISTS</c> when the company already has a workspace. The body carries
    /// the matched org and whether the match can be overridden, so the client can offer
    /// "request to join" instead of a dead end — see <see cref="OrgExistsDto"/>.</para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrgDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OrgExistsDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateOrgDto dto, CancellationToken ct)
    {
        try
        {
            var org = await orgService.CreateAsync(CurrentUserId, dto, ct);
            return CreatedAtAction(nameof(Get), new { id = org.Id }, org);
        }
        catch (DuplicateOrganizationException ex)
        {
            // Caught here rather than left to GlobalExceptionHandler: that maps to a bare
            // ProblemDetails and would drop the payload, leaving the SPA able to see
            // "conflict" but not which organization it conflicted with.
            return Conflict(ex.Conflict);
        }
    }

    /// <summary>Get organization detail including member list.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrgDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var org = await orgService.GetAsync(id, CurrentUserId, ct);
        return Ok(org);
    }

    /// <summary>Update organization profile fields (owner or admin only).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(OrgDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrgDto dto, CancellationToken ct)
    {
        var org = await orgService.UpdateAsync(id, CurrentUserId, dto, ct);
        return Ok(org);
    }

    /// <summary>Deactivate (soft-delete) the organization. Owner only.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await orgService.DeactivateAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>List all members of the organization.</summary>
    [HttpGet("{id:guid}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<OrgMemberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var members = await orgService.GetMembersAsync(id, ct);
        return Ok(members);
    }

    /// <summary>
    /// Recent bid activity across the whole organization (newest first) — powers
    /// the SPA Team page's Activity Log tab. Caller must be an active org member.
    /// </summary>
    [HttpGet("{id:guid}/activities")]
    [ProducesResponseType(typeof(IReadOnlyList<OrgActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActivities(Guid id, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var activities = await orgService.GetRecentActivitiesAsync(id, CurrentUserId, limit, ct);
        return Ok(activities);
    }

    /// <summary>
    /// Invite a user to the organization by email (owner or admin only). Always creates
    /// a pending invite (<c>status="invited"</c>) — membership requires the invitee's
    /// explicit confirmation. Existing users get an email linking to the SPA accept page;
    /// unregistered emails get a registration link. 409 if already an active member.
    /// </summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(InviteMemberResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberDto dto, CancellationToken ct)
    {
        try
        {
            var result = await orgService.InviteMemberAsync(id, CurrentUserId, dto, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message == "ALREADY_MEMBER")
        {
            return Conflict(new { error = "ALREADY_MEMBER" });
        }
    }

    /// <summary>
    /// List pending invites for the org (rows where the invitee has not yet
    /// registered). Used by the SPA's Teams page to show "Pending" rows next
    /// to active members.
    /// </summary>
    [HttpGet("{id:guid}/invites")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingInviteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingInvites(Guid id, CancellationToken ct)
    {
        var invites = await orgService.GetPendingInvitesAsync(id, ct);
        return Ok(invites);
    }

    /// <summary>Revoke a pending invite (the token can no longer be redeemed). Owner/admin only.</summary>
    [HttpDelete("{id:guid}/invites/{inviteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokePendingInvite(Guid id, Guid inviteId, CancellationToken ct)
    {
        await orgService.RevokePendingInviteAsync(id, inviteId, CurrentUserId, ct);
        return NoContent();
    }

    // ── Join requests (decision side) ────────────────────────────────────────
    //
    // The requester-facing half is /api/join-requests, which runs without org context.
    // These are org-scoped because the caller IS a member here — X-Org-Id is present and
    // the service applies the owner/admin gate on top.

    /// <summary>Pending requests to join this org, oldest first. Owner/admin only.</summary>
    [HttpGet("{id:guid}/join-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<OrgJoinRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetJoinRequests(Guid id, CancellationToken ct)
        => Ok(await joinRequests.GetPendingForOrgAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Approve a join request → the requester becomes a member with the role chosen here.
    /// Owner/admin only. The role comes from the approver, never from the request.
    /// </summary>
    [HttpPost("{id:guid}/join-requests/{requestId:guid}/approve")]
    [ProducesResponseType(typeof(OrgMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveJoinRequest(
        Guid id, Guid requestId, [FromBody] ApproveJoinRequestDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await joinRequests.ApproveAsync(id, requestId, CurrentUserId, dto, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "REQUEST_ALREADY_DECIDED")
        {
            // Two admins opened the queue at once. The first decision stands.
            return Conflict(new { error = "REQUEST_ALREADY_DECIDED" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_ROLE")
        {
            return BadRequest(new { error = "INVALID_ROLE" });
        }
    }

    /// <summary>Decline a join request. Owner/admin only.</summary>
    [HttpPost("{id:guid}/join-requests/{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectJoinRequest(Guid id, Guid requestId, CancellationToken ct)
    {
        try
        {
            await joinRequests.RejectAsync(id, requestId, CurrentUserId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message == "REQUEST_ALREADY_DECIDED")
        {
            return Conflict(new { error = "REQUEST_ALREADY_DECIDED" });
        }
    }

    /// <summary>Update a member's role or department (owner or admin only).</summary>
    [HttpPatch("{id:guid}/members/{memberId:guid}")]
    [ProducesResponseType(typeof(OrgMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMember(Guid id, Guid memberId, [FromBody] UpdateMemberDto dto, CancellationToken ct)
    {
        var member = await orgService.UpdateMemberAsync(id, memberId, CurrentUserId, dto, ct);
        return Ok(member);
    }

    /// <summary>Remove a member from the organization (cannot remove the owner).</summary>
    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken ct)
    {
        await orgService.RemoveMemberAsync(id, memberId, CurrentUserId, ct);
        return NoContent();
    }
}
