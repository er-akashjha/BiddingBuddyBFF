namespace BiddingBuddy.Bff.Core.Constants;

/// <summary>
/// How an EMD is furnished. Single source of truth on the backend; must stay in lockstep with
/// the <c>emd_payments_payment_mode_check</c> constraint (migration 0029) and the frontend
/// <c>emd</c> module.
/// <para>
/// The distinction that matters is <see cref="RequiresPhysicalDispatch"/>: an online transfer
/// settles the moment it clears, but an instrument is a piece of paper whose ORIGINAL has to
/// be in the buyer's hands before technical-bid opening. That single predicate is what decides
/// whether a courier is expected, whether the EMD tab shows a dispatch section, and whether
/// EMD_DISPATCH_DUE can fire.
/// </para>
/// </summary>
public static class EmdModes
{
    /// <summary>Money moves electronically. Nothing physical to send.</summary>
    public static readonly IReadOnlyList<string> Electronic =
        ["neft", "rtgs", "upi", "online"];

    /// <summary>
    /// The original paper must physically reach the buyer. dd = demand draft,
    /// bg = bank guarantee, fdr = fixed deposit receipt.
    /// </summary>
    public static readonly IReadOnlyList<string> Instrument =
        ["dd", "bg", "fdr", "bankers_cheque", "surety_bond"];

    /// <summary>MSME / NSIC / Startup exemption — no money, no paper.</summary>
    public const string Exempt = "exempt";

    /// <summary>
    /// Instrument modes that carry an expiry worth warning about. A demand draft technically
    /// has a validity too, but only BG/FDR routinely need EXTENDING mid-bid, which is the
    /// action EMD_BG_EXPIRING exists to prompt.
    /// </summary>
    public static readonly IReadOnlyList<string> Expiring = ["bg", "fdr", "surety_bond"];

    public static bool RequiresPhysicalDispatch(string? mode)
        => mode is not null && Instrument.Contains(mode);

    public static bool CanExpire(string? mode)
        => mode is not null && Expiring.Contains(mode);

    /// <summary>Human label for notification copy — "Bank Guarantee", not "bg".</summary>
    public static string Label(string? mode) => mode switch
    {
        "dd"             => "demand draft",
        "bg"             => "bank guarantee",
        "fdr"            => "fixed deposit receipt",
        "bankers_cheque" => "banker's cheque",
        "surety_bond"    => "insurance surety bond",
        "neft" or "rtgs" or "upi" or "online" => "EMD payment",
        Exempt           => "EMD exemption",
        _                => "EMD instrument",
    };
}

/// <summary>
/// Whether a bid needs an EMD at all. Only <see cref="Required"/> makes a missing instrument
/// or an un-dispatched courier worth alerting about — which is why the default is
/// <see cref="Unknown"/> rather than assuming either way.
/// </summary>
public static class EmdRequirements
{
    public const string Unknown = "unknown";
    public const string Required = "required";
    public const string Exempt = "exempt";
    public const string NotRequired = "not_required";

    public static readonly IReadOnlyList<string> All = [Unknown, Required, Exempt, NotRequired];

    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

/// <summary>
/// Lifecycle of a physical consignment. <c>draft</c> is the row created when someone starts
/// filling in courier details but hasn't handed the packet over yet — it is deliberately NOT
/// treated as dispatched by the deadline scan.
/// </summary>
public static class DispatchStatuses
{
    public const string Draft = "draft";
    public const string Dispatched = "dispatched";
    public const string InTransit = "in_transit";
    public const string Delivered = "delivered";
    public const string Returned = "returned";
    public const string Lost = "lost";

    public static readonly IReadOnlyList<string> All =
        [Draft, Dispatched, InTransit, Delivered, Returned, Lost];

    /// <summary>Sent but not yet accounted for — the states the "courier is late" scan watches.</summary>
    public static readonly IReadOnlyList<string> InFlight = [Dispatched, InTransit];

    /// <summary>
    /// A dispatch that no longer protects the bid. <c>returned</c> and <c>lost</c> count here:
    /// the packet exists as a record but the buyer never got it, so the EMD is still
    /// effectively un-sent and must be re-dispatched.
    /// </summary>
    public static readonly IReadOnlyList<string> Failed = [Returned, Lost];

    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}
