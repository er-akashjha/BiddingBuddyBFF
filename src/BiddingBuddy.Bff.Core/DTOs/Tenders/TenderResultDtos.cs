namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>
/// A tender's award result as proxied from BiddingBuddyServices (Mongo <c>tender_results</c>):
/// the winner + full competitive price ladder. Shapes mirror the Services model; the BFF is a
/// pass-through (generic data is never stored in Postgres).
/// </summary>
public record TenderResultDto(
    string PlatformTenderId,
    string BId,
    string ResultViewType,
    string ResultUrl,
    string? Category,
    string? State,
    bool IsReverseAuction,
    string? BidStatus,
    string? Ministry,
    string? Department,
    string? Organisation,
    string? Office,
    int? Quantity,
    DateTimeOffset? BidStartAt,
    DateTimeOffset? BidEndAt,
    DateTimeOffset? BidOpeningAt,
    List<TenderResultBidderDto> Bidders,
    int ParticipantCount,
    TenderResultWinnerDto? Winner,
    decimal? L1Price,
    decimal? L2Price,
    decimal? L1L2Spread,
    DateTimeOffset? ScrapedAt
);

/// <param name="SellerKey">Normalized identity key — the UI matches the org's own seller against
/// THIS, not the display name, so casing/suffix variants still resolve.</param>
public record TenderResultBidderDto(
    int SerialNo,
    string SellerName,
    string SellerKey,
    bool IsMse,
    string? MseSocialCategory,
    bool UnderPma,
    string? OfferedItems,
    decimal? TotalPrice,
    string? Rank,
    int? RankNumber,
    string? Status,
    bool IsQualified
);

public record TenderResultWinnerDto(
    string SellerName,
    string SellerKey,
    decimal? Price,
    string? Rank,
    bool IsMse,
    bool UnderPma,
    string Confidence
);

/// <summary>
/// A tender's award result plus the caller-org's own position on the ladder.
///
/// The "which row is us" question is answered HERE rather than in the SPA on purpose: it needs the
/// canonical seller-key normalization, and re-implementing that in TypeScript would make a third
/// copy that silently drifts from the two C# ones — at which point a user's row highlights in one
/// place and not another.
/// </summary>
/// <param name="YourSellerKey">The seller key we matched the org by, so the UI can explain what it
/// looked for when nothing matched.</param>
/// <param name="YourRow">The org's own ladder row, or null when the org isn't on this ladder.</param>
public record TenderResultViewDto(
    TenderResultDto Result,
    string? YourSellerKey,
    TenderResultBidderDto? YourRow,
    YourOutcomeDto? YourOutcome
);

/// <summary>
/// The org's outcome on this award, derived from its own ladder row.
/// </summary>
/// <param name="Verdict">"won" | "lost" — from the ladder, independent of the bid pipeline stage.</param>
/// <param name="LossKind">Why we lost: "disqualified" (compliance failure) | "preference"
/// (L1 held an MSE/PMA advantage) | "outbid" (pure pricing). Null when we won. These are three
/// different lessons and collapsing them into "lost" throws away the actionable part.</param>
/// <param name="GapToWinner">Our price − the winning price. Positive = how much we needed to come down.</param>
/// <param name="GapToWinnerPct">The same gap as a percentage of our bid.</param>
public record YourOutcomeDto(
    string Verdict,
    string? LossKind,
    int? YourRank,
    decimal? YourPrice,
    decimal? GapToWinner,
    double? GapToWinnerPct
);

/// <summary>Aggregate market pricing/competition stats over awards for a slice.</summary>
public record MarketPricingStatsDto(
    string? Category,
    string? State,
    long SampleSize,
    decimal? AvgWinningValue,
    decimal? MinWinningValue,
    decimal? MaxWinningValue,
    decimal? P25WinningValue,
    decimal? MedianWinningValue,
    decimal? P75WinningValue,
    double? AvgParticipants,
    decimal? AvgL1L2Spread,
    double? AvgL1L2SpreadPct,
    double? MseWinRate,
    double? ReverseAuctionRate
);

/// <summary>One bucket of a grouped market aggregation.</summary>
public record MarketGroupBucketDto(
    string Key,
    long SampleSize,
    decimal? AvgWinningValue,
    decimal? MedianWinningValue,
    double? AvgParticipants
);

/// <summary>Award-history stats for one seller, derived from the ladders they appear on.</summary>
public record SellerStatsDto(
    string SellerKey,
    string SellerName,
    long Appearances,
    long Wins,
    double WinRate,
    decimal? AvgWinningValue,
    decimal? TotalWonValue,
    double? AvgRank,
    bool IsMse,
    bool UnderPma,
    List<string> Categories,
    List<string> States,
    DateTimeOffset? LastSeenAt
);

/// <summary>A head-to-head record between the caller's org and one rival.</summary>
public record HeadToHeadRecordDto(
    string SellerKey,
    string SellerName,
    long Encounters,
    long TheyRankedHigher,
    long WeRankedHigher,
    double? AvgPriceDeltaPct
);

/// <summary>Buyer-level award behaviour.</summary>
public record BuyerProfileDto(
    string Buyer,
    long AwardCount,
    double? AvgParticipants,
    decimal? AvgWinningValue,
    decimal? MedianWinningValue,
    double? MseWinRate,
    double? Top3Concentration,
    List<SellerStatsDto> TopSellers
);

/// <summary>A page of award records.</summary>
public record TenderResultPageDto(
    List<TenderResultDto> Items,
    long Total,
    int Page,
    int PageSize
);

/// <summary>
/// Filters shared by every market read, mirroring Services' <c>MarketQuery</c>. All optional.
/// </summary>
public class MarketFilterDto
{
    public string? Category { get; set; }
    public string? State { get; set; }
    public string? SellerKey { get; set; }
    public string? Buyer { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public bool? IsReverseAuction { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Render as a query string for the Services call. Only set values are emitted.</summary>
    public IDictionary<string, string?> ToQuery()
    {
        var qs = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(Category)) qs["category"] = Category;
        if (!string.IsNullOrWhiteSpace(State)) qs["state"] = State;
        if (!string.IsNullOrWhiteSpace(SellerKey)) qs["sellerKey"] = SellerKey;
        if (!string.IsNullOrWhiteSpace(Buyer)) qs["buyer"] = Buyer;
        if (From.HasValue) qs["from"] = From.Value.ToString("o");
        if (To.HasValue) qs["to"] = To.Value.ToString("o");
        if (MinValue.HasValue) qs["minValue"] = MinValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (MaxValue.HasValue) qs["maxValue"] = MaxValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (IsReverseAuction.HasValue) qs["isReverseAuction"] = IsReverseAuction.Value ? "true" : "false";
        return qs;
    }
}
