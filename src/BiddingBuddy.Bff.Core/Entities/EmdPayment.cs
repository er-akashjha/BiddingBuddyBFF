namespace BiddingBuddy.Bff.Core.Entities;

public class EmdPayment
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? BidId { get; set; }
    public Guid? TenderId { get; set; }
    public string? GemTenderId { get; set; }
    public string? TenderTitle { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    /// <summary>
    /// neft|rtgs|upi|online — money moves electronically, nothing to courier.
    /// dd|bg|fdr|bankers_cheque|surety_bond — an instrument whose ORIGINAL PAPER has to
    /// physically reach the buyer (see <see cref="BidDispatch"/>). exempt — MSME/NSIC/Startup.
    /// </summary>
    public string? PaymentMode { get; set; }
    public string? TransactionRef { get; set; }
    public string? BankName { get; set; }
    public string Status { get; set; } = "held";  // pending|submitted|held|refunded|forfeited
    public DateOnly? RefundDate { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundRef { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Instrument details (migration 0029) ──────────────────────────────────
    // Only meaningful when PaymentMode is an instrument mode. Null for online/exempt EMD.

    /// <summary>DD / BG / FDR number as printed on the instrument.</summary>
    public string? InstrumentNumber { get; set; }
    public DateOnly? InstrumentDate { get; set; }

    /// <summary>
    /// BG/FDR expiry. Watched by EMD_BG_EXPIRING: once it passes, the buyer can no longer
    /// invoke the guarantee, which most tender conditions treat as a withdrawn EMD.
    /// </summary>
    public DateOnly? ValidUntil { get; set; }

    public string? IssuingBranch { get; set; }

    /// <summary>Payee the instrument is drawn in favour of, e.g. "Pay &amp; Accounts Officer, …".</summary>
    public string? Favouring { get; set; }

    /// <summary>When the EMD must be in place — normally the bid submission deadline.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// Scan of the instrument. A LINK to an org vault <see cref="Document"/>, never a copy —
    /// same reasoning as <see cref="BidDocument"/>.
    /// </summary>
    public Guid? DocumentId { get; set; }

    public Organization Organization { get; set; } = default!;
    public Bid? Bid { get; set; }
    public Tender? Tender { get; set; }
    public Document? Document { get; set; }
    public ICollection<BidDispatch> Dispatches { get; set; } = [];
}
