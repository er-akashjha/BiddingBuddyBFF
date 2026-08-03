using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Compliance;

/// <summary>
/// Validates a tender draft against the statutory instruments that govern Indian public
/// procurement, plus the internal-consistency rules a notice has to satisfy to be publishable.
///
/// <para><b>This is the wedge.</b> Compliance checking is manual today and it is audit-fatal:
/// departments fear a CAG paragraph more than they want speed. Encoding the rules as blocking
/// validations, each carrying the citation of the instrument it comes from, means the department's
/// file is audit-proof by construction rather than by somebody remembering.</para>
///
/// <para><b>Why every finding carries a citation.</b> An auditor does not want our opinion that a
/// field is wrong — they want the authority. A finding that says "MSE reservation below 25%" is an
/// argument; one that says it and cites the Public Procurement Policy for MSEs Order 2012 is a
/// reference. Citations are also what makes a rule reviewable by someone who knows procurement law
/// and not this codebase.</para>
///
/// <para><b>Why the rule set is versioned.</b> The rules change. An engine that silently evaluates a
/// two-year-old tender under today's thresholds produces a confidently wrong audit answer, which is
/// worse than producing none — so <see cref="Version"/> is stamped onto every published version and
/// a historical version is always re-evaluated under the set it was published beneath. Bump it
/// whenever a threshold or citation below changes, and never edit a rule in place without bumping.
/// </para>
/// </summary>
public static class TenderComplianceRules
{
    /// <summary>
    /// Rule set identity. Format <c>YYYY.MM.N</c>. Pinned into <c>tender_versions.rule_set_version</c>
    /// at publish time. BUMP THIS whenever any threshold, severity or citation below changes.
    /// </summary>
    public const string Version = "2026.07.1";

    // Citations, named once so a rule and its authority cannot drift apart.
    private const string CiteMse   = "Public Procurement Policy for Micro & Small Enterprises Order 2012, paras 3–4";
    private const string CiteMii   = "Public Procurement (Preference to Make in India) Order — DPIIT PPP-MII";
    private const string CiteLbs   = "GFR 2017 Rule 144(xi); DoE Order dated 23-07-2020 (land border sharing)";
    private const string CiteGfr159 = "GFR 2017 Rule 159 — publication of tender information";
    private const string CiteGfr160 = "GFR 2017 Rule 160 — receipt of bids through e-procurement";
    private const string CiteGfr149 = "GFR 2017 Rule 149 — GeM mandatory for catalogued common-use goods";
    private const string CiteCvc    = "CVC guidance on tender transparency and minimum bidding time";
    private const string CiteIntegrity = "CVC Office Order on Integrity Pact — Independent External Monitor";
    private const string CiteStartup = "DIPP Notification No. F.No.21(3)/2016-PP (startup relaxations)";
    private const string CiteInternal = "Tenders Agent publishing requirement";

    /// <summary>
    /// Minimum bidding window. CVC guidance sets three weeks for open national tenders; below that
    /// a tender invites a challenge that it was tailored to whoever already knew about it.
    /// </summary>
    private const int MinOpenTenderNoticeDays = 21;

    /// <summary>Limited/single-source procurement gets a shorter floor — the bidder set is known.</summary>
    private const int MinLimitedTenderNoticeDays = 7;

    /// <summary>Re-notice after a material amendment. An extension shorter than this leaves bidders
    /// who already submitted no real chance to revise.</summary>
    public const int MinCorrigendumNoticeDays = 3;

    /// <summary>MSE reservation floor under the 2012 Order.</summary>
    private const decimal MseReservationFloorPct = 25m;

