using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Authorization;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Buyer-side tendering: a government department authors and publishes a tender notice.
///
/// <para>Every route on this controller is capability-gated by
/// <see cref="RequireOrgCapabilityAttribute"/> — the first authorization enforcement point in this
/// API. Membership alone is not enough here, because the separation of duties between the officer
/// who drafts a tender and the officer who publishes it is the control that makes the department's
/// file auditable.</para>
///
/// <para>Phase 1 (e-publishing) only: no bid is ever received through this system, which is what
/// keeps the whole surface outside STQC certification. See docs/gov-tendering/PLAN.md §3.</para>
/// </summary>
[ApiController]
[Route("api/buyer/tenders")]
[Authorize]
[RequireOrgCapability(OrgCapabilities.TenderRead)]
public class BuyerTendersController(IBuyerTenderService service) : BffControllerBase
{
    /// <summary>The role the caller acted under, stashed by the capability filter. Recorded in the
    /// audit trail so it reflects the role in force at the time rather than today's.</summary>
    private string ActorRole => HttpContext.Items["OrgRole"] as string ?? "unknown";

    /// <summary>Canonical dropdown values for the authoring form.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<BuyerTenderOptionsDto>> GetOptions(CancellationToken ct)
        => Ok(await service.GetOptionsAsync(ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TenderDraftListItemDto>>> List(
        [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
        => Ok(await service.ListAsync(CurrentOrgId, status, search, ct));

    [HttpGet("{draftId:guid}")]
    public async Task<ActionResult<TenderDraftDetailDto>> Get(Guid draftId, CancellationToken ct)
    {
        var draft = await service.GetAsync(CurrentOrgId, draftId, ct);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpPost]
    [RequireOrgCapability(OrgCapabilities.TenderAuthor)]
    public async Task<ActionResult<TenderDraftDetailDto>> Create(
        [FromBody] SaveTenderDraftDto dto, CancellationToken ct)
    {
        var created = await service.CreateAsync(CurrentOrgId, CurrentUserId, ActorRole, dto, ct);
        return CreatedAtAction(nameof(Get), new { draftId = created.Id }, created);
    }

    [HttpPatch("{draftId:guid}")]
    [RequireOrgCapability(OrgCapabilities.TenderAuthor)]
    public async Task<ActionResult<TenderDraftDetailDto>> Update(
        Guid draftId, [FromBody] SaveTenderDraftDto dto, CancellationToken ct)
    {
        var updated = await service.UpdateAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, dto, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{draftId:guid}")]
    [RequireOrgCapability(OrgCapabilities.TenderAuthor)]
    public async Task<IActionResult> Delete(Guid draftId, CancellationToken ct)
        => await service.DeleteDraftAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, ct)
            ? NoContent()
            : NotFound();

    /// <summary>Runs the compliance engine without changing anything.</summary>
    [HttpGet("{draftId:guid}/validate")]
    public async Task<ActionResult<ValidationResultDto>> Validate(Guid draftId, CancellationToken ct)
    {
        var result = await service.ValidateAsync(CurrentOrgId, draftId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Publishes the tender. Requires <c>tender.publish</c>, which <c>po_admin</c> does NOT hold —
    /// the officer who drafts is not, by default, the officer who publishes.
    /// </summary>
    [HttpPost("{draftId:guid}/publish")]
    [RequireOrgCapability(OrgCapabilities.TenderPublish)]
    public async Task<ActionResult<TenderDraftDetailDto>> Publish(
        Guid draftId, [FromBody] PublishTenderDto dto, CancellationToken ct)
        => await Guarded(() => service.PublishAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, dto, ct));

    [HttpPost("{draftId:guid}/corrigenda")]
    [RequireOrgCapability(OrgCapabilities.TenderPublish)]
    public async Task<ActionResult<TenderDraftDetailDto>> IssueCorrigendum(
        Guid draftId, [FromBody] IssueCorrigendumDto dto, CancellationToken ct)
        => await Guarded(() => service.IssueCorrigendumAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, dto, ct));

    [HttpPost("{draftId:guid}/award")]
    [RequireOrgCapability(OrgCapabilities.TenderPublish)]
    public async Task<ActionResult<TenderDraftDetailDto>> Award(
        Guid draftId, [FromBody] AwardTenderDto dto, CancellationToken ct)
        => await Guarded(() => service.AwardAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, dto, ct));

    [HttpPost("{draftId:guid}/cancel")]
    [RequireOrgCapability(OrgCapabilities.TenderPublish)]
    public async Task<ActionResult<TenderDraftDetailDto>> Cancel(
        Guid draftId, [FromBody] CancelTenderRequest dto, CancellationToken ct)
        => await Guarded(() => service.CancelAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, dto.Reason, ct));

    [HttpPut("{draftId:guid}/committee")]
    [RequireOrgCapability(OrgCapabilities.CommitteeManage)]
    public async Task<ActionResult<IReadOnlyList<CommitteeMemberDto>>> SetCommittee(
        Guid draftId, [FromBody] SaveCommitteeMemberDto[] members, CancellationToken ct)
        => Ok(await service.SetCommitteeAsync(CurrentOrgId, CurrentUserId, ActorRole, draftId, members, ct));

    /// <summary>
    /// The downloadable audit file for CAG or vigilance inspection: the tender, every version,
    /// every corrigendum, every recorded action, and a replay of the hash chain.
    /// </summary>
    /// <remarks>
    /// Readable by <c>auditor</c>, which holds <c>tender.read</c> and nothing else — an auditor sees
    /// everything and can change nothing, which is the whole shape of the role.
    /// </remarks>
    [HttpGet("{draftId:guid}/audit-file")]
    public async Task<ActionResult<TenderAuditFileDto>> GetAuditFile(Guid draftId, CancellationToken ct)
    {
        var file = await service.GetAuditFileAsync(CurrentOrgId, draftId, ct);
        return file is null ? NotFound() : Ok(file);
    }

    /// <summary>
    /// Runs an operation, mapping a compliance refusal to 422 with the findings intact.
    /// </summary>
    /// <remarks>
    /// 422 rather than 400: the request was well-formed and understood, the tender is simply not yet
    /// publishable. Handled here rather than in <c>GlobalExceptionHandler</c> because that renders
    /// bare ProblemDetails and would drop the findings — which are the entire response the authoring
    /// form needs, since each one carries the citation of the rule behind it.
    /// </remarks>
    private async Task<ActionResult<TenderDraftDetailDto>> Guarded(
        Func<Task<TenderDraftDetailDto?>> operation)
    {
        try
        {
            var result = await operation();
            return result is null ? NotFound() : Ok(result);
        }
        catch (ValidationFailedException ex)
        {
            return UnprocessableEntity(new
            {
                error = ex.Message,
                code = "COMPLIANCE_FAILED",
                validation = ex.Result,
            });
        }
    }
}

/// <summary>Body for the cancel endpoint. A reason is mandatory and is recorded in the audit file.</summary>
public record CancelTenderRequest(string Reason);
