namespace BiddingBuddy.Bff.Core.DTOs.Competitors;

public record CompetitorDto(
    Guid Id,
    string CompanyName,
    string? GemSellerId,
    string? Tier,
    string? ThreatLevel,
    decimal? WinRate,
    int TotalContracts,
    decimal? TotalWinValue,
    decimal? AvgBidValue,
    string[]? ActiveStates,
    string[]? ActiveCategories,
    DateOnly? FirstSeenAt,
    DateOnly? LastSeenAt,
    DateTime UpdatedAt
);

public record CompetitorDetailDto(
    Guid Id,
    string CompanyName,
    string? GemSellerId,
    string? Tier,
    string? ThreatLevel,
    decimal? WinRate,
    int TotalContracts,
    decimal? TotalWinValue,
    decimal? AvgBidValue,
    string[]? ActiveStates,
    string[]? ActiveCategories,
    DateOnly? FirstSeenAt,
    DateOnly? LastSeenAt,
    DateTime UpdatedAt,
    IReadOnlyList<BidObservationDto> RecentObservations
);

public record BidObservationDto(
    Guid Id,
    Guid? TenderId,
    string? GemTenderId,
    string? TenderTitle,
    decimal? BidValue,
    bool Won,
    string? Notes,
    DateOnly ObservedDate
);

public record UpdateCompetitorDto(
    string? Tier,
    string? ThreatLevel
);
