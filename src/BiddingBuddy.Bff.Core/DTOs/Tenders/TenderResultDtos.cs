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

public record TenderResultBidderDto(
    int SerialNo,
    string SellerName,
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
    decimal? Price,
    string? Rank,
    bool IsMse,
    bool UnderPma,
    string Confidence
);

/// <summary>Aggregate market pricing/competition stats over awards for a category/state slice.</summary>
public record MarketPricingStatsDto(
    string? Category,
    string? State,
    long SampleSize,
    decimal? AvgWinningValue,
    decimal? MinWinningValue,
    decimal? MaxWinningValue,
    double? AvgParticipants,
    decimal? AvgL1L2Spread
);
