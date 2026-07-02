using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/organizations")]
[Authorize]
[Produces("application/json")]
public class OrganizationsController(IOrganizationService orgService) : BffControllerBase
{
    /// <summary>Create a new organization. The calling user becomes the owner.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrgDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrgDto dto, CancellationToken ct)
    {
        var org = await orgService.CreateAsync(CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = org.Id }, org);
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
    /// Invite a user to the organization by email (owner or admin only).
    /// If the email belongs to an existing user, they're added immediately
    /// (response <c>status="added"</c>, populated <c>member</c>). Otherwise a
    /// pending invite is created and an email with a registration link is sent
    /// (response <c>status="invited"</c>, populated <c>invitedEmail</c> + <c>expiresAt</c>).
    /// </summary>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(InviteMemberResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberDto dto, CancellationToken ct)
    {
        var result = await orgService.InviteMemberAsync(id, CurrentUserId, dto, ct);
        return Ok(result);
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
