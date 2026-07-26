using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Operator-only organization administration. X-Api-Key, not a user JWT.
///
/// <para><b>Why becoming a buyer is provisioned rather than self-served.</b> A buyer organization can
/// publish a tender notice that appears on the public portal under a department's name. If any
/// signup could tick a box and claim to be "Directorate of Health Services, Kerala", this platform
/// becomes an impersonation surface — and the notices it carries are exactly the artefact a supplier
/// is meant to be able to trust. There is deliberately no client-facing route to <c>org_type</c>:
/// <c>CreateOrgDto</c> and <c>UpdateOrgDto</c> do not carry it, and this endpoint is the only way to
/// set it.</para>
///
/// <para>This matches how the first buyers are reached anyway — the plan's beachhead is ULBs,
/// universities and state PSUs, sold to rather than signed up (docs/gov-tendering/PLAN.md §3).</para>
/// </summary>
[ApiController]
[Route("internal/organizations")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalOrganizationsController(
    IOrganizationService orgs,
    IBuyerRequestService buyerRequests) : ControllerBase
{
    // ── Buyer-access request queue ───────────────────────────────────────────
    //
    // The inbound counterpart to the direct provisioning below. An org raises a request on the
    // client side; an operator reviews it here. Approval runs the same SetOrgTypeAsync conversion,
    // so "requested then approved" and "provisioned outright" cannot drift into two different ideas
    // of what a buyer is.

    /// <summary>The review queue. <c>?status=</c> defaults to <c>pending</c>; pass approved / rejected
    /// / cancelled to see history.</summary>
    [HttpGet("buyer-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<BuyerRequestQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListBuyerRequests([FromQuery] string? status, CancellationToken ct)
        => Ok(await buyerRequests.ListAsync(status, ct));

    /// <summary>
    /// Approve a request: convert the org to a buyer (reusing its claimed identity), close the
    /// request, notify the org. Idempotency is by state — a request already decided returns 409.
    /// </summary>
    [HttpPost("buyer-requests/{requestId:guid}/approve")]
    [ProducesResponseType(typeof(BuyerRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveBuyerRequest(
        Guid requestId, [FromBody] ApproveBuyerRequestDto dto, CancellationToken ct)
        => await DecideAsync(() => buyerRequests.ApproveAsync(requestId, dto, ct));

    /// <summary>Reject a request with a mandatory reason and notify the org.</summary>
    [HttpPost("buyer-requests/{requestId:guid}/reject")]
    [ProducesResponseType(typeof(BuyerRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectBuyerRequest(
        Guid requestId, [FromBody] RejectBuyerRequestDto dto, CancellationToken ct)
        => await DecideAsync(() => buyerRequests.RejectAsync(requestId, dto, ct));

    private async Task<IActionResult> DecideAsync(Func<Task<BuyerRequestDto?>> decide)
    {
        try
        {
            var result = await decide();
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message == "NOT_PENDING")
        {
            return Conflict(new { error = "This request has already been decided.", code = "NOT_PENDING" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Grants or revokes buyer status, and records the procuring-entity identity that a department's
    /// notices are published under.
    /// </summary>
    /// <remarks>
    /// Writes an <c>audit_events</c> row, so there is a durable answer to "who made this org a buyer,
    /// and when" — which is the first question anyone asks after a tender is published under a name
    /// that turns out to be wrong.
    ///
    /// The org's <b>owner</b> holds every buyer capability already, so flipping the type is enough to
    /// start authoring; assigning `po_admin` / `po_publisher` to separate the duties is a follow-up
    /// on the Team page.
    /// </remarks>
    [HttpPost("{orgId:guid}/org-type")]
    [ProducesResponseType(typeof(OrgTypeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetOrgType(
        Guid orgId, [FromBody] SetOrgTypeDto dto, CancellationToken ct)
    {
        try
        {
            var result = await orgs.SetOrgTypeAsync(orgId, dto, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, code = "INVALID_ORG_TYPE" });
        }
    }
}
