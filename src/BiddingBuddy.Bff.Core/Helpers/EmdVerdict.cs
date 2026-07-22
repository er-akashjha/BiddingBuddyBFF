using BiddingBuddy.Bff.Core.Constants;

namespace BiddingBuddy.Bff.Core.Helpers;

/// <summary>
/// The single answer to "is this EMD OK?".
/// <para>
/// It lives on the server, and every surface reads it rather than deriving its own: the EMD
/// register, the bid's EMD tab, the reminder emails, and anything mobile adds later. Three
/// implementations of this question would drift, and the one that drifts is always the one
/// telling somebody their money is fine.
/// </para>
/// </summary>
public static class EmdVerdict
{
    // Levels, worst first. 'danger' means the bid or the money is actually at risk today.
    public const string LevelDanger = "danger";
    public const string LevelWarn = "warn";
    public const string LevelInfo = "info";
    public const string LevelNone = "none";

    /// <summary>An EMD held at least this long is working capital nobody is chasing.</summary>
    public const int DefaultRefundOverdueDays = 90;

    /// <summary>Warn this far ahead of a bank guarantee lapsing.</summary>
    public const int DefaultExpiryLeadDays = 21;

    /// <summary>
    /// Everything the verdict depends on, flattened so both the bid-scoped service and the
    /// register's projection can build it without loading the same graph twice.
    /// </summary>
    /// <param name="HasEmdRecord">False means nothing has been recorded at all — not that the amount is zero.</param>
    /// <param name="LiveDispatchCount">
    /// Outbound EMD consignments that are NOT returned/lost. A returned packet is not a sent
    /// packet — the buyer never got it, so the EMD is still effectively un-dispatched.
    /// </param>
    public readonly record struct Input(
        string Requirement,
        bool BidIsOpen,
        decimal? TenderEmdAmount,
        string? ExemptionBasis,
        bool HasEmdRecord,
        string? PaymentMode,
        string? Status,
        DateOnly? ValidUntil,
        DateOnly? PaymentDate,
        int LiveDispatchCount,
        bool AnyDispatchOverdue,
        bool AnyDispatchDelivered);

    public readonly record struct Result(string Level, string Code, string Chip, string? Message);

    public static Result For(
        Input i,
        DateOnly today,
        int refundOverdueDays = DefaultRefundOverdueDays,
        int expiryLeadDays = DefaultExpiryLeadDays)
    {
        if (i.Requirement == EmdRequirements.NotRequired)
            return new(LevelNone, "not_required", "No EMD", null);

        if (i.Requirement == EmdRequirements.Exempt)
            return string.IsNullOrWhiteSpace(i.ExemptionBasis)
                ? new(LevelInfo, "exemption_unrecorded", "EMD exempt",
                    "Record which exemption you are claiming — buyers usually ask for the certificate.")
                : new(LevelNone, "exempt", "EMD exempt", null);

        if (i.Requirement == EmdRequirements.Unknown)
            return i.TenderEmdAmount is > 0
                ? new(LevelWarn, "undecided", "EMD?",
                    $"This tender lists an EMD of ₹{i.TenderEmdAmount:N0}. Confirm whether you need to furnish it.")
                : new(LevelInfo, "undecided", "EMD?", "Nobody has said whether this tender needs an EMD.");

        // ── Required from here ────────────────────────────────────────────────

        if (!i.HasEmdRecord)
            return new(i.BidIsOpen ? LevelDanger : LevelWarn, "emd_missing", "EMD missing",
                "EMD is required but nothing has been recorded.");

        // Money already returned or lost: the instrument is spent and its dates stop mattering.
        // Checked before the courier rules so a refunded EMD never reads as "courier late".
        if (i.Status == "refunded") return new(LevelNone, "refunded", "Refunded", null);
        if (i.Status == "forfeited") return new(LevelWarn, "forfeited", "Forfeited", null);

        if (i.AnyDispatchOverdue)
            return new(LevelDanger, "courier_late", "Courier late",
                "A consignment has passed its delivery cut-off without being confirmed delivered.");

        var needsPaper = EmdModes.RequiresPhysicalDispatch(i.PaymentMode);

        if (needsPaper && i.LiveDispatchCount == 0)
            return new(i.BidIsOpen ? LevelDanger : LevelWarn, "not_couriered", "Not couriered",
                "The original instrument has to physically reach the buyer. No consignment has been recorded.");

        if (i.ValidUntil is { } expiry)
        {
            var days = expiry.DayNumber - today.DayNumber;
            if (days < 0)
                return new(LevelDanger, "instrument_expired", "Instrument expired",
                    $"The instrument expired on {expiry:dd MMM yyyy}.");
            if (days <= expiryLeadDays)
                return new(LevelWarn, "instrument_expiring", $"Expires in {days}d",
                    $"The instrument expires on {expiry:dd MMM yyyy}. Get it extended if the bid is still live.");
        }

        if (needsPaper && !i.AnyDispatchDelivered)
            return new(LevelWarn, "in_transit", "In transit", null);

        // Working capital nobody is chasing. This is the register's reason to exist: by the time
        // an EMD has been held three months the bid is long closed, so the bid page — where the
        // rest of these verdicts surface — is a page nobody opens again.
        if (i.Status == "held" && i.PaymentDate is { } paid)
        {
            var heldDays = today.DayNumber - paid.DayNumber;
            if (heldDays >= refundOverdueDays)
                return new(LevelWarn, "refund_overdue", "Chase refund",
                    $"Held {heldDays} days since {paid:dd MMM yyyy}. Check whether a refund is due.");
        }

        return new(LevelNone, "ok", "EMD ready", null);
    }
}
