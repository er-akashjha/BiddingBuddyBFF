using BiddingBuddy.Bff.Core.DTOs.Tenders;
using Xunit;

/// <summary>
/// The UI badges reverse auctions off <c>bidKind</c>. The pipeline stamps <c>source.bidKind</c>
/// only from the point it shipped, so every tender enriched before that has it null — including
/// all 5,190 reverse auctions already in Mongo. Rather than gate the badge on a backfill, the BFF
/// re-derives the kind from the bid number, which GeM has always encoded. These pin that fallback:
/// get it wrong and the badge silently disappears for exactly the records it exists to flag.
/// </summary>
public sealed class TenderKindTests
{
    [Fact]
    public void StoredValueWins_WhenPresent() =>
        Assert.Equal(TenderKind.Bid, TenderKind.Resolve(TenderKind.Bid, "GEM/2026/R/694756"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingStoredValue_FallsBackToTheBidNumber(string? stored) =>
        Assert.Equal(TenderKind.ReverseAuction, TenderKind.Resolve(stored, "GEM/2026/R/694756"));

    [Theory]
    [InlineData("GEM/2026/R/694756", TenderKind.ReverseAuction)]
    [InlineData("GEM/2026/B/7593620", TenderKind.Bid)]
    [InlineData("gem/2026/r/694756", TenderKind.ReverseAuction)]  // case-insensitive
    [InlineData("GEM/2026/X/123", null)]                          // unknown segment → no guess
    [InlineData("2026_ALHW_915157_1", null)]                      // eprocure id → not a GeM shape
    [InlineData("GEM/2026", null)]                                // too few segments
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DeriveFromBidNumber_ReadsTheTypeSegment(string? bidNumber, string? expected) =>
        Assert.Equal(expected, TenderKind.DeriveFromBidNumber(bidNumber));

    [Fact]
    public void UnknownEverything_ResolvesToNull_SoTheUiRendersNothing() =>
        Assert.Null(TenderKind.Resolve(null, null));
}
