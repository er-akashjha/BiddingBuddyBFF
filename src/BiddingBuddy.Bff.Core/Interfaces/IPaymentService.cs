using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Payments;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// The EMD register. Every row carries a server-computed <c>Verdict</c> and a courier
    /// roll-up, so the client never derives status and never needs a call per deposit.
    /// </summary>
    /// <param name="status">held|submitted|pending|refunded|forfeited, or null for all.</param>
    /// <param name="needsAction">
    /// True returns only deposits whose verdict is danger or warn — the working view. Applied
    /// after projection because the verdict is computed, not stored.
    /// </param>
    /// <param name="q">Free text over tender title, bid title and instrument number.</param>
    Task<PagedResult<EmdPaymentDto>> ListEmdAsync(
        Guid orgId, string? status, bool needsAction, string? q, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Org-wide EMD totals for the register header — computed over all rows, not a page.</summary>
    Task<EmdRegisterSummaryDto> GetEmdSummaryAsync(Guid orgId, CancellationToken ct = default);

    Task<EmdPaymentDto> GetEmdAsync(Guid emdId, Guid orgId, CancellationToken ct = default);

    /// <summary>The full record: deposit, the bid it is attached to, and every consignment.</summary>
    Task<EmdDetailDto> GetEmdDetailAsync(Guid emdId, Guid orgId, CancellationToken ct = default);
    Task<EmdPaymentDto> CreateEmdAsync(Guid orgId, CreateEmdDto dto, CancellationToken ct = default);
    Task<EmdPaymentDto> UpdateEmdAsync(Guid emdId, Guid orgId, UpdateEmdDto dto, CancellationToken ct = default);

    Task<PagedResult<InvoiceDto>> ListInvoicesAsync(Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId, Guid orgId, CancellationToken ct = default);
    Task<InvoiceDto> CreateInvoiceAsync(Guid orgId, CreateInvoiceDto dto, CancellationToken ct = default);
    Task<InvoiceDto> UpdateInvoiceAsync(Guid invoiceId, Guid orgId, UpdateInvoiceDto dto, CancellationToken ct = default);
}
