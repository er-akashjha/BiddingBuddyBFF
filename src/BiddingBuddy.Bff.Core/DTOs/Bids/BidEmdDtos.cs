using BiddingBuddy.Bff.Core.DTOs.Payments;

namespace BiddingBuddy.Bff.Core.DTOs.Bids;

/// <summary>
/// Everything the bid's EMD tab needs in one call: whether EMD is needed at all, the money
/// record if one has been made, and the courier legs that carry a physical instrument.
/// </summary>
/// <param name="Requirement">unknown|required|exempt|not_required — the user's answer, not the tender's.</param>
/// <param name="TenderEmdAmount">
/// What the linked tender asks for, if known. Kept separate from the recorded EMD amount so
/// the UI can show "tender wants ₹90,000, you recorded ₹50,000" instead of quietly agreeing.
/// </param>
/// <param name="Emd">Null until someone records one — which is exactly the state EMD_DISPATCH_DUE warns about.</param>
/// <param name="Verdict">
/// The same server-computed answer the EMD register shows. Rendered, never re-derived — the
/// register, this tab and the reminder emails all read this one value.
/// </param>
public record BidEmdDto(
    string Requirement,
    string? ExemptionBasis,
    decimal? TenderEmdAmount,
    EmdPaymentDto? Emd,
    IReadOnlyList<BidDispatchDto> Dispatches,
    EmdVerdictDto Verdict
);

/// <summary>
/// Upsert the bid's EMD in one call. Every field is optional: the tab saves the requirement
/// on its own (before any money exists), and later saves instrument details on top.
/// <para>
/// <see cref="Requirement"/> of <c>exempt</c> or <c>not_required</c> means no EMD record is
/// created — the remaining fields are ignored rather than writing a zero-amount row.
/// </para>
/// </summary>
public record SaveBidEmdDto(
    string? Requirement = null,
    string? ExemptionBasis = null,
    decimal? Amount = null,
    DateOnly? PaymentDate = null,
    string? PaymentMode = null,
    string? TransactionRef = null,
    string? BankName = null,
    string? Status = null,
    string? InstrumentNumber = null,
    DateOnly? InstrumentDate = null,
    DateOnly? ValidUntil = null,
    string? IssuingBranch = null,
    string? Favouring = null,
    DateOnly? DueDate = null,
    Guid? DocumentId = null,
    DateOnly? RefundDate = null,
    decimal? RefundAmount = null,
    string? RefundRef = null,
    string? Notes = null
);

/// <summary>
/// One physical consignment for the bid. <paramref name="IsOverdue"/> and
/// <paramref name="DaysToDeliverBy"/> are computed server-side against the scan date so the
/// list, the badge and the email all agree on what "late" means.
/// </summary>
public record BidDispatchDto(
    Guid Id,
    Guid BidId,
    Guid? EmdPaymentId,
    string Purpose,
    string Direction,
    string? CourierName,
    string? TrackingNumber,
    string? TrackingUrl,
    DateOnly? DispatchedOn,
    Guid? DispatchedBy,
    string? DispatchedByName,
    string? RecipientName,
    string? RecipientDesignation,
    string? RecipientAddress,
    string? RecipientPhone,
    DateOnly? DeliverBy,
    DateOnly? ExpectedDeliveryOn,
    DateOnly? DeliveredOn,
    string? ReceivedBy,
    string Status,
    Guid? PodDocumentId,
    string? PodDocumentName,
    string? Notes,
    bool IsOverdue,
    int? DaysToDeliverBy,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Create a dispatch. Defaults match the common case: an outbound EMD instrument.</summary>
public record CreateBidDispatchDto(
    string Purpose = "emd_instrument",
    string Direction = "outbound",
    Guid? EmdPaymentId = null,
    string? CourierName = null,
    string? TrackingNumber = null,
    string? TrackingUrl = null,
    DateOnly? DispatchedOn = null,
    string? RecipientName = null,
    string? RecipientDesignation = null,
    string? RecipientAddress = null,
    string? RecipientPhone = null,
    DateOnly? DeliverBy = null,
    DateOnly? ExpectedDeliveryOn = null,
    string? Status = null,
    string? Notes = null
);

/// <summary>
/// Patch a dispatch. Null means "leave alone" — the tab PATCHes single fields as the
/// consignment moves (marking delivered, attaching the POD) rather than resubmitting the form.
/// </summary>
public record UpdateBidDispatchDto(
    string? CourierName = null,
    string? TrackingNumber = null,
    string? TrackingUrl = null,
    DateOnly? DispatchedOn = null,
    string? RecipientName = null,
    string? RecipientDesignation = null,
    string? RecipientAddress = null,
    string? RecipientPhone = null,
    DateOnly? DeliverBy = null,
    DateOnly? ExpectedDeliveryOn = null,
    DateOnly? DeliveredOn = null,
    string? ReceivedBy = null,
    string? Status = null,
    Guid? PodDocumentId = null,
    string? Notes = null
);
