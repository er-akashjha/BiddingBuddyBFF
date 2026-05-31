using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/bids")]
[Authorize]
public class BidsController(IBidService bidService) : BffControllerBase
{
    /// <summary>GET /api/bids?stage=&amp;priority=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? stage,
        [FromQuery] string? priority,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await bidService.ListAsync(CurrentOrgId, stage, priority, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>GET /api/bids/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var bid = await bidService.GetAsync(id, CurrentOrgId, ct);
        return Ok(bid);
    }

    /// <summary>POST /api/bids</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBidDto dto, CancellationToken ct)
    {
        var bid = await bidService.CreateAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = bid.Id }, bid);
    }

    /// <summary>PATCH /api/bids/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBidDto dto, CancellationToken ct)
    {
        var bid = await bidService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(bid);
    }

    /// <summary>PATCH /api/bids/{id}/stage</summary>
    [HttpPatch("{id:guid}/stage")]
    public async Task<IActionResult> ChangeStage(Guid id, [FromBody] ChangeStageDto dto, CancellationToken ct)
    {
        var bid = await bidService.ChangeStageAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(bid);
    }

    /// <summary>DELETE /api/bids/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await bidService.DeleteAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Activities ────────────────────────────────────────────────────────────

    /// <summary>GET /api/bids/{id}/activities</summary>
    [HttpGet("{id:guid}/activities")]
    public async Task<IActionResult> GetActivities(Guid id, CancellationToken ct)
    {
        var activities = await bidService.GetActivitiesAsync(id, CurrentOrgId, ct);
        return Ok(activities);
    }

    /// <summary>POST /api/bids/{id}/activities</summary>
    [HttpPost("{id:guid}/activities")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteDto dto, CancellationToken ct)
    {
        var activity = await bidService.AddNoteAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(activity);
    }

    // ── Checklist ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/bids/{id}/checklist</summary>
    [HttpGet("{id:guid}/checklist")]
    public async Task<IActionResult> GetChecklist(Guid id, CancellationToken ct)
    {
        var items = await bidService.GetChecklistAsync(id, CurrentOrgId, ct);
        return Ok(items);
    }

    /// <summary>POST /api/bids/{id}/checklist</summary>
    [HttpPost("{id:guid}/checklist")]
    public async Task<IActionResult> AddChecklistItem(Guid id, [FromBody] CreateChecklistItemDto dto, CancellationToken ct)
    {
        var item = await bidService.CreateChecklistItemAsync(id, CurrentOrgId, dto, ct);
        return Ok(item);
    }

    /// <summary>PATCH /api/bids/{id}/checklist/{itemId}</summary>
    [HttpPatch("{id:guid}/checklist/{itemId:guid}")]
    public async Task<IActionResult> UpdateChecklistItem(Guid id, Guid itemId, [FromBody] UpdateChecklistItemDto dto, CancellationToken ct)
    {
        var item = await bidService.UpdateChecklistItemAsync(itemId, id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(item);
    }

    /// <summary>DELETE /api/bids/{id}/checklist/{itemId}</summary>
    [HttpDelete("{id:guid}/checklist/{itemId:guid}")]
    public async Task<IActionResult> DeleteChecklistItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await bidService.DeleteChecklistItemAsync(itemId, id, CurrentOrgId, ct);
        return NoContent();
    }
}
