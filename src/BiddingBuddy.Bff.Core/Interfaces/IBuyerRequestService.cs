using BiddingBuddy.Bff.Core.DTOs.Orgs;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// The inbound path to buyer status: an org raises a request, an operator approves it, and approval
/// runs the same <see cref="IOrganizationService.SetOrgTypeAsync"/> conversion an operator would
/// run directly. The request is self-serve; the decision is not.
/// </summary>
public interface IBuyerRequestService
{
    // ── Org side (owner/admin, org-scoped) ───────────────────────────────────

    /// <summary>
    /// Raise (or return the existing pending) buyer request for the org.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Caller is not an owner/admin of the org.</exception>
    /// <exception cref="InvalidOperationException"><c>ALREADY_BUYER</c> when the org already publishes.</exception>
    Task<BuyerRequestDto> RequestAsync(Guid orgId, Guid userId, RequestBuyerAccessDto dto, CancellationToken ct = default);

    /// <summary>The org's current or most recent request, or null if it has never asked.</summary>
    Task<BuyerRequestDto?> GetCurrentAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>Withdraw the pending request. Owner/admin only. No-op-safe if nothing is pending.</summary>
    Task<bool> CancelAsync(Guid orgId, Guid userId, CancellationToken ct = default);

    // ── Operator side (X-Api-Key) ────────────────────────────────────────────

    /// <summary>The review queue, oldest first. Filter by status (default <c>pending</c>).</summary>
    Task<IReadOnlyList<BuyerRequestQueueItemDto>> ListAsync(string? status, CancellationToken ct = default);

    /// <summary>
    /// Approve: convert the org (reusing <see cref="IOrganizationService.SetOrgTypeAsync"/> with the
    /// claimed identity), mark the request approved, notify the org. Null if the request is unknown.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>NOT_PENDING</c> if it was already decided.</exception>
    Task<BuyerRequestDto?> ApproveAsync(Guid requestId, ApproveBuyerRequestDto dto, CancellationToken ct = default);

    /// <summary>Reject with a mandatory reason and notify the org. Null if the request is unknown.</summary>
    /// <exception cref="InvalidOperationException"><c>NOT_PENDING</c> if it was already decided.</exception>
    Task<BuyerRequestDto?> RejectAsync(Guid requestId, RejectBuyerRequestDto dto, CancellationToken ct = default);
}
