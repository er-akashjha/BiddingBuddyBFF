using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.Helpers;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Bids;

/// <summary>
/// <see cref="EmdVerdict"/> is the one answer to "is this EMD OK?" — the register, the bid's EMD
/// tab and the reminder emails all render it. These tests pin the precedence, because the order
/// is the whole design: the verdict a user sees should be the thing that will hurt them soonest,
/// not whichever rule happened to match first.
/// </summary>
public sealed class EmdVerdictTests
{
    private static readonly DateOnly Today = new(2026, 7, 22);

    private static EmdVerdict.Input Base(
        string requirement = EmdRequirements.Required,
        bool bidIsOpen = true,
        bool hasRecord = true,
        string? mode = "bg",
        string? status = "submitted",
        DateOnly? validUntil = null,
        DateOnly? paymentDate = null,
        int live = 1,
        bool overdue = false,
        bool delivered = true,
        decimal? tenderAmount = null,
        string? exemptionBasis = null)
        => new(requirement, bidIsOpen, tenderAmount, exemptionBasis, hasRecord, mode, status,
            validUntil, paymentDate, live, overdue, delivered);

    private static string Code(EmdVerdict.Input i) => EmdVerdict.For(i, Today).Code;
    private static string Level(EmdVerdict.Input i) => EmdVerdict.For(i, Today).Level;

    // ── requirement gates ───────────────────────────────────────────────────────

    [Fact]
    public void Not_required_is_silent()
        => Assert.Equal(EmdVerdict.LevelNone, Level(Base(requirement: EmdRequirements.NotRequired, hasRecord: false)));

    [Fact]
    public void Exempt_without_a_basis_nudges_but_does_not_alarm()
    {
        var v = EmdVerdict.For(Base(requirement: EmdRequirements.Exempt, hasRecord: false), Today);
        Assert.Equal("exemption_unrecorded", v.Code);
        Assert.Equal(EmdVerdict.LevelInfo, v.Level);
    }

    [Fact]
    public void Exempt_with_a_basis_is_silent()
        => Assert.Equal(EmdVerdict.LevelNone,
            Level(Base(requirement: EmdRequirements.Exempt, hasRecord: false, exemptionBasis: "MSME")));

    [Fact]
    public void Undecided_warns_only_when_the_tender_actually_asks_for_money()
    {
        Assert.Equal(EmdVerdict.LevelWarn,
            Level(Base(requirement: EmdRequirements.Unknown, hasRecord: false, tenderAmount: 90_000m)));
        Assert.Equal(EmdVerdict.LevelInfo,
            Level(Base(requirement: EmdRequirements.Unknown, hasRecord: false)));
    }

    // ── the danger ladder ───────────────────────────────────────────────────────

    [Fact]
    public void Required_with_nothing_recorded_is_danger_on_an_open_bid()
    {
        Assert.Equal("emd_missing", Code(Base(hasRecord: false)));
        Assert.Equal(EmdVerdict.LevelDanger, Level(Base(hasRecord: false)));
    }

    [Fact]
    public void A_closed_bid_downgrades_missing_from_danger_to_warn()
    {
        // Nothing can be done about it now; it should not sit in the same red bucket as a bid
        // that is still winnable.
        Assert.Equal(EmdVerdict.LevelWarn, Level(Base(hasRecord: false, bidIsOpen: false)));
    }

    [Fact]
    public void A_late_courier_outranks_an_expiring_instrument()
    {
        // Both true. The courier is the one that ends the bid this week.
        var v = Base(overdue: true, delivered: false, validUntil: Today.AddDays(3));
        Assert.Equal("courier_late", Code(v));
    }

    [Fact]
    public void An_instrument_with_no_consignment_is_danger()
        => Assert.Equal("not_couriered", Code(Base(mode: "dd", live: 0, delivered: false)));

    [Fact]
    public void An_electronic_emd_never_asks_for_a_courier()
    {
        // NEFT has no paper. Demanding a consignment for it would be noise on every online EMD.
        Assert.Equal("ok", Code(Base(mode: "neft", live: 0, delivered: false, status: "held",
            paymentDate: Today.AddDays(-3))));
    }

    [Fact]
    public void An_expired_instrument_is_danger_and_an_expiring_one_is_warn()
    {
        Assert.Equal("instrument_expired", Code(Base(validUntil: Today.AddDays(-1))));
        Assert.Equal(EmdVerdict.LevelDanger, Level(Base(validUntil: Today.AddDays(-1))));

        Assert.Equal("instrument_expiring", Code(Base(validUntil: Today.AddDays(10))));
        Assert.Equal(EmdVerdict.LevelWarn, Level(Base(validUntil: Today.AddDays(10))));
    }

    [Fact]
    public void An_instrument_expiring_well_beyond_the_lead_window_is_not_flagged()
        => Assert.Equal("ok", Code(Base(validUntil: Today.AddDays(90))));

    [Fact]
    public void Dispatched_but_not_yet_delivered_is_in_transit()
        => Assert.Equal("in_transit", Code(Base(delivered: false)));

    // ── the register-only verdict ───────────────────────────────────────────────

    [Fact]
    public void An_emd_held_past_the_threshold_asks_someone_to_chase_the_refund()
    {
        // The reason the register exists: by 90 days the bid is long closed, so the bid page —
        // where every other verdict surfaces — is a page nobody opens again.
        var v = Base(mode: "neft", status: "held", paymentDate: Today.AddDays(-120), live: 0, delivered: false);
        Assert.Equal("refund_overdue", Code(v));
        Assert.Equal(EmdVerdict.LevelWarn, Level(v));
    }

    [Fact]
    public void An_emd_held_a_short_while_is_not_a_refund_problem()
        => Assert.Equal("ok",
            Code(Base(mode: "neft", status: "held", paymentDate: Today.AddDays(-10), live: 0, delivered: false)));

    [Fact]
    public void A_settled_emd_stops_reporting_courier_and_expiry_problems()
    {
        // Refunded money is back. Without this, an old refunded BG would keep reading
        // "instrument expired" forever and clog the needs-action filter.
        var refunded = Base(status: "refunded", validUntil: Today.AddDays(-30), overdue: true, delivered: false);
        Assert.Equal("refunded", Code(refunded));
        Assert.Equal(EmdVerdict.LevelNone, Level(refunded));

        var forfeited = Base(status: "forfeited", validUntil: Today.AddDays(-30));
        Assert.Equal("forfeited", Code(forfeited));
    }

    [Fact]
    public void A_healthy_delivered_instrument_is_silent()
        => Assert.Equal("ok", Code(Base(validUntil: Today.AddDays(120), status: "submitted")));

    [Fact]
    public void Every_verdict_carries_a_chip_and_only_quiet_ones_omit_the_message()
    {
        var noisy = EmdVerdict.For(Base(hasRecord: false), Today);
        Assert.False(string.IsNullOrWhiteSpace(noisy.Chip));
        Assert.False(string.IsNullOrWhiteSpace(noisy.Message));

        var quiet = EmdVerdict.For(Base(validUntil: Today.AddDays(120)), Today);
        Assert.False(string.IsNullOrWhiteSpace(quiet.Chip));
        Assert.Null(quiet.Message);
    }
}