    /// <summary>
    /// Run every rule. <paramref name="canonicalCategories"/> and <paramref name="canonicalStates"/>
    /// are injected rather than referenced directly because the canonical taxonomy is owned by
    /// BiddingBuddyServices and this project must not take a dependency on it.
    /// </summary>
    public static IReadOnlyList<ComplianceFindingDto> Evaluate(
        TenderDraft d,
        TenderDraftDetail detail,
        IReadOnlySet<string> canonicalCategories,
        IReadOnlySet<string> canonicalStates,
        bool selfPublishing,
        DateTime nowUtc)
    {
        var f = new List<ComplianceFindingDto>();

        Required(f, d.Title, "title", "A tender must have a title.");
        Required(f, d.Description, "description", "Describe what is being procured.");
        Required(f, d.TenderType, "tenderType", "Select a tender type (open, limited, global, EOI…).");
        Required(f, d.ProcurementCategory, "procurementCategory", "Select goods, works, services or consultancy.");
        Required(f, d.BiddingSystem, "biddingSystem", "Select a bidding system (single/two/three cover).");
        Required(f, d.EvaluationMethod, "evaluationMethod", "State how bids will be evaluated.");

        EvaluateTaxonomy(f, d, canonicalCategories, canonicalStates);
        EvaluateDates(f, d, nowUtc);
        EvaluateFinancials(f, d);
        EvaluateEvaluationMethod(f, d);
        EvaluateMse(f, d);
        EvaluateMakeInIndia(f, d);
        EvaluateLandBorderSharing(f, d);
        EvaluateIntegrityPact(f, d);
        EvaluateStartup(f, d);
        EvaluateGeMJustification(f, d);
        EvaluateDetail(f, d, detail);
        EvaluateSeparationOfDuties(f, selfPublishing);

        return f;
    }

    // ── Taxonomy — the rule that decides whether the tender reaches anyone ───

    private static void EvaluateTaxonomy(
        List<ComplianceFindingDto> f, TenderDraft d,
        IReadOnlySet<string> categories, IReadOnlySet<string> states)
    {
        // This is an ERROR and not a warning on purpose, and it is worth being explicit about why.
        //
        // BiddingBuddyServices rewrites an off-taxonomy category to the fallback silently on write,
        // and supplier alert matching is an EXACT string match on the category. So a free-text
        // category does not merely weaken the tender's reach — the notice matches nobody, forever,
        // and nothing anywhere reports it. The department would see a published tender and no bids.
        //
        // Rejecting here is the only place in the whole chain where that is visible.
        if (string.IsNullOrWhiteSpace(d.Category))
        {
            f.Add(Error("CATEGORY_REQUIRED", "category",
                "Select a category. Suppliers are alerted by exact category match — a tender with none reaches nobody.",
                CiteInternal));
        }
        else if (!categories.Contains(d.Category))
        {
            f.Add(Error("CATEGORY_NOT_CANONICAL", "category",
                $"'{d.Category}' is not a recognised category. Pick one from the list — a free-text value is silently "
                + "rewritten downstream and the tender would then match no supplier interests at all.",
                CiteInternal));
        }

        if (string.IsNullOrWhiteSpace(d.State))
        {
            f.Add(Error("STATE_REQUIRED", "state",
                "Select the state this procurement is for. Suppliers filter by it.", CiteInternal));
        }
        else if (!states.Contains(d.State))
        {
            f.Add(Error("STATE_NOT_CANONICAL", "state",
                $"'{d.State}' is not a recognised state or union territory. Pick one from the list.", CiteInternal));
        }
    }

    // ── Critical dates ──────────────────────────────────────────────────────

