namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A physical consignment sent for a bid — in practice the courier that carries an EMD
/// instrument's original paper (DD / Bank Guarantee / FDR) to the buyer's office before
/// technical-bid opening. Nothing else in the system tracked this, and a physical original
/// that arrives late is treated as no EMD at all.
/// <para>
/// Its own table rather than courier columns on <see cref="EmdPayment"/> because one EMD can
/// have several dispatches: a consignment returned undelivered has to be re-sent, and the
/// refund instrument travels back <c>inbound</c>. One column-set cannot hold both legs.
/// </para>
/// <para>
/// <see cref="Purpose"/> is deliberately wider than EMD (hard-copy bid submission, samples)
/// so the same table serves those without another migration — but v1 only exposes
/// <c>emd_instrument</c> in the UI.
/// </para>
/// </summary>
public class BidDispatch
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid BidId { get; set; }

    /// <summary>Null for a non-EMD dispatch, and nulled (not cascaded) if the EMD record is deleted.</summary>
    public Guid? EmdPaymentId { get; set; }

    /// <summary>emd_instrument | hard_copy_bid | sample | other</summary>
    public string Purpose { get; set; } = "emd_instrument";

    /// <summary>outbound | inbound — inbound is the refund instrument coming back.</summary>
    public string Direction { get; set; } = "outbound";

    public string? CourierName { get; set; }

    /// <summary>AWB / consignment number.</summary>
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }

    public DateOnly? DispatchedOn { get; set; }
    public Guid? DispatchedBy { get; set; }

    public string? RecipientName { get; set; }
    public string? RecipientDesignation { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientPhone { get; set; }

    /// <summary>
    /// The hard cut-off from the tender (usually technical-bid opening). This — not
    /// <see cref="ExpectedDeliveryOn"/>, which is only the courier's own promise — is the
    /// date that actually matters and the one EMD_DISPATCH_DUE keys off.
    /// </summary>
    public DateOnly? DeliverBy { get; set; }

    public DateOnly? ExpectedDeliveryOn { get; set; }
    public DateOnly? DeliveredOn { get; set; }
    public string? ReceivedBy { get; set; }

    /// <summary>draft | dispatched | in_transit | delivered | returned | lost</summary>
    public string Status { get; set; } = "draft";

    /// <summary>Proof-of-delivery scan, linked from the org vault.</summary>
    public Guid? PodDocumentId { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Bid Bid { get; set; } = default!;
    public EmdPayment? EmdPayment { get; set; }
    public User? Dispatcher { get; set; }
    public Document? PodDocument { get; set; }
}
