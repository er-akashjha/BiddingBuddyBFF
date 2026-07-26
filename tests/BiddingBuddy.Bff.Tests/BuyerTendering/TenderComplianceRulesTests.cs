using BiddingBuddy.Bff.Core.Compliance;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using Xunit;

namespace BiddingBuddy.Bff.Tests.BuyerTendering;

/// <summary>
/// The compliance engine is the product's wedge: it is what makes a department's file audit-proof
/// by construction instead of by somebody remembering. These tests pin the severities as much as
/// the rules, because severity is the design — an error blocks publication and a warning does not,
/// and getting that boundary wrong either strands a department that cannot publish anything or
/// waves through the one mistake that matters.
/// </summary>
public sealed class TenderComplianceRulesTests
{
    private static readonly DateTime Now = new(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc);

    private static readonly HashSet<string> Categories =
        new(["Computers & IT Hardware", "Civil Works & Infrastructure"], StringComparer.Ordinal);

    private static readonly HashSet<string> States =
        new(["Kerala", "Maharashtra"], StringComparer.Ordinal);

    /// <summary>A draft that passes every rule, so each test can break exactly one thing.</summary>
    private static TenderDraft Valid() => new()
    {
        Title = "Supply of desktop computers",
        Description = "Supply and installation of 200 desktop computers",
        ScopeOfWork = "Supply, install, commission",
        TenderType = "open",
        ProcurementCategory = "goods",
        BiddingSystem = "two_cover",
        EvaluationMethod = "l1",
        Category = "Computers & IT Hardware",
        State = "Kerala",
        EstimatedValue = 5_000_000m,
        EmdAmount = 100_000m,
        EmdExemptions = ["mse", "startup"],
        BidValidityDays = 90,
        BidSubmissionStart = Now,
        BidSubmissionEnd = Now.AddDays(30),
        ClarificationEnd = Now.AddDays(10),
        TechnicalOpeningAt = Now.AddDays(31),
        FinancialOpeningAt = Now.AddDays(35),
        MseReservationPct = 25m,
        MiiApplicable = true,
        MiiLocalContentPct = 50m,
        LbsDeclarationRequired = true,
        StartupRelaxation = true,
        GemarptsReference = "GEMARPTS/2026/00123",
    };

    private static TenderDraftDetail ValidDetail() => new()
    {
        Covers =
        [
            new CoverDto(1, "Technical", []),
            new CoverDto(2, "Financial", []),
        ],
        Items = [new BoqItemDto("IT-001", "Desktop computer", "nos", 200, 25_000m, 5_000_000m)],
        Attachments = [new TenderAttachmentDto("a1", "nit", "nit.pdf", "orgs/x/nit.pdf", 1024, Now)],
        Contact = new TenderContactDto("R. Menon", "Executive Engineer", "ee@dept.gov.in", "0471-000000", "Trivandrum"),
    };

    private static IReadOnlyList<ComplianceFindingDto> Run(
        TenderDraft? draft = null, TenderDraftDetail? detail = null, bool selfPublishing = false)
        => TenderComplianceRules.Evaluate(
            draft ?? Valid(), detail ?? ValidDetail(), Categories, States, selfPublishing, Now);

    private static bool HasError(IReadOnlyList<ComplianceFindingDto> f, string code)
        => f.Any(x => x.Code == code && x.Severity == "error");

    private static bool HasWarning(IReadOnlyList<ComplianceFindingDto> f, string code)
        => f.Any(x => x.Code == code && x.Severity == "warning");

    // ── The happy path ──────────────────────────────────────────────────────

    [Fact]
    public void A_complete_tender_produces_no_errors()
    {
        var findings = Run();

        Assert.DoesNotContain(findings, f => f.Severity == "error");
    }