    private static void EvaluateDates(List<ComplianceFindingDto> f, TenderDraft d, DateTime now)
    {
        if (d.BidSubmissionEnd is null)
        {
            f.Add(Error("SUBMISSION_END_REQUIRED", "bidSubmissionEnd",
                "A bid submission deadline is required.", CiteGfr159));
            return;
        }

        Order(f, d.DocDownloadStart, d.DocDownloadEnd,
            "docDownloadEnd", "Document download must end after it starts.");
        Order(f, d.ClarificationStart, d.ClarificationEnd,
            "clarificationEnd", "The clarification window must end after it starts.");
        Order(f, d.BidSubmissionStart, d.BidSubmissionEnd,
            "bidSubmissionEnd", "Bid submission must close after it opens.");

        if (d.ClarificationEnd is { } clarEnd && clarEnd > d.BidSubmissionEnd)
            f.Add(Error("CLARIFICATION_AFTER_SUBMISSION", "clarificationEnd",
                "Clarifications must close before bid submission closes — otherwise an answer can arrive "
                + "after bidders can no longer act on it.", CiteCvc));

        if (d.PrebidMeetingAt is { } prebid && prebid > d.BidSubmissionEnd)
            f.Add(Error("PREBID_AFTER_SUBMISSION", "prebidMeetingAt",
                "The pre-bid meeting must happen before bid submission closes.", CiteCvc));

        if (d.TechnicalOpeningAt is { } tech && tech < d.BidSubmissionEnd)
            f.Add(Error("OPENING_BEFORE_SUBMISSION", "technicalOpeningAt",
                "Technical bids cannot be opened before submission closes.", CiteGfr160));

        if (d.FinancialOpeningAt is { } fin && d.TechnicalOpeningAt is { } tech2 && fin < tech2)
            f.Add(Error("FINANCIAL_BEFORE_TECHNICAL", "financialOpeningAt",
                "Financial bids are opened after technical evaluation, not before.", CiteGfr160));

        if (d.BidSubmissionEnd <= now)
            f.Add(Error("SUBMISSION_END_IN_PAST", "bidSubmissionEnd",
                "The submission deadline is in the past.", CiteInternal));

        // Minimum notice period, scaled by procurement mode.
        var start = d.BidSubmissionStart ?? d.PublishedAt ?? now;
        var window = (d.BidSubmissionEnd.Value - start).TotalDays;
        var required = d.TenderType is "limited" or "single" or "rfq"
            ? MinLimitedTenderNoticeDays
            : MinOpenTenderNoticeDays;

        if (window < required)
            f.Add(Warning("NOTICE_PERIOD_SHORT", "bidSubmissionEnd",
                $"Bidders get {window:F0} days. {required} is the expected minimum for a {d.TenderType ?? "tender"} "
                + "tender; a shorter window is a standard audit objection and invites a challenge that the "
                + "tender was tailored to a bidder who already knew about it.", CiteCvc));

        if (d.BidValidityDays is { } validity && validity > 0 && validity < 30)
            f.Add(Warning("BID_VALIDITY_SHORT", "bidValidityDays",
                $"A {validity}-day bid validity rarely survives evaluation and approval.", CiteInternal));
    }

    private static void Order(
        List<ComplianceFindingDto> f, DateTime? start, DateTime? end, string field, string message)
    {
        if (start is { } s && end is { } e && e <= s)
            f.Add(Error("DATE_ORDER", field, message, CiteInternal));
    }

    // ── Money ───────────────────────────────────────────────────────────────

    private static void EvaluateFinancials(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (d.EmdAmount is { } amt && amt < 0)
            f.Add(Error("EMD_NEGATIVE", "emdAmount", "EMD cannot be negative.", CiteInternal));

        if (d.EmdAmount is { } a && d.EmdPercentage is { } p && a > 0 && p > 0)
            f.Add(Warning("EMD_DOUBLE_SPECIFIED", "emdAmount",
                "EMD is given both as an amount and as a percentage. Bidders will not know which governs.",
                CiteInternal));

        // MSE and startup EMD exemption is a statutory entitlement, not a courtesy. Departments
        // routinely omit it and then have to answer for it.
        if ((d.EmdAmount > 0 || d.EmdPercentage > 0) && d.EmdExemptions.Length == 0)
            f.Add(Warning("EMD_NO_EXEMPTIONS", "emdExemptions",
                "No EMD exemption is recorded. MSEs and recognised startups are entitled to one; if this "
                + "procurement genuinely allows none, record why in the notice.", CiteMse));

        if (d.EstimatedValue is { } v && v <= 0)
            f.Add(Error("ESTIMATED_VALUE_INVALID", "estimatedValue",
                "The estimated value must be greater than zero.", CiteInternal));

        if (d.PerformanceSecurityPct is { } ps && ps > 10)
            f.Add(Warning("PERFORMANCE_SECURITY_HIGH", "performanceSecurityPct",
                $"{ps}% performance security is above the 5–10% norm and narrows the bidder pool.", CiteInternal));
    }

