using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/bids")]
[Authorize]
[Produces("application/json")]
public class BidsController(IBidService bidService) : BffControllerBase
{
    /// <summary>Paginated list of bids for the org. Filter by stage (identified|qualified|proposal|submitted|won|lost|dropped) or priority (low|medium|high|critical).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BidListItemDto>), StatusCodes.Status200OK)]
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

    /// <summary>Get full bid detail including recent activities and checklist counts.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BidDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var bid = await bidService.GetAsync(id, CurrentOrgId, ct);
        return Ok(bid);
    }

    /// <summary>Create a new bid, optionally linked to a tender.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BidDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBidDto dto, CancellationToken ct)
    {
        var bid = await bidService.CreateAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = bid.Id }, bid);
    }

    /// <summary>Update bid fields (title, description, value, probability, etc.).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(BidDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBidDto dto, CancellationToken ct)
    {
        var bid = await bidService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(bid);
    }

    /// <summary>Advance or revert the bid stage and log the change in the activity feed.</summary>
    [HttpPatch("{id:guid}/stage")]
    [ProducesResponseType(typeof(BidDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStage(Guid id, [FromBody] ChangeStageDto dto, CancellationToken ct)
    {
        var bid = await bidService.ChangeStageAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(bid);
    }

    /// <summary>Permanently delete a bid and all its activities / checklist items.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await bidService.DeleteAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Activities ────────────────────────────────────────────────────────────

    /// <summary>List all activity log entries for a bid (stage changes, notes, etc.).</summary>
    [HttpGet("{id:guid}/activities")]
    [ProducesResponseType(typeof(IReadOnlyList<BidActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActivities(Guid id, CancellationToken ct)
    {
        var activities = await bidService.GetActivitiesAsync(id, CurrentOrgId, ct);
        return Ok(activities);
    }

    /// <summary>Add a plain-text note to the bid activity feed.</summary>
    [HttpPost("{id:guid}/activities")]
    [ProducesResponseType(typeof(BidActivityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteDto dto, CancellationToken ct)
    {
        var activity = await bidService.AddNoteAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(activity);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    /// <summary>List all comments on a bid, oldest first, with author names.</summary>
    [HttpGet("{id:guid}/comments")]
    [ProducesResponseType(typeof(IReadOnlyList<BidCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetComments(Guid id, CancellationToken ct)
    {
        var comments = await bidService.GetCommentsAsync(id, CurrentOrgId, ct);
        return Ok(comments);
    }

    /// <summary>Save a comment on a bid authored by the current user.</summary>
    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(BidCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentDto dto, CancellationToken ct)
    {
        var comment = await bidService.AddCommentAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(GetComments), new { id }, comment);
    }

    // ── Checklist ─────────────────────────────────────────────────────────────

    /// <summary>Get all checklist items for a bid, ordered by sort_order.</summary>
    [HttpGet("{id:guid}/checklist")]
    [ProducesResponseType(typeof(IReadOnlyList<ChecklistItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChecklist(Guid id, CancellationToken ct)
    {
        var items = await bidService.GetChecklistAsync(id, CurrentOrgId, ct);
        return Ok(items);
    }

    /// <summary>Add a new checklist item to a bid.</summary>
    [HttpPost("{id:guid}/checklist")]
    [ProducesResponseType(typeof(ChecklistItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChecklistItem(Guid id, [FromBody] CreateChecklistItemDto dto, CancellationToken ct)
    {
        var item = await bidService.CreateChecklistItemAsync(id, CurrentOrgId, dto, ct);
        return Ok(item);
    }

    /// <summary>Update a checklist item (title, done status, due date, assignee, sort order).</summary>
    [HttpPatch("{id:guid}/checklist/{itemId:guid}")]
    [ProducesResponseType(typeof(ChecklistItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChecklistItem(Guid id, Guid itemId, [FromBody] UpdateChecklistItemDto dto, CancellationToken ct)
    {
        var item = await bidService.UpdateChecklistItemAsync(itemId, id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(item);
    }

    /// <summary>Delete a checklist item.</summary>
    [HttpDelete("{id:guid}/checklist/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChecklistItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await bidService.DeleteChecklistItemAsync(itemId, id, CurrentOrgId, ct);
        return NoContent();
    }
}
