using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Infrastructure.Services;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// TenderResultView answers "which ladder row is us, and what happened to us".
///
/// The loss taxonomy is the part worth pinning: being DISQUALIFIED, losing on PREFERENCE, and being
/// OUTBID look identical in a bid's pipeline stage but call for completely different responses, and
/// getting them confused would tell a user to cut price when their real problem was paperwork.
/// </summary>
public sealed class TenderResultViewTests
{
    private static TenderResultBidderDto Bidder(
        string name, int? rank, decimal? price, bool qualified = true,
        bool mse = false, bool pma = false, string? status = null)
        => new(
            SerialNo: rank ?? 1,
            SellerName: name,
            SellerKey: SellerKey.For(name),
            IsMse: mse,
            MseSocialCategory: null,
            UnderPma: pma,
            OfferedItems: null,
            TotalPrice: price,
            Rank: rank is int r ? $"L{r}" : null,
            RankNumber: rank,
            Status: status ?? (qualified ? "Qualified" : "Disqualified"),
            IsQualified: qualified);

    private static TenderResultDto Result(
        IEnumerable<TenderResultBidderDto> bidders, TenderResultWinnerDto? winner, decimal? l1)
        => new(
            PlatformTenderId: "GEM/2026/B/1", BId: "1", ResultViewType: "single-packet",
            ResultUrl: "https://bidplus.gem.gov.in/x", Category: "Furniture", State: "Delhi",
            IsReverseAuction: false, BidStatus: null, Ministry: null, Department: null,
            Organisation: null, Office: null, Quantity: null,
            BidStartAt: null, BidEndAt: null, BidOpeningAt: null,
            Bidders: bidders.ToList(), ParticipantCount: bidders.Count(), Winner: winner,
            L1Price: l1, L2Price: null, L1L2Spread: null, ScrapedAt: null);

    private static TenderResultWinnerDto Winner(string name, decimal price, bool mse = false, bool pma = false)
        => new(name, SellerKey.For(name), price, "L1", mse, pma, "high");

    [Fact]
    public void Finds_us_by_gem_seller_name_despite_legal_suffix_variance()
    {
        var result = Result(
            [Bidder("JAGJIT TRADERS", 1, 30_069), Bidder("ABC Private Limited", 2, 33_861)],
            Winner("JAGJIT TRADERS", 30_069), 30_069);

        // The org calls itself "ABC Pvt Ltd"; GeM printed "ABC Private Limited".
        var view = TenderResultView.Build(result, gemSellerName: "ABC Pvt Ltd", orgName: "Irrelevant");

        Assert.NotNull(view.YourRow);
        Assert.Equal("ABC Private Limited", view.YourRow!.SellerName);
    }

    [Fact]
    public void Falls_back_to_the_org_name_when_no_seller_name_is_configured()
    {
        var result = Result(
            [Bidder("Villa Communication", 1, 100)], Winner("Villa Communication", 100), 100);

        var view = TenderResultView.Build(result, gemSellerName: null, orgName: "Villa Communication");

        Assert.NotNull(view.YourRow);
        Assert.Equal("won", view.YourOutcome!.Verdict);
    }

    [Fact]
    public void Reports_no_row_and_the_key_it_looked_for_when_we_are_absent()
    {
        var result = Result([Bidder("Someone Else", 1, 100)], Winner("Someone Else", 100), 100);

        var view = TenderResultView.Build(result, gemSellerName: "Our Firm", orgName: "Our Firm");

        Assert.Null(view.YourRow);
        Assert.Null(view.YourOutcome);
        // Surfaced so the UI can say what it searched for rather than leaving the user guessing.
        Assert.Equal("our firm", view.YourSellerKey);
    }

    [Fact]
    public void Won_when_we_are_L1_qualified()
    {
        var result = Result(
            [Bidder("Us", 1, 30_000), Bidder("Them", 2, 33_000)], Winner("Us", 30_000), 30_000);

        var view = TenderResultView.Build(result, "Us", "Us");

        Assert.Equal("won", view.YourOutcome!.Verdict);
        Assert.Null(view.YourOutcome.LossKind);
    }

    [Fact]
    public void Disqualified_is_a_compliance_failure_not_a_pricing_one()
    {
        // We were the CHEAPEST and still lost — proof the loss had nothing to do with price.
        var result = Result(
            [Bidder("Us", null, 20_000, qualified: false, status: "Disqualified"), Bidder("Them", 1, 33_000)],
            Winner("Them", 33_000), 33_000);

        var view = TenderResultView.Build(result, "Us", "Us");

        Assert.Equal("lost", view.YourOutcome!.Verdict);
        Assert.Equal("disqualified", view.YourOutcome.LossKind);
    }

    [Fact]
    public void Lost_on_preference_when_the_winner_holds_an_MSE_edge_we_lack()
    {
        var result = Result(
            [Bidder("Us", 2, 34_000), Bidder("Them", 1, 33_000, mse: true)],
            Winner("Them", 33_000, mse: true), 33_000);

        var view = TenderResultView.Build(result, "Us", "Us");

        Assert.Equal("preference", view.YourOutcome!.LossKind);
    }

    [Fact]
    public void Not_a_preference_loss_when_we_hold_the_same_MSE_status()
    {
        var result = Result(
            [Bidder("Us", 2, 34_000, mse: true), Bidder("Them", 1, 33_000, mse: true)],
            Winner("Them", 33_000, mse: true), 33_000);

        var view = TenderResultView.Build(result, "Us", "Us");

        // Both are MSE, so preference didn't decide it — this is a straight pricing loss.
        Assert.Equal("outbid", view.YourOutcome!.LossKind);
    }

    [Fact]
    public void Outbid_quantifies_the_gap_we_needed_to_close()
    {
        var result = Result(
            [Bidder("Us", 2, 40_000), Bidder("Them", 1, 30_000)], Winner("Them", 30_000), 30_000);

        var view = TenderResultView.Build(result, "Us", "Us");

        Assert.Equal("outbid", view.YourOutcome!.LossKind);
        Assert.Equal(10_000m, view.YourOutcome.GapToWinner);
        Assert.Equal(0.25d, view.YourOutcome.GapToWinnerPct!.Value, 3); // 10k off a 40k bid
        Assert.Equal(2, view.YourOutcome.YourRank);
    }
}