    private static void EvaluateEvaluationMethod(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (d.EvaluationMethod == "qcbs")
        {
            if (d.TechnicalWeightage is null)
                f.Add(Error("QCBS_WEIGHTAGE_REQUIRED", "technicalWeightage",
                    "QCBS requires a technical weightage — bidders must know how quality and price are traded off.",
                    CiteGfr160));
            else if (d.TechnicalWeightage is < 0 or > 100)
                f.Add(Error("QCBS_WEIGHTAGE_RANGE", "technicalWeightage",
                    "Technical weightage must be between 0 and 100.", CiteInternal));
        }

        if (d.EvaluationMethod == "single_source" && string.IsNullOrWhiteSpace(d.GfrRuleCited))
            f.Add(Error("SINGLE_SOURCE_NEEDS_RULE", "gfrRuleCited",
                "Single-source procurement must cite the rule that permits it. This is the first thing an "
                + "auditor asks for.", CiteGfr160));

        // A three-cover system with a two-cover mental model is a common and expensive mix-up.
        if (d.BiddingSystem == "single_cover" && d.FinancialOpeningAt is not null)
            f.Add(Warning("SINGLE_COVER_FINANCIAL_OPENING", "financialOpeningAt",
                "A single-cover tender has no separate financial opening.", CiteInternal));
    }

    // ── Statutory instruments ───────────────────────────────────────────────

    private static void EvaluateMse(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (d.ProcurementCategory is not ("goods" or "services")) return;

        if (d.MseReservationPct is null)
        {
            f.Add(Warning("MSE_RESERVATION_UNSET", "mseReservationPct",
                "MSE reservation is not stated. The 2012 Order reserves 25% of annual procurement of goods "
                + "and services for micro and small enterprises, within which 4% is for SC/ST-owned and 3% for "
                + "women-owned MSEs.", CiteMse));
        }
        else if (d.MseReservationPct < MseReservationFloorPct)
        {
            f.Add(Warning("MSE_RESERVATION_BELOW_FLOOR", "mseReservationPct",
                $"{d.MseReservationPct}% is below the {MseReservationFloorPct}% reservation. That can be correct for a "
                + "single tender — the target is annual, not per-tender — but record the justification, because it "
                + "is the number an auditor will sample.", CiteMse));
        }
    }

    private static void EvaluateMakeInIndia(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (!d.MiiApplicable)
        {
            if (d.ProcurementCategory == "goods")
                f.Add(Info("MII_NOT_APPLIED", "miiApplicable",
                    "Make in India preference is not applied. For goods procurement, confirm this is deliberate.",
                    CiteMii));
            return;
        }

        if (d.MiiLocalContentPct is null)
            f.Add(Error("MII_LOCAL_CONTENT_REQUIRED", "miiLocalContentPct",
                "Make in India is applied but no minimum local content is stated. Without it, Class-I and Class-II "
                + "supplier status cannot be determined and the preference is unenforceable.", CiteMii));
        else if (d.MiiLocalContentPct is < 0 or > 100)
            f.Add(Error("MII_LOCAL_CONTENT_RANGE", "miiLocalContentPct",
                "Local content must be between 0 and 100 percent.", CiteInternal));

        if (d.MiiClassRestriction == "class_i_only" && d.TenderType == "global")
            f.Add(Error("MII_GLOBAL_CONFLICT", "miiClassRestriction",
                "A global tender cannot simultaneously be restricted to Class-I local suppliers.", CiteMii));
    }

    private static void EvaluateLandBorderSharing(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (!d.LbsDeclarationRequired)
            f.Add(Warning("LBS_DECLARATION_MISSING", "lbsDeclarationRequired",
                "No land-border-sharing declaration is required of bidders. Rule 144(xi) requires bidders from "
                + "countries sharing a land border with India to be registered with the Competent Authority, and "
                + "the declaration is how that is established.", CiteLbs));
    }

    private static void EvaluateIntegrityPact(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (!d.IntegrityPactApplicable) return;

        if (string.IsNullOrWhiteSpace(d.IntegrityPactMonitor))
            f.Add(Error("IEM_REQUIRED", "integrityPactMonitor",
                "An Integrity Pact requires a named Independent External Monitor. A pact with no monitor is "
                + "not a pact.", CiteIntegrity));
    }