    [Fact]
    public void Every_finding_carries_a_citation()
    {
        // A finding without its authority is our opinion, not a reference — and the citation is
        // what the department's file records so an auditor reads the rule rather than our message.
        var findings = Run(new TenderDraft(), new TenderDraftDetail());

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Citation)));
    }

    // ── Taxonomy: the rule that decides whether the tender reaches anyone ────

    [Fact]
    public void An_off_taxonomy_category_is_an_ERROR_not_a_warning()
    {
        // The severity is the point. Downstream this value is silently rewritten, and supplier
        // alert matching is an exact string match — so a free-text category publishes a tender that
        // matches nobody, forever, with nothing anywhere reporting it. This validation is the only
        // place in the entire chain where that is visible.
        var draft = Valid();
        draft.Category = "Laptops and stuff";

        Assert.True(HasError(Run(draft), "CATEGORY_NOT_CANONICAL"));
    }

    [Fact]
    public void A_missing_category_is_an_error()
    {
        var draft = Valid();
        draft.Category = null;

        Assert.True(HasError(Run(draft), "CATEGORY_REQUIRED"));
    }

    [Fact]
    public void An_off_taxonomy_state_is_an_error()
    {
        var draft = Valid();
        draft.State = "Travancore";

        Assert.True(HasError(Run(draft), "STATE_NOT_CANONICAL"));
    }

    // ── Critical dates ──────────────────────────────────────────────────────

    [Fact]
    public void Bid_submission_must_close_after_it_opens()
    {
        var draft = Valid();
        draft.BidSubmissionStart = Now.AddDays(30);
        draft.BidSubmissionEnd = Now.AddDays(10);

        Assert.True(HasError(Run(draft), "DATE_ORDER"));
    }

    [Fact]
    public void Bids_cannot_be_opened_before_submission_closes()
    {
        var draft = Valid();
        draft.TechnicalOpeningAt = draft.BidSubmissionEnd!.Value.AddDays(-1);

        Assert.True(HasError(Run(draft), "OPENING_BEFORE_SUBMISSION"));
    }

    [Fact]
    public void Financial_opening_cannot_precede_technical_opening()
    {
        var draft = Valid();
        draft.FinancialOpeningAt = draft.TechnicalOpeningAt!.Value.AddDays(-1);

        Assert.True(HasError(Run(draft), "FINANCIAL_BEFORE_TECHNICAL"));
    }

    [Fact]
    public void Clarifications_must_close_before_submission_closes()
    {
        // Otherwise an answer can arrive after bidders can no longer act on it.
        var draft = Valid();
        draft.ClarificationEnd = draft.BidSubmissionEnd!.Value.AddDays(1);

        Assert.True(HasError(Run(draft), "CLARIFICATION_AFTER_SUBMISSION"));
    }

    [Fact]
    public void A_deadline_in_the_past_is_an_error()
    {
        var draft = Valid();
        draft.BidSubmissionStart = Now.AddDays(-40);
        draft.BidSubmissionEnd = Now.AddDays(-1);
        draft.ClarificationEnd = Now.AddDays(-10);
        draft.TechnicalOpeningAt = Now.AddDays(1);

        Assert.True(HasError(Run(draft), "SUBMISSION_END_IN_PAST"));
    }

    [Fact]
    public void A_short_notice_period_warns_but_does_not_block()
    {
        // Departments do run genuinely urgent procurements. The objection is recorded and
        // acknowledged rather than made impossible.
        var draft = Valid();
        draft.BidSubmissionEnd = Now.AddDays(5);
        draft.ClarificationEnd = Now.AddDays(2);
        draft.TechnicalOpeningAt = Now.AddDays(6);
        draft.FinancialOpeningAt = Now.AddDays(7);

        var findings = Run(draft);

        Assert.True(HasWarning(findings, "NOTICE_PERIOD_SHORT"));
        Assert.False(HasError(findings, "NOTICE_PERIOD_SHORT"));
    }

    [Fact]
    public void A_limited_tender_gets_a_shorter_notice_floor_than_an_open_one()
    {
        var draft = Valid();
        draft.TenderType = "limited";
        draft.BidSubmissionEnd = Now.AddDays(10);
        draft.ClarificationEnd = Now.AddDays(5);
        draft.TechnicalOpeningAt = Now.AddDays(11);
        draft.FinancialOpeningAt = Now.AddDays(12);

        // 10 days clears the 7-day limited floor but would breach the 21-day open one.
        Assert.False(HasWarning(Run(draft), "NOTICE_PERIOD_SHORT"));
    }

    // ── Evaluation method ───────────────────────────────────────────────────

    [Fact]
    public void QCBS_without_a_technical_weightage_is_an_error()
    {
        var draft = Valid();
        draft.EvaluationMethod = "qcbs";
        draft.TechnicalWeightage = null;

        Assert.True(HasError(Run(draft), "QCBS_WEIGHTAGE_REQUIRED"));
    }

    [Fact]
    public void Single_source_must_cite_the_rule_that_permits_it()
    {
        var draft = Valid();
        draft.EvaluationMethod = "single_source";
        draft.GfrRuleCited = null;

        Assert.True(HasError(Run(draft), "SINGLE_SOURCE_NEEDS_RULE"));
    }

    // ── Statutory instruments ───────────────────────────────────────────────

    [Fact]
    public void MSE_reservation_below_the_floor_warns_rather_than_blocks()
    {
        // The 25% target is annual, not per-tender, so a single tender below it can be correct —
        // it just needs to be a recorded decision.
        var draft = Valid();
        draft.MseReservationPct = 10m;

        var findings = Run(draft);

        Assert.True(HasWarning(findings, "MSE_RESERVATION_BELOW_FLOOR"));
        Assert.DoesNotContain(findings, f => f.Code == "MSE_RESERVATION_BELOW_FLOOR" && f.Severity == "error");
    }

    [Fact]
    public void Make_in_India_without_a_local_content_percentage_is_an_error()
    {
        // Without it, Class-I and Class-II supplier status cannot be determined and the preference
        // is unenforceable — the tender says a thing it cannot apply.
        var draft = Valid();
        draft.MiiApplicable = true;
        draft.MiiLocalContentPct = null;

        Assert.True(HasError(Run(draft), "MII_LOCAL_CONTENT_REQUIRED"));
    }

    [Fact]
    public void A_global_tender_cannot_also_be_restricted_to_Class_I_local_suppliers()
    {
        var draft = Valid();
        draft.TenderType = "global";
        draft.MiiClassRestriction = "class_i_only";

        Assert.True(HasError(Run(draft), "MII_GLOBAL_CONFLICT"));
    }

    [Fact]
    public void An_Integrity_Pact_without_a_monitor_is_an_error()
    {
        var draft = Valid();
        draft.IntegrityPactApplicable = true;
        draft.IntegrityPactMonitor = null;

        Assert.True(HasError(Run(draft), "IEM_REQUIRED"));
    }

    [Fact]
    public void A_missing_land_border_declaration_warns()
    {
        var draft = Valid();
        draft.LbsDeclarationRequired = false;

        Assert.True(HasWarning(Run(draft), "LBS_DECLARATION_MISSING"));
    }

    [Fact]
    public void Procuring_goods_without_a_GeMARPTS_reference_warns()
    {
        // The report justifying going off GeM is the document that legitimises running the tender
        // at all — but it is a warning because the department may genuinely be outside Rule 149.
        var draft = Valid();
        draft.GemarptsReference = null;

        Assert.True(HasWarning(Run(draft), "GEMARPTS_MISSING"));
    }

    // ── The detail blob ─────────────────────────────────────────────────────

    [Fact]
    public void A_brand_restriction_without_justification_is_an_ERROR()
    {
        // Restricting a government tender to a brand is the most audit-sensitive specification
        // choice there is, so this one blocks rather than warns.
        var detail = ValidDetail() with
        {
            BrandRestrictions = [new BrandRestrictionDto("Dell", "OptiPlex", "")],
        };

        Assert.True(HasError(Run(detail: detail), "BRAND_RESTRICTION_UNJUSTIFIED"));
    }

    [Fact]
    public void A_justified_brand_restriction_passes()
    {
        var detail = ValidDetail() with
        {
            BrandRestrictions =
                [new BrandRestrictionDto("Dell", "OptiPlex", "Existing fleet standardisation; spares held in stores")],
        };

        Assert.False(HasError(Run(detail: detail), "BRAND_RESTRICTION_UNJUSTIFIED"));
    }

    [Fact]
    public void The_cover_count_must_match_the_bidding_system()
    {
        var draft = Valid();
        draft.BiddingSystem = "three_cover";   // detail defines only two

        Assert.True(HasError(Run(draft), "COVER_COUNT_MISMATCH"));
    }

    [Fact]
    public void A_missing_contact_is_an_error()
    {
        // Bidders must have somewhere to send a clarification.
        var detail = ValidDetail() with { Contact = null };

        Assert.True(HasError(Run(detail: detail), "CONTACT_REQUIRED"));
    }

    [Fact]
    public void A_line_item_with_no_quantity_is_an_error()
    {
        var detail = ValidDetail() with
        {
            Items = [new BoqItemDto("IT-001", "Desktop computer", "nos", 0, 25_000m, 0m)],
        };

        Assert.True(HasError(Run(detail: detail), "ITEM_QUANTITY_INVALID"));
    }

    // ── Separation of duties ────────────────────────────────────────────────

    [Fact]
    public void Self_publication_warns_but_never_blocks()
    {
        // A one-officer ULB or university department must still be able to publish. The control is
        // made visible rather than mandatory; the audit trail records both actors either way.
        var findings = Run(selfPublishing: true);

        Assert.True(HasWarning(findings, "SELF_PUBLISHED"));
        Assert.DoesNotContain(findings, f => f.Severity == "error");
    }

    [Fact]
    public void Publication_by_a_second_officer_raises_nothing()
    {
        Assert.False(HasWarning(Run(selfPublishing: false), "SELF_PUBLISHED"));
    }

    // ── Rule-set versioning ─────────────────────────────────────────────────

    [Fact]
    public void The_rule_set_version_is_set_and_shaped_like_a_version()
    {
        // Pinned onto every published version so a historical tender is re-evaluated under the
        // rules it was published beneath. An engine that silently applies today's thresholds to a
        // two-year-old tender gives a confidently wrong audit answer, which is worse than none.
        Assert.False(string.IsNullOrWhiteSpace(TenderComplianceRules.Version));
        Assert.Matches(@"^\d{4}\.\d{2}\.\d+$", TenderComplianceRules.Version);
    }
}
