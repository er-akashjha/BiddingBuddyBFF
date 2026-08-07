using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Fit;

namespace BiddingBuddy.Bff.Tests.Fit;

/// <summary>
/// The fit engine decides whether a customer commits capital to a bid, so these tests are less
/// about coverage than about pinning the two failure directions that matter:
///
/// <list type="bullet">
///   <item>never invent — an unknown must surface as unknown and must not become a pass;</item>
///   <item>never scare a customer off a bid they are statutorily entitled to make — the MSE and
///   startup relaxations have to be applied BEFORE the raw threshold is compared.</item>
/// </list>
/// </summary>
public class TenderFitRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 5);
    private static readonly DateOnly Close = new(2026, 9, 15);

    // ── Turnover ────────────────────────────────────────────────────────────

    [Fact]
    public void Turnover_shortfall_blocks()
    {
        var result = Evaluate(
            TenderWith(turnoverRequired: 20_000_000m),
            ProfileWith(turnoverFy1: 5_000_000m));

        var finding = Single(result, "TURNOVER_SHORTFALL");
        Assert.Equal(FitSeverities.Blocker, finding.Severity);
        Assert.Equal(FitVerdicts.Blocked, result.Verdict);
        // The shortfall figure is what the user acts on, so it belongs in the sentence.
        Assert.Contains("₹1.5Cr", finding.Detail);
    }

    [Fact]
    public void Turnover_met_passes()
    {
        var result = Evaluate(
            TenderWith(turnoverRequired: 5_000_000m),
            ProfileWith(turnoverFy1: 20_000_000m));

        Assert.Equal(FitSeverities.Ok, Single(result, "TURNOVER_MET").Severity);
        Assert.Equal(FitVerdicts.Go, result.Verdict);
    }

    [Fact]
    public void Turnover_averages_only_the_years_actually_supplied()
    {
        // A missing FY must not be averaged in as zero — that manufactures a shortfall out of
        // an org that simply hasn't finished typing, and blocks a bid it could win.
        var profile = ProfileWith(turnoverFy1: 12_000_000m);
        profile.TurnoverFy2 = 12_000_000m;   // fy3 left null

        var result = Evaluate(TenderWith(turnoverRequired: 12_000_000m), profile);

        Assert.Equal("TURNOVER_MET", Single(result, "TURNOVER_MET").Code);
    }

    [Fact]
    public void Turnover_unknown_is_reported_as_unknown_not_as_a_pass()
    {
        var result = Evaluate(TenderWith(turnoverRequired: 20_000_000m), profile: null);

        var finding = Single(result, "TURNOVER_UNKNOWN");
        Assert.Equal(FitSeverities.Unknown, finding.Severity);
        Assert.NotEqual(FitSeverities.Ok, finding.Severity);
        // With nothing on file at all, the engine must decline rather than green-light.
        Assert.Equal(FitVerdicts.InsufficientData, result.Verdict);
    }

    [Fact]
    public void Mse_relaxation_is_applied_before_the_threshold_is_compared()
    {
        // The customer would fail the raw threshold ten times over, but the tender offers the
        // MSE relaxation and they hold Udyam — reporting a blocker here would push them off a
        // bid the PP Policy entitles them to make.
        var tender = TenderWith(turnoverRequired: 50_000_000m, mseRelaxation: true);
        var profile = ProfileWith(turnoverFy1: 5_000_000m);
        profile.UdyamNumber = "UDYAM-DL-05-1234567";

        var result = Evaluate(tender, profile);

        var finding = Single(result, "TURNOVER_MSE_RELAXED");
        Assert.Equal(FitSeverities.Ok, finding.Severity);
        Assert.NotEqual(FitVerdicts.Blocked, result.Verdict);
        // Self-asserted registration → low confidence, and the citation must be present so the
        // bidder can take the instrument to their own team.
        Assert.Equal(FitConfidence.Low, finding.Confidence);
        Assert.Contains("2012", finding.Citation);
    }

    [Fact]
    public void Mse_relaxation_offered_but_not_registered_still_blocks()
    {
        var result = Evaluate(
            TenderWith(turnoverRequired: 50_000_000m, mseRelaxation: true),
            ProfileWith(turnoverFy1: 5_000_000m));   // no Udyam number

        var finding = Single(result, "TURNOVER_SHORTFALL");
        Assert.Equal(FitSeverities.Blocker, finding.Severity);
        // ...but it must point at the way out rather than just saying no.
        Assert.Contains("Udyam", finding.Detail);
    }

    // ── Certificates ────────────────────────────────────────────────────────

    [Fact]
    public void Certificate_expiring_before_the_deadline_blocks()
    {
        // The highest-value rule in the engine: valid today, worthless on bid day, and nothing
        // else in the product notices.
        var tender = TenderWith(certifications: ["ISO 9001:2015"]);
        var credential = Credential("ISO 9001", validUntil: Close.AddDays(-3));

        var result = Evaluate(tender, ProfileWith(turnoverFy1: 1m), [credential]);

        var finding = Single(result, "CERTIFICATION_EXPIRES_BEFORE_DEADLINE");
        Assert.Equal(FitSeverities.Blocker, finding.Severity);
        Assert.Equal(FitVerdicts.Blocked, result.Verdict);
    }

    [Fact]
    public void Certificate_valid_past_the_deadline_passes()
    {
        var result = Evaluate(
            TenderWith(certifications: ["ISO 9001:2015"]),
            ProfileWith(turnoverFy1: 1m),
            [Credential("ISO 9001", validUntil: Close.AddDays(30))]);

        Assert.Equal(FitSeverities.Ok, Single(result, "CERTIFICATION_HELD").Severity);
    }

    [Fact]
    public void Certificate_edition_year_does_not_defeat_the_match()
    {
        // A tender asking for "ISO 9001:2008" is satisfied by a current ISO 9001 certificate.
        // Treating the edition year as part of the identity would flag every up-to-date
        // certificate in the country as the wrong one.
        var result = Evaluate(
            TenderWith(certifications: ["ISO 9001:2008 certification"]),
            ProfileWith(turnoverFy1: 1m),
            [Credential("ISO 9001:2015", validUntil: Close.AddDays(30))]);

        Assert.Equal("CERTIFICATION_HELD", Single(result, "CERTIFICATION_HELD").Code);
    }

    [Fact]
    public void A_different_iso_standard_does_not_satisfy_the_requirement()
    {
        // The matcher is loose about wording and strict about the number. ISO 27001 must never
        // stand in for ISO 9001.
        var result = Evaluate(
            TenderWith(certifications: ["ISO 9001:2015"]),
            ProfileWith(turnoverFy1: 1m),
            [Credential("ISO 27001:2022", validUntil: Close.AddDays(30))]);

        Assert.Equal(FitSeverities.Blocker, Single(result, "CERTIFICATION_MISSING").Severity);
    }

    [Fact]
    public void Missing_certificate_blocks()
    {
        var result = Evaluate(
            TenderWith(certifications: ["ISO 27001:2022"]),
            ProfileWith(turnoverFy1: 1m));

        Assert.Equal(FitSeverities.Blocker, Single(result, "CERTIFICATION_MISSING").Severity);
    }

    // ── EMD ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Emd_over_headroom_is_a_gap_not_a_blocker()
    {
        // Capital can be arranged inside a bid window; a completed financial year cannot.
        var profile = ProfileWith(turnoverFy1: 1m);
        profile.EmdHeadroom = 100_000m;

        var result = Evaluate(TenderWith(emd: 500_000m), profile);

        Assert.Equal(FitSeverities.Gap, Single(result, "EMD_EXCEEDS_HEADROOM").Severity);
        Assert.Equal(FitVerdicts.GoWithGaps, result.Verdict);
    }

    [Fact]
    public void Mse_is_reported_exempt_from_emd_and_the_cost_line_is_zeroed()
    {
        var profile = ProfileWith(turnoverFy1: 1m);
        profile.UdyamNumber = "UDYAM-DL-05-1234567";
        profile.EmdHeadroom = 1m;    // would fail badly if the exemption were not applied

        var result = Evaluate(TenderWith(emd: 500_000m), profile);

        Assert.Equal(FitSeverities.Ok, Single(result, "EMD_MSE_EXEMPT").Severity);
        Assert.DoesNotContain(result.Findings, x => x.Code == "EMD_EXCEEDS_HEADROOM");

        var line = Assert.Single(result.Cost.Lines, l => l.Label.StartsWith("EMD"));
        Assert.True(line.IsExempt);
        Assert.Equal(0m, line.Amount);
    }

    [Fact]
    public void Emd_with_no_headroom_on_file_is_unknown_not_a_gap()
    {
        var result = Evaluate(TenderWith(emd: 500_000m), ProfileWith(turnoverFy1: 1m));

        Assert.Equal(FitSeverities.Unknown, Single(result, "EMD_HEADROOM_UNKNOWN").Severity);
    }

    // ── Verdict resolution ──────────────────────────────────────────────────

    [Fact]
    public void An_empty_profile_yields_insufficient_data_never_go()
    {
        // The most important test here. "No blockers found" against an empty profile means
        // "we didn't look", and rendering that as a green light is the exact failure the whole
        // design exists to prevent.
        var result = Evaluate(TenderWith(), profile: null);

        Assert.Equal(FitVerdicts.InsufficientData, result.Verdict);
        Assert.NotEqual(FitVerdicts.Go, result.Verdict);
    }

    [Fact]
    public void A_blocker_outranks_a_thin_profile()
    {
        // A blocker was found by comparing against something the org DID tell us, so it stands
        // on its own evidence and must not be softened into "we don't know".
        var result = Evaluate(
            TenderWith(turnoverRequired: 20_000_000m),
            ProfileWith(turnoverFy1: 1_000m));

        Assert.Equal(FitVerdicts.Blocked, result.Verdict);
    }

    [Fact]
    public void Blockers_are_listed_before_everything_else()
    {
        var profile = ProfileWith(turnoverFy1: 20_000_000m);
        profile.EmdHeadroom = 1m;

        var result = Evaluate(
            TenderWith(turnoverRequired: 5_000_000m, emd: 500_000m, certifications: ["ISO 27001:2022"]),
            profile);

        Assert.Equal(FitSeverities.Blocker, result.Findings[0].Severity);
    }

    [Fact]
    public void A_closed_tender_blocks()
    {
        var tender = TenderWith() with { ClosingDate = Today.AddDays(-1) };
        var result = Evaluate(tender, ProfileWith(turnoverFy1: 1m));

        Assert.Equal(FitSeverities.Blocker, Single(result, "TENDER_CLOSED").Severity);
    }

    [Fact]
    public void Every_finding_declares_a_source_and_a_confidence()
    {
        // The structural guarantee behind the whole tab: nothing renders without saying where it
        // came from, so a model's guess can never be presented like a subtraction over two known
        // numbers. This is what the previous version of the page got wrong.
        var profile = ProfileWith(turnoverFy1: 20_000_000m);
        profile.EmdHeadroom = 10_000_000m;
        profile.ServiceableStates = ["Delhi"];

        var result = Evaluate(
            TenderWith(turnoverRequired: 5_000_000m, emd: 500_000m, certifications: ["ISO 9001:2015"]),
            profile,
            [Credential("ISO 9001", validUntil: Close.AddDays(60))]);

        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Source));
            Assert.False(string.IsNullOrWhiteSpace(x.Confidence));
            Assert.False(string.IsNullOrWhiteSpace(x.Code));
            Assert.False(string.IsNullOrWhiteSpace(x.Detail));
        });
    }

    [Fact]
    public void The_rule_set_version_is_stamped_on_every_result()
    {
        var result = Evaluate(TenderWith(), ProfileWith(turnoverFy1: 1m));
        Assert.Equal(TenderFitRules.Version, result.RuleSetVersion);
    }

    // ── Cost to bid ─────────────────────────────────────────────────────────

    [Fact]
    public void Emd_is_counted_as_blocked_capital_not_as_spend()
    {
        var result = Evaluate(TenderWith(emd: 500_000m), ProfileWith(turnoverFy1: 1m));

        Assert.Equal(500_000m, result.Cost.BlockedNow);
        Assert.Equal(0m, result.Cost.CommittedOnWin);
    }

    [Fact]
    public void Epbg_is_committed_on_win_and_computed_from_the_stated_percentage()
    {
        var tender = TenderWith(tenderValue: 10_000_000m, epbgPercent: 5);
        var result = Evaluate(tender, ProfileWith(turnoverFy1: 1m));

        Assert.Equal(500_000m, result.Cost.CommittedOnWin);
        Assert.Equal(0m, result.Cost.BlockedNow);
    }

    [Fact]
    public void Nothing_is_invented_when_the_tender_states_no_money()
    {
        // No EMD, no ePBG → no cost lines at all. A "typical document preparation: ₹15,000"
        // line would be a fabricated number sitting beside real ones with no way to tell them
        // apart, which is precisely what this feature is a correction of.
        var result = Evaluate(TenderWith(), ProfileWith(turnoverFy1: 1m));

        Assert.False(result.Cost.HasAnything);
        Assert.Empty(result.Cost.Lines);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static FitResult Evaluate(
        TenderDetailDto tender,
        OrgCapabilityProfile? profile,
        IReadOnlyList<OrgCredential>? credentials = null) =>
        TenderFitRules.Evaluate(tender, profile, credentials ?? [], OrgBidHistory.Empty, Today);

    private static FitFinding Single(FitResult result, string code) =>
        Assert.Single(result.Findings, x => x.Code == code);

    private static OrgCapabilityProfile ProfileWith(decimal? turnoverFy1 = null) =>
        new() { OrgId = Guid.NewGuid(), TurnoverFy1 = turnoverFy1 };

    private static OrgCredential Credential(string code, DateOnly? validUntil = null) =>
        new() { Id = Guid.NewGuid(), OrgId = Guid.NewGuid(), Kind = CredentialKinds.Certification,
                Code = code, ValidUntil = validUntil };

    private static TenderDetailDto TenderWith(
        decimal? turnoverRequired = null,
        decimal? emd = null,
        decimal? tenderValue = null,
        double? epbgPercent = null,
        string[]? certifications = null,
        bool mseRelaxation = false,
        bool startupRelaxation = false)
    {
        var financial = new TenderFinancialDto(
            EstimatedBidValue:   tenderValue,
            Emd:                 emd is null ? null : new TenderEmdDto(true, emd, "SBI"),
            Epbg:                epbgPercent is null ? null : new TenderEpbgDto(true, epbgPercent, 12, null),
            TurnoverRequirement: turnoverRequired is null ? null : new TenderTurnoverDto(turnoverRequired, null));

        var qualification = new TenderQualificationDto(
            ExperienceYears: null, PastPerformancePercentage: null,
            StartupRelaxation: startupRelaxation, MseRelaxation: mseRelaxation,
            RequiredDocuments: null, Certifications: certifications);

        return new TenderDetailDto(
            Id: Guid.NewGuid(), GemTenderId: "GEM/2026/B/123456",
            Title: "Supply of desktop computers", Description: null,
            BuyerOrgName: "MeitY", BuyerOrgIdGem: null,
            State: "Delhi", City: "New Delhi",
            Category: "IT & Software Services", SubCategory: null,
            TenderValue: tenderValue, EmdAmount: emd,
            PublishedDate: new DateOnly(2026, 8, 1), ClosingDate: Close,
            DeliveryDays: 90, Status: "open", CorrigendumCount: 0,
            AiScore: null, EligibilityScore: null, WinProbability: null, RiskScore: null,
            AiSummary: null, AiTags: null,
            CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow,
            Documents: [], OrgSettings: null, AiAnalysis: null,
            Financial: financial, Qualification: qualification,
            Commercial: null, Compliance: null, Items: [],
            Ministry: null, Department: null, Office: null,
            BuyerName: null, BuyerDesignation: null,
            SourceDocuments: [], Timeline: null);
    }
}
