using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// The in-app buyer-access review queue — a platform operator works it with their own logged-in
/// identity, instead of the out-of-band <c>X-Api-Key</c> curl to
/// <see cref="InternalOrganizationsController"/>.
///
/// <para>Same service, same decision. Every action here delegates to the identical
/// <see cref="IBuyerRequestService"/> methods the <c>/internal</c> controller calls, so "approved in
/// the app" and "approved by curl" cannot drift into two different notions of what a buyer is — the
/// conversion still runs through <c>SetOrgTypeAsync</c> with the org's claimed identity. Only the
/// gate differs: a JWT plus <see cref="PlatformAdminAttribute"/> rather than the pipeline key.</para>
///
/// <para>Not org-scoped: the queue spans every organization, so these routes carry no <c>X-Org-Id</c>
/// and are exempt from <c>OrgContextMiddleware</c> (the <c>/api/admin</c> skip-prefix). The platform-admin
/// check is therefore the only authorization gate, which is exactly why it fails closed.</para>
/// </summary>
[ApiController]
[Route("api/admin/buyer-requests")]
[Authorize]
[PlatformAdmin]
[Produces("application/json")]
public class AdminBuyerRequestsController(IBuyerRequestService buyerRequests) : ControllerBase
{
    /// <summary>The review queue. <c>?status=</c> defaults to <c>pending</c>; pass approved /
    /// rejected / cancelled to see history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BuyerRequestQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => Ok(await buyerRequests.ListAsync(status, ct));

    /// <summary>
    /// Approve a request: convert the org to a buyer (reusing its claimed identity), close the
    /// request, notify the org. A request already decided returns 409.
    /// </summary>
    [HttpPost("{requestId:guid}/approve")]
    [ProducesResponseType(typeof(BuyerRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid requestId, [FromBody] ApproveBuyerRequestDto dto, CancellationToken ct)
        => await DecideAsync(() => buyerRequests.ApproveAsync(requestId, dto, ct));

    /// <summary>Reject a request with a mandatory reason and notify the org.</summary>
    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType(typeof(BuyerRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid requestId, [FromBody] RejectBuyerRequestDto dto, CancellationToken ct)
        => await DecideAsync(() => buyerRequests.RejectAsync(requestId, dto, ct));

    // Mirrors InternalOrganizationsController.DecideAsync: NOT_PENDING → 409, unknown id → 404, a
    // mandatory-reason violation surfaced by the service → 400. Kept in step deliberately, since the
    // two controllers are two doors onto the same decision and should answer identically.
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
}
