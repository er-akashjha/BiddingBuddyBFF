namespace BiddingBuddy.Bff.Core.DTOs.Analysis;

public record PerformanceSnapshotDto(
    Guid Id,
    string Period,
    int TotalBids,
    int WonBids,
    int LostBids,
    int ActiveBids,
    decimal? WinRate,
    decimal? TotalWonValue,
    decimal? AvgBidValue,
    string? TopCategory,
    string? TopState,
    DateTime GeneratedAt
);
