using BiddingBuddy.Bff.Core.DTOs.Bids;

namespace BiddingBuddy.Bff.Core.DTOs.Payments;

/// <summary>
/// The server's answer to "is this EMD OK?" — see <see cref="Helpers.EmdVerdict"/>. Clients
/// render it, they never re-derive it.
/// </summary>
/// <param name="Level">danger | warn | info | none — drives colour and sort order.</param>
/// <param name="Code">Stable machine value (courier_late, not_couriered, refund_overdue, …).</param>
/// <param name="Chip">Short label for a badge.</param>
/// <param name="Message">One sentence, or null when there is nothing to say.</param>
public record EmdVerdictDto(string Level, string Code, string Chip, string? Message);

/// <summary>
/// Courier state rolled up for a list row, so the register can show "Courier late" without
/// N+1 calls for every deposit's consignments.
/// </summary>
public record EmdDispatchSummaryDto(
    int Total,
    int Live,
    string? LatestStatus,
    string? CourierName,
    string? TrackingNumber,
    DateOnly? DeliverBy,
    DateOnly? DeliveredOn,
    bool AnyOverdue);

/// <summary>
/// Bid context for the register — enough to identify and open the bid without loading it.
/// Null when the EMD was recorded standalone (the table allows a null bid_id).
/// </summary>
public record EmdBidRefDto(
    Guid BidId,
    string Title,
    string Stage,
    string StatusCategory,
    DateOnly? DueDate,
    string? AssignedToName);

/// <summary>Full record behind a register row: the deposit, its bid, and every consignment.</summary>
public record EmdDetailDto(
    EmdPaymentDto Emd,
    EmdBidRefDto? Bid,
    IReadOnlyList<BidDispatchDto> Dispatches);

/// <summary>
/// Org-wide totals for the register's header. Computed over ALL deposits, not the current
/// page — a "blocked" figure that only counted page one would be worse than none.
/// </summary>
public record EmdRegisterSummaryDto(
    decimal BlockedAmount,
    int BlockedCount,
    int NeedsActionCount,
    int CourierLateCount,
    int NotCouriedCount,
    decimal RefundOverdueAmount,
    int RefundOverdueCount,
    decimal RefundedAmount,
    decimal ForfeitedAmount);

/// <summary>
/// One EMD record. The instrument block (<paramref name="InstrumentNumber"/> onward, added by
/// migration 0029) is only populated for physical instruments — DD / BG / FDR / banker's
/// cheque / surety bond. For an online EMD or an exemption those fields stay null.
/// <para>
/// <paramref name="RequiresPhysicalDispatch"/> is derived server-side from
/// <paramref name="PaymentMode"/> so clients don't each hard-code the list of instrument
/// modes; it's what tells the UI whether a courier is expected at all.
/// </para>
/// </summary>
public record EmdPaymentDto(
    Guid Id,
    Guid? BidId,
    string? BidTitle,
    Guid? TenderId,
    string? GemTenderId,
    string? TenderTitle,
    decimal Amount,
    DateOnly PaymentDate,
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string Status,
    DateOnly? RefundDate,
    decimal? RefundAmount,
    string? RefundRef,
    string? Notes,
    DateTime UpdatedAt,
    // ── Instrument details (0029) — appended, so existing clients are unaffected ──
    string? InstrumentNumber = null,
    DateOnly? InstrumentDate = null,
    DateOnly? ValidUntil = null,
    string? IssuingBranch = null,
    string? Favouring = null,
    DateOnly? DueDate = null,
    Guid? DocumentId = null,
    string? DocumentName = null,
    bool RequiresPhysicalDispatch = false,
    // ── Register context (v34) — populated by the EMD list/detail reads ──
    string? EmdRequirement = null,
    EmdDispatchSummaryDto? Dispatch = null,
    EmdVerdictDto? Verdict = null
);

public record CreateEmdDto(
    Guid? BidId,
    Guid? TenderId,
    string? GemTenderId,
    string? TenderTitle,
    decimal Amount,
    DateOnly PaymentDate,
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string? Notes
);

public record UpdateEmdDto(
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string? Status,
    DateOnly? RefundDate,
    decimal? RefundAmount,
    string? RefundRef,
    string? Notes
);

public record InvoiceDto(
    Guid Id,
    Guid? OrderId,
    string? OrderNumber,
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal Amount,
    decimal? GstAmount,
    decimal? TotalAmount,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    DateOnly? PaidDate,
    decimal? PaidAmount,
    string Status,
    string? PaymentRef,
    string? Notes,
    DateTime UpdatedAt
);

public record CreateInvoiceDto(
    Guid? OrderId,
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal Amount,
    decimal? GstAmount,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    string? Notes
);

public record UpdateInvoiceDto(
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal? Amount,
    decimal? GstAmount,
    decimal? TotalAmount,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    DateOnly? PaidDate,
    decimal? PaidAmount,
    string? Status,
    string? PaymentRef,
    string? Notes
);
