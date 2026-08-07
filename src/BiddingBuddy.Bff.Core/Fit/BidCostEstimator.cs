using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Fit;

/// <summary>
/// What bidding this tender ties up, in rupees.
///
/// <para>Fully deterministic — every line is read off the tender's own structured fields, and a
/// line we cannot compute is <b>omitted</b> rather than estimated. That constraint is the whole
/// point: a "typical document preparation cost: ₹15,000" line would be a number we invented,
/// sitting next to numbers we didn't, and the customer has no way to tell them apart.</para>
///
/// <para><b>Blocked ≠ spent.</b> EMD comes back when the bid is decided and the ePBG is released
/// at the end of the contract. Presenting them as expenditure would overstate the cost of
/// bidding by an order of magnitude, so each line says which it is and the client shows it.</para>
/// </summary>
public static class BidCostEstimator
{
    public static BidCostEstimate Estimate(TenderDetailDto tender, bool isMse)
    {
        var lines = new List<BidCostLine>();

        var emd = tender.Financial?.Emd?.Amount ?? tender.EmdAmount;
        if (emd is > 0)
        {
            if (isMse)
                lines.Add(new BidCostLine(
                    "EMD (bid security)", 0m, BidCostKind.Blocked,
                    $"{Money.Inr(emd.Value)} waived — MSEs are exempt on production of the Udyam certificate.",
                    IsExempt: true));
            else
                lines.Add(new BidCostLine(
                    "EMD (bid security)", emd.Value, BidCostKind.Blocked,
                    "Blocked from submission until the bid is decided, then returned.",
                    IsExempt: false));
        }

        var epbg = EpbgExposure(tender);
        if (epbg is > 0)
        {
            var duration = tender.Financial?.Epbg?.DurationMonths;
            lines.Add(new BidCostLine(
                "Performance guarantee (ePBG)", epbg.Value, BidCostKind.CommittedOnWin,
                "Only if you win: "
                + $"{tender.Financial?.Epbg?.Percentage}% of contract value"
                + (duration is not null ? $", held for {duration} months." : ".")
                + " Consumes your bank-guarantee line, not your cash.",
                IsExempt: false));
        }

        // Deliberately NO "document preparation" or "tender fee" line. GeM charges no tender fee
        // and we do not extract eProcure's; inventing either would put a fabricated number on
        // the one screen whose value is that its numbers are real.

        return new BidCostEstimate(
            Total: lines.Sum(l => l.Amount),
            BlockedNow: lines.Where(l => l.Kind == BidCostKind.Blocked).Sum(l => l.Amount),
            CommittedOnWin: lines.Where(l => l.Kind == BidCostKind.CommittedOnWin).Sum(l => l.Amount),
            Lines: lines);
    }

    /// <summary>
    /// ePBG rupee exposure = percentage × contract value. Null when either side is missing —
    /// there is no default percentage worth guessing, and a wrong one here misleads a bidder
    /// about their bank line.
    /// </summary>
    public static decimal? EpbgExposure(TenderDetailDto tender)
    {
        var pct = tender.Financial?.Epbg?.Percentage;
        var value = tender.Financial?.EstimatedBidValue ?? tender.TenderValue;
        if (pct is null or <= 0 || value is null or <= 0) return null;
        return Math.Round(value.Value * (decimal)pct.Value / 100m, 2);
    }
}

public enum BidCostKind
{
    /// <summary>Capital tied up from submission and returned when the bid is decided.</summary>
    Blocked,
    /// <summary>Only owed if the bid wins.</summary>
    CommittedOnWin,
}

public sealed record BidCostLine(
    string Label,
    decimal Amount,
    BidCostKind Kind,
    string Detail,
    bool IsExempt);

public sealed record BidCostEstimate(
    decimal Total,
    decimal BlockedNow,
    decimal CommittedOnWin,
    IReadOnlyList<BidCostLine> Lines)
{
    public static readonly BidCostEstimate Empty = new(0, 0, 0, []);

    /// <summary>Nothing was computable — the client says so rather than printing ₹0, which
    /// reads as "free" instead of "unknown".</summary>
    public bool HasAnything => Lines.Count > 0;
}
