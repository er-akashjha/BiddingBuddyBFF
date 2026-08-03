using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// The grant pursuit pipeline — applications and their plan checklist / activity feed. Membership-only
/// (any org member), like bids. The proposal-authoring sub-resources (narrative / budget / reviews /
/// submissions) are added with Phase 3.
/// </summary>
[ApiController]
[Route("api/grant-applications")]
[Authorize]
[Produces("application/json")]
public class GrantApplicationsController(
    IGrantApplicationService apps,
    IGrantApprovalFormService approvalForms) : BffControllerBase
{
    /// <summary>The org's applications. Filter by <c>stage</c>, <c>statusCategory</c> (open|closed),
    /// <c>assignedTo</c> ('me' or a user id), free-text <c>q</c>; sort with <c>sort</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GrantApplicationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? stage,
        [FromQuery] string? statusCategory,
        [FromQuery] string? assignedTo,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        CancellationToken ct = default)
    {
        Guid? assignee = null;
        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            if (assignedTo.Equals("me", StringComparison.OrdinalIgnoreCase)) assignee = CurrentUserId;
            else if (Guid.TryParse(assignedTo, out var g)) assignee = g;
            else throw new ArgumentException("assignedTo must be 'me' or a user id.", nameof(assignedTo));
        }
        var query = new GrantApplicationListQuery(stage, statusCategory, assignee, q, sort);
        return Ok(await apps.ListAsync(CurrentOrgId, query, ct));
    }

    /// <summary>Full application detail (fields + recent activity + plan checklist).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GrantApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await apps.GetAsync(id, CurrentOrgId, ct));

    /// <summary>Start an application — from a grant (idempotent per grant) or a manual entry.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GrantApplicationDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateGrantApplicationDto dto, CancellationToken ct)
    {
        var app = await apps.CreateAsync(CurrentOrgId, CurrentUserId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = app.Id }, app);
    }

    /// <summary>Update application fields.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(GrantApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGrantApplicationDto dto, CancellationToken ct)
        => Ok(await apps.UpdateAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    /// <summary>Advance or revert the stage; logged in the activity feed.</summary>
    [HttpPatch("{id:guid}/stage")]
    [ProducesResponseType(typeof(GrantApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStage(Guid id, [FromBody] ChangeGrantApplicationStageDto dto, CancellationToken ct)
        => Ok(await apps.ChangeStageAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    /// <summary>Delete an application and everything under it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await apps.DeleteAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>
    /// Download the pre-filled "Approval to Proceed" Word form for this application. Streamed as a
    /// .docx (not JSON), so the SPA fetches it with the auth + org headers and saves the returned blob.
    /// </summary>
    [HttpGet("{id:guid}/approval-form.docx")]
    [Produces("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApprovalForm(Guid id, CancellationToken ct)
    {
        var form = await approvalForms.BuildAsync(id, CurrentOrgId, ct);
        return File(form.Content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", form.FileName);
    }

    // ── Activities ────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/activities")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantApplicationActivityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActivities(Guid id, CancellationToken ct)
        => Ok(await apps.GetActivitiesAsync(id, CurrentOrgId, ct));

    [HttpPost("{id:guid}/activities")]
    [ProducesResponseType(typeof(GrantApplicationActivityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddGrantApplicationNoteDto dto, CancellationToken ct)
        => Ok(await apps.AddNoteAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    // ── Plan checklist ──────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/checklist")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantChecklistItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChecklist(Guid id, CancellationToken ct)
        => Ok(await apps.GetChecklistAsync(id, CurrentOrgId, ct));

    [HttpPost("{id:guid}/checklist")]
    [ProducesResponseType(typeof(GrantChecklistItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddChecklistItem(Guid id, [FromBody] CreateGrantChecklistItemDto dto, CancellationToken ct)
        => Ok(await apps.CreateChecklistItemAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpPatch("{id:guid}/checklist/{itemId:guid}")]
    [ProducesResponseType(typeof(GrantChecklistItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChecklistItem(Guid id, Guid itemId, [FromBody] UpdateGrantChecklistItemDto dto, CancellationToken ct)
        => Ok(await apps.UpdateChecklistItemAsync(itemId, id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpDelete("{id:guid}/checklist/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChecklistItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await apps.DeleteChecklistItemAsync(itemId, id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Narrative ─────────────────────────────────────────────────────────────

    /// <summary>The proposal narrative sections (seeded on first read).</summary>
    [HttpGet("{id:guid}/narrative")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantNarrativeSectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNarrative(Guid id, CancellationToken ct)
        => Ok(await apps.GetNarrativeAsync(id, CurrentOrgId, ct));

    [HttpPatch("{id:guid}/narrative/{sectionId:guid}")]
    [ProducesResponseType(typeof(GrantNarrativeSectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateNarrative(Guid id, Guid sectionId, [FromBody] UpdateNarrativeSectionDto dto, CancellationToken ct)
        => Ok(await apps.UpdateNarrativeSectionAsync(sectionId, id, CurrentOrgId, CurrentUserId, dto, ct));

    // ── Budget ────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/budget")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantBudgetLineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBudget(Guid id, CancellationToken ct)
        => Ok(await apps.GetBudgetAsync(id, CurrentOrgId, ct));

    [HttpPost("{id:guid}/budget")]
    [ProducesResponseType(typeof(GrantBudgetLineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddBudgetLine(Guid id, [FromBody] CreateBudgetLineDto dto, CancellationToken ct)
        => Ok(await apps.AddBudgetLineAsync(id, CurrentOrgId, dto, ct));

    [HttpPatch("{id:guid}/budget/{lineId:guid}")]
    [ProducesResponseType(typeof(GrantBudgetLineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBudgetLine(Guid id, Guid lineId, [FromBody] UpdateBudgetLineDto dto, CancellationToken ct)
        => Ok(await apps.UpdateBudgetLineAsync(lineId, id, CurrentOrgId, dto, ct));

    [HttpDelete("{id:guid}/budget/{lineId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBudgetLine(Guid id, Guid lineId, CancellationToken ct)
    {
        await apps.DeleteBudgetLineAsync(lineId, id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Reviews ───────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/reviews")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(Guid id, CancellationToken ct)
        => Ok(await apps.GetReviewsAsync(id, CurrentOrgId, ct));

    [HttpPost("{id:guid}/reviews")]
    [ProducesResponseType(typeof(GrantReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddReview(Guid id, [FromBody] CreateReviewDto dto, CancellationToken ct)
        => Ok(await apps.AddReviewAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpPatch("{id:guid}/reviews/{reviewId:guid}")]
    [ProducesResponseType(typeof(GrantReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateReview(Guid id, Guid reviewId, [FromBody] UpdateReviewDto dto, CancellationToken ct)
        => Ok(await apps.UpdateReviewAsync(reviewId, id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpDelete("{id:guid}/reviews/{reviewId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReview(Guid id, Guid reviewId, CancellationToken ct)
    {
        await apps.DeleteReviewAsync(reviewId, id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Submissions ───────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/submissions")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmissions(Guid id, CancellationToken ct)
        => Ok(await apps.GetSubmissionsAsync(id, CurrentOrgId, ct));

    [HttpPost("{id:guid}/submissions")]
    [ProducesResponseType(typeof(GrantSubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSubmission(Guid id, [FromBody] CreateSubmissionDto dto, CancellationToken ct)
        => Ok(await apps.AddSubmissionAsync(id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpPatch("{id:guid}/submissions/{submissionId:guid}")]
    [ProducesResponseType(typeof(GrantSubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSubmission(Guid id, Guid submissionId, [FromBody] UpdateSubmissionDto dto, CancellationToken ct)
        => Ok(await apps.UpdateSubmissionAsync(submissionId, id, CurrentOrgId, CurrentUserId, dto, ct));

    [HttpDelete("{id:guid}/submissions/{submissionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSubmission(Guid id, Guid submissionId, CancellationToken ct)
    {
        await apps.DeleteSubmissionAsync(submissionId, id, CurrentOrgId, ct);
        return NoContent();
    }
}
