using BiddingBuddy.Bff.Core.DTOs.Bids;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// A bid's EMD: whether one is needed, the money/instrument record, and the courier legs that
/// carry a physical instrument to the buyer.
/// <para>
/// The EMD record itself lives in <c>emd_payments</c> — the same table the finance register
/// (<c>/api/payments/emd</c>) reads — so recording an EMD from the bid page and recording it
/// from Payments produce one row, not two views that drift apart.
/// </para>
/// </summary>
public interface IBidEmdService
{
    /// <summary>
    /// The bid's whole EMD picture in one call. Org-scoped; 404 if the bid isn't this org's.
    /// Returns a populated <see cref="BidEmdDto"/> with a null <c>Emd</c> when nothing has been
    /// recorded yet — the tab renders the empty state from that, not from a 404.
    /// </summary>
    Task<BidEmdDto> GetAsync(Guid orgId, Guid bidId, CancellationToken ct = default);

    /// <summary>
    /// Upsert the bid's EMD. Creates the <c>emd_payments</c> row on first save and updates it
    /// after; null fields are left alone. Setting the requirement to <c>exempt</c> or
    /// <c>not_required</c> deliberately does NOT create a money row.
    /// </summary>
    Task<BidEmdDto> SaveAsync(Guid orgId, Guid bidId, Guid userId, SaveBidEmdDto dto, CancellationToken ct = default);

    Task<BidDispatchDto> CreateDispatchAsync(Guid orgId, Guid bidId, Guid userId, CreateBidDispatchDto dto, CancellationToken ct = default);

    Task<BidDispatchDto> UpdateDispatchAsync(Guid orgId, Guid bidId, Guid dispatchId, Guid userId, UpdateBidDispatchDto dto, CancellationToken ct = default);

    Task DeleteDispatchAsync(Guid orgId, Guid bidId, Guid dispatchId, CancellationToken ct = default);
}