    private static void EvaluateStartup(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (d.StartupRelaxation) return;

        f.Add(Info("STARTUP_RELAXATION_NOT_APPLIED", "startupRelaxation",
            "Prior turnover and experience requirements are not relaxed for recognised startups.", CiteStartup));
    }

    private static void EvaluateGeMJustification(List<ComplianceFindingDto> f, TenderDraft d)
    {
        if (d.ProcurementCategory is not ("goods" or "services")) return;

        if (string.IsNullOrWhiteSpace(d.GemarptsReference))
            f.Add(Warning("GEMARPTS_MISSING", "gemarptsReference",
                "No GeM Availability Report reference. Procuring common-use goods or services outside GeM requires "
                + "a GeMARPTS report showing the item was not available there — this is the document that "
                + "justifies running this tender at all.", CiteGfr149));
    }

    // ── The JSONB long tail ─────────────────────────────────────────────────

    private static void EvaluateDetail(List<ComplianceFindingDto> f, TenderDraft d, TenderDraftDetail detail)
    {
        if (detail.Items.Length == 0)
            f.Add(Warning("NO_LINE_ITEMS", "items",
                "No bill of quantities. Bidders will price from prose, and comparing the results is guesswork.",
                CiteInternal));

        foreach (var item in detail.Items.Where(i => i.Quantity <= 0))
            f.Add(Error("ITEM_QUANTITY_INVALID", "items",
                $"Line item '{item.Description}' has a quantity of {item.Quantity}.", CiteInternal));

        // Brand restriction without justification is the single most audit-sensitive specification
        // choice a procuring officer can make, so this one is an error rather than a warning.
        foreach (var brand in detail.BrandRestrictions.Where(b => string.IsNullOrWhiteSpace(b.Justification)))
            f.Add(Error("BRAND_RESTRICTION_UNJUSTIFIED", "brandRestrictions",
                $"Restricting to '{brand.Make}' needs a written justification recorded in the file.", CiteCvc));

        if (detail.Attachments.Length == 0)
            f.Add(Warning("NO_DOCUMENTS", "attachments",
                "No documents attached. A notice with no NIT or tender document forces every interested "
                + "supplier to call the department for it.", CiteGfr159));

        if (detail.Contact is null || string.IsNullOrWhiteSpace(detail.Contact.Email))
            f.Add(Error("CONTACT_REQUIRED", "contact",
                "Name the tender inviting authority and give an email. Bidders must have somewhere to send a "
                + "clarification.", CiteGfr159));

        var covers = detail.Covers.Length;
        var expected = d.BiddingSystem switch
        {
            "single_cover" => 1,
            "two_cover"    => 2,
            "three_cover"  => 3,
            _              => (int?)null,
        };
        if (expected is { } n && covers > 0 && covers != n)
            f.Add(Error("COVER_COUNT_MISMATCH", "covers",
                $"The bidding system says {n} cover(s) but {covers} are defined.", CiteInternal));
    }

    private static void EvaluateSeparationOfDuties(List<ComplianceFindingDto> f, bool selfPublishing)
    {
        // Deliberately a WARNING, not an error.
        //
        // GePNIC separates creation from publication across two credentials, and that separation is
        // a genuine control. But a small ULB or a university department may have exactly one
        // procurement officer, and hard-enforcing four-eyes would mean they can never publish
        // anything — so the control is made VISIBLE instead of mandatory. The audit trail records
        // both actors either way, so an inspector can see when it did not hold.
        if (selfPublishing)
            f.Add(Warning("SELF_PUBLISHED", "publishedBy",
                "The same officer created and published this tender. Where staffing allows, publication by a "
                + "second officer is the control that makes the separation of duties real.", CiteCvc));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void Required(List<ComplianceFindingDto> f, string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            f.Add(Error("REQUIRED", field, message, CiteInternal));
    }

    private static ComplianceFindingDto Error(string code, string field, string msg, string cite)
        => new(code, "error", field, msg, cite);

    private static ComplianceFindingDto Warning(string code, string field, string msg, string cite)
        => new(code, "warning", field, msg, cite);

    private static ComplianceFindingDto Info(string code, string field, string msg, string cite)
        => new(code, "info", field, msg, cite);
}
