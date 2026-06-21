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

// ── Reports & Analytics dashboard ────────────────────────────────────────────

/// <summary>Top KPI tiles on the Reports page.</summary>
public record KpisDto(
    int     TendersAnalyzed,    // scraped tender count in the period (global, not org-scoped)
    int     BidsSubmitted,      // org's bids with stage in (submitted | won | lost) created in the period
    decimal WinRate,            // 0-100, wins / (wins + losses) for completed bids in the period
    decimal RevenueWon,         // sum(won_value) for bids that ended in 'won' in the period
    decimal PipelineValue,      // sum(our_bid_value ?? tender_value) over all org bids not won/lost (current state, not period-scoped)
    DateTime From,
    DateTime To
);

/// <summary>Full Reports-page dashboard payload — one round-trip drives every chart.</summary>
public record DashboardDto(
    IReadOnlyList<TenderActivityPointDto>   TenderActivity,
    IReadOnlyList<WinLossByCategoryDto>     WinLossByCategory,
    IReadOnlyList<RevenueByMonthDto>        RevenueByMonth,
    IReadOnlyList<WinRateOverTimePointDto>  WinRateOverTime,
    DateTime From,
    DateTime To
);

public record TenderActivityPointDto(string Month, int Discovered, int Won);
public record WinLossByCategoryDto(string Category, int Won, int Lost);
public record RevenueByMonthDto(string Month, decimal WonValue);
public record WinRateOverTimePointDto(string Month, decimal WinRate);
