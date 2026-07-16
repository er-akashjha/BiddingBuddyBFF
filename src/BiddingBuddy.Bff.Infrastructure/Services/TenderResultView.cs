using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Locates the caller-org on an award ladder and derives what actually happened to them.
///
/// Kept server-side because it needs <see cref="SellerKey"/>: re-implementing that normalization in
/// the SPA would create a third copy of the rules (Services, BFF, TypeScript) that drifts apart, and
/// a user's row would then highlight on one screen but not another.
/// </summary>
public static class TenderResultView
{
    public static TenderResultViewDto Build(TenderResultDto result, string? gemSellerName, string orgName)
    {
        // The explicit setting wins; the org name is the zero-config fallback (same precedence the
        // award pipeline uses to resolve bids, so the two never disagree about who we are).
        var candidates = new[] { gemSellerName, orgName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => SellerKey.For(s))
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();

        TenderResultBidderDto? mine = null;
        string? matchedKey = null;
        foreach (var key in candidates)
        {
            mine = result.Bidders.FirstOrDefault(b => b.SellerKey == key);
            if (mine is not null) { matchedKey = key; break; }
        }

        return new TenderResultViewDto(
            Result:        result,
            YourSellerKey: matchedKey ?? candidates.FirstOrDefault(),
            YourRow:       mine,
            YourOutcome:   mine is null ? null : BuildOutcome(result, mine));
    }

    private static YourOutcomeDto BuildOutcome(TenderResultDto result, TenderResultBidderDto mine)
    {
        var won = mine.RankNumber == 1 && mine.IsQualified;
        if (won)
            return new YourOutcomeDto("won", null, mine.RankNumber, mine.TotalPrice, 0m, 0d);

        // Three genuinely different failures — see YourOutcomeDto.LossKind.
        string lossKind;
        if (!mine.IsQualified)
        {
            lossKind = "disqualified";
        }
        else if (result.Winner is { IsMse: true } && !mine.IsMse
              || result.Winner is { UnderPma: true } && !mine.UnderPma)
        {
            lossKind = "preference";
        }
        else
        {
            lossKind = "outbid";
        }

        var winnerPrice = result.Winner?.Price ?? result.L1Price;
        decimal? gap = mine.TotalPrice.HasValue && winnerPrice.HasValue
            ? mine.TotalPrice.Value - winnerPrice.Value
            : null;
        double? gapPct = gap.HasValue && mine.TotalPrice is > 0
            ? (double)(gap.Value / mine.TotalPrice.Value)
            : null;

        return new YourOutcomeDto("lost", lossKind, mine.RankNumber, mine.TotalPrice, gap, gapPct);
    }
}
