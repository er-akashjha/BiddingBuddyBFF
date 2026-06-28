using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Documents;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/bids")]
[Authorize]
[Produces("application/json")]
public class BidsController(IBidService bidService, IBidAttachmentService attachmentService) : BffControllerBase
{
    /// <summary>
    /// Paginated list of bids for the org. Filter by <c>stage</c>
    /// (identified|reviewing|preparing|approval|submitted|won|lost|dropped),
    /// <c>priority</c> (low|medium|high|critical), <c>statusCategory</c> (open|closed),
    /// <c>assignedTo</c> (a user id or the literal <c>me</c>), <c>dueBefore</c> (ISO date),
    /// and free-text <c>q</c> (matches title + GeM ref). Sort with <c>sort</c> =
    /// updatedAt|dueDate|priority|title, optionally prefixed with '-' for descending
    /// (default: newest-updated first).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BidListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? stage,
        [FromQuery] string? priority,
        [FromQuery] string? statusCategory,
        [FromQuery] string? assignedTo,
        [FromQuery] DateOnly? dueBefore,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        Guid? assignee = null;
        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            if (assignedTo.Equals("me", StringComparison.OrdinalIgnoreCase))
                assignee = CurrentUserId;
            else if (Guid.TryParse(assignedTo, out var g))
                assignee = g;
            else
                throw new ArgumentException("assignedTo must be 'me' or a user id.", nameof(assignedTo));
        }

        var query = new BidListQuery(stage, priority, statusCategory, q, assignee, dueBefore, sort, page, pageSize);
        var result = await bidService.ListAsync(CurrentOrgId, CurrentUserId, query, ct);
        return Ok(result);
    }

    /// <summary>
    /// For the supplied <c>tenderIds</c> (repeated query param), return which already have a bid
    /// in this org — one entry per tender, newest bid wins. Tenders with no bid are omitted.
    /// Lets the tender list + detail pages flag already-in-pipeline tenders (owner + stage) in a
    /// single round trip instead of one lookup per row.
    /// </summary>
    [HttpGet("by-tender")]
    [ProducesResponseType(typeof(IReadOnlyList<BidByTenderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByTender([FromQuery] Guid[] tenderIds, CancellationToken ct)
    {
        var result = await bidService.GetByTenderIdsAsync(CurrentOrgId, tenderIds, ct);
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
        var item = await bidService.CreateChecklistItemAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
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

    /// <summary>
    /// Close a task. A non-empty note is mandatory (400 otherwise) and is recorded as a
    /// task-completion comment in the Notes feed; an optional attachmentId links a
    /// previously-registered file to that note.
    /// </summary>
    [HttpPost("{id:guid}/checklist/{itemId:guid}/complete")]
    [ProducesResponseType(typeof(CompleteChecklistResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteChecklistItem(Guid id, Guid itemId, [FromBody] CompleteChecklistItemDto dto, CancellationToken ct)
    {
        var result = await bidService.CompleteChecklistItemAsync(itemId, id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(result);
    }

    // ── Attachments ─────────────────────────────────────────────────────────────

    /// <summary>Request a presigned PUT URL to upload a bid attachment directly to R2.</summary>
    [HttpPost("{id:guid}/attachments/upload-url")]
    [ProducesResponseType(typeof(UploadUrlResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestAttachmentUploadUrl(Guid id, [FromBody] RequestUploadUrlDto dto, CancellationToken ct)
    {
        var result = await attachmentService.CreateUploadUrlAsync(CurrentOrgId, id, dto, ct);
        return Ok(result);
    }

    /// <summary>Register an uploaded R2 object as an attachment on the bid.</summary>
    [HttpPost("{id:guid}/attachments")]
    [ProducesResponseType(typeof(BidAttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterAttachment(Guid id, [FromBody] RegisterBidAttachmentDto dto, CancellationToken ct)
    {
        var result = await attachmentService.RegisterAsync(CurrentOrgId, id, CurrentUserId, dto, ct);
        return Ok(result);
    }

    /// <summary>Presigned GET URL to download a bid attachment.</summary>
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/download-url")]
    [ProducesResponseType(typeof(DocumentUrlResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentDownloadUrl(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var result = await attachmentService.CreateDownloadUrlAsync(CurrentOrgId, id, attachmentId, ct);
        return Ok(result);
    }
}
