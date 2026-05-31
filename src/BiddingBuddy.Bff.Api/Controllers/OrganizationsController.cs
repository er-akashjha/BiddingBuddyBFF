using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/organizations")]
[Authorize]
public class OrganizationsController(IOrganizationService orgService) : BffControllerBase
{
    /// <summary>POST /api/organizations</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrgDto dto, CancellationToken ct)
    {
        var org = await orgService.CreateAsync(CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = org.Id }, org);
    }

    /// <summary>GET /api/organizations/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var org = await orgService.GetAsync(id, CurrentUserId, ct);
        return Ok(org);
    }

    /// <summary>PATCH /api/organizations/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrgDto dto, CancellationToken ct)
    {
        var org = await orgService.UpdateAsync(id, CurrentUserId, dto, ct);
        return Ok(org);
    }

    /// <summary>DELETE /api/organizations/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await orgService.DeactivateAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>GET /api/organizations/{id}/members</summary>
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken ct)
    {
        var members = await orgService.GetMembersAsync(id, ct);
        return Ok(members);
    }

    /// <summary>POST /api/organizations/{id}/members</summary>
    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberDto dto, CancellationToken ct)
    {
        var member = await orgService.InviteMemberAsync(id, CurrentUserId, dto, ct);
        return Ok(member);
    }

    /// <summary>PATCH /api/organizations/{id}/members/{memberId}</summary>
    [HttpPatch("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> UpdateMember(Guid id, Guid memberId, [FromBody] UpdateMemberDto dto, CancellationToken ct)
    {
        var member = await orgService.UpdateMemberAsync(id, memberId, CurrentUserId, dto, ct);
        return Ok(member);
    }

    /// <summary>DELETE /api/organizations/{id}/members/{memberId}</summary>
    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken ct)
    {
        await orgService.RemoveMemberAsync(id, memberId, CurrentUserId, ct);
        return NoContent();
    }
}
