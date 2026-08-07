using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Fit;

/// <summary>
/// Decides whether THIS org can bid THIS tender, and what it will cost them to try.
///
/// <para><b>This is deterministic on purpose.</b> Eligibility is arithmetic — turnover against a
/// threshold, a certificate's expiry against a deadline, an EMD against available capital. Code
/// does that instantly, for free, identically every time, and can be pointed at the two numbers
/// that produced the answer. A model asked the same question produces a fluent paragraph that is
/// right most of the time, which is the worst possible property for a figure someone commits
/// capital against. The LLM's job in this feature is to write prose over a verdict it did not
/// decide, and if the model is unavailable the verdict is unaffected.</para>
///
/// <para><b>Why every finding carries a citation.</b> The relaxations below are not our policy,
/// they are statute. A bidder who reads "you may be exempt from EMD" needs to be able to take
/// the instrument to their finance team, and an engine that asserts an exemption without naming
/// its authority is asking to be trusted rather than checked.</para>
///
/// <para><b>Why the rule set is versioned.</b> Identical reasoning to
/// <c>TenderComplianceRules.Version</c>: thresholds and relaxations change, and re-rendering a
/// stored verdict under a newer set produces a confidently wrong answer. <see cref="Version"/> is
/// pinned onto every report; a mismatch is what makes an old report re-runnable rather than
/// silently restated.</para>
/// </summary>
public static class TenderFitRules
{
    /// <summary>Rule-set identity, <c>YYYY.MM.N</c>. BUMP whenever a threshold, severity,
    /// relaxation or citation below changes. Never edit a rule in place without bumping.</summary>
    public const string Version = "2026.08.1";

    // Citations, named once so a rule and its authority cannot drift apart.
    private const string CiteMse =
        "Public Procurement Policy for Micro & Small Enterprises Order 2012, paras 3–4";
    private const string CiteMseEmd =
        "GeM GTC clause on EMD exemption for MSEs; PP Policy for MSEs Order 2012";
    private const string CiteStartup =
        "DIPP Notification No. F.No.21(3)/2016-PP — relaxation of prior turnover and experience";
    private const string CiteMii =
        "Public Procurement (Preference to Make in India) Order — DPIIT PPP-MII";
    private const string CiteGfr =
        "GFR 2017 Rule 170 — bid security / EMD";

    /// <summary>
    /// Below this many days to close, a gap that needs a third party (a certifying body, an OEM,
    /// a bank) is realistically unfixable. Not a statutory figure — an operational one — so it is
    /// stated as a judgement in the finding text rather than dressed up with a citation.
    /// </summary>
    private const int TightDeadlineDays = 7;

    /// <summary>
    /// Evaluate. Pure: no I/O, no clock of its own — <paramref name="today"/> is passed in so the
    /// result is reproducible and testable, and so "today" is IST rather than whatever the
    /// container thinks.
    /// </summary>
    public static FitResult Evaluate(
        TenderDetailDto tender,
        OrgCapabilityProfile? profile,
        IReadOnlyList<OrgCredential> credentials,
        OrgBidHistory history,
        DateOnly today)
    {
        var f = new List<FitFinding>();
        var ctx = new EvalContext(tender, profile, credentials, history, today);

        EvaluateTurnover(f, ctx);
        EvaluateExperience(f, ctx);
        EvaluateCertifications(f, ctx);
        EvaluateRequiredDocuments(f, ctx);
        EvaluateEmd(f, ctx);
        EvaluateEpbg(f, ctx);
        EvaluateReach(f, ctx);
        EvaluateCategory(f, ctx);
        EvaluateTimeline(f, ctx);
        EvaluateCommercial(f, ctx);
        EvaluatePreferences(f, ctx);

        var cost = BidCostEstimator.Estimate(tender, ctx.IsMse);
        var (verdict, reason) = Decide(f, ctx);

        return new FitResult(verdict, reason, Order(f), cost, Version);
    }

    // ── The verdict ─────────────────────────────────────────────────────────

    private static (string Verdict, string Reason) Decide(List<FitFinding> findings, EvalContext ctx)
    {
        var blockers = findings.Count(x => x.Severity == FitSeverities.Blocker);
        var gaps     = findings.Count(x => x.Severity == FitSeverities.Gap);

        // A blocker is a blocker even on a thin profile — it was found by comparing against
        // something the org DID tell us, so it stands on its own evidence.
        if (blockers > 0)
            return (FitVerdicts.Blocked,
                blockers == 1
                    ? "One requirement disqualifies this bid as things stand."
                    : $"{blockers} requirements disqualify this bid as things stand.");

        // Only now does an empty profile matter: with nothing to compare against, "no blockers
        // found" means "we didn't look", and presenting that as a green light is the exact
        // failure this engine exists to prevent.
        if (!ctx.CanEvaluate)
            return (FitVerdicts.InsufficientData,
                "We don't know enough about your company to judge this tender yet. "
                + "Add your turnover and certificates and this becomes a real answer.");

        if (gaps > 0)
            return (FitVerdicts.GoWithGaps,
                gaps == 1
                    ? "Biddable once one gap is closed."
                    : $"Biddable once {gaps} gaps are closed.");

        return (FitVerdicts.Go, "Nothing we checked stands in the way of this bid.");
    }

    /// <summary>Blockers first, then gaps, then watches, then what passed. Unknowns sit above
    /// the passes: "we couldn't check this" is more actionable than "this is fine".</summary>
    private static IReadOnlyList<FitFinding> Order(List<FitFinding> findings) =>
        findings.OrderBy(x => x.Severity switch
        {
            FitSeverities.Blocker => 0,
            FitSeverities.Gap     => 1,
            FitSeverities.Watch   => 2,
            FitSeverities.Unknown => 3,
            _                     => 4,
        }).ThenBy(x => x.Dimension).ToList();

    // ── Eligibility ─────────────────────────────────────────────────────────

    private static void EvaluateTurnover(List<FitFinding> f, EvalContext ctx)
    {
        var required = ctx.Tender.Financial?.TurnoverRequirement?.Bidder;
        if (required is null or <= 0) return;

        var evidence = $"Bidder turnover of at least {Money.Inr(required.Value)}";

        // The relaxation is checked BEFORE the shortfall. An MSE that would fail the raw
        // threshold does not fail the tender, and reporting a blocker it is statutorily exempt
        // from would push a customer off a bid they are entitled to make.
        if (ctx.Tender.Qualification?.MseRelaxation == true && ctx.IsMse)
        {
            f.Add(new FitFinding
            {
                Code = "TURNOVER_MSE_RELAXED", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Ok,
                Title = "Turnover criterion relaxed for MSEs",
                Detail = "This tender offers the MSE relaxation and your Udyam registration is on file, "
                       + "so the prior-turnover criterion should not apply to you. Carry the Udyam "
                       + "certificate in your bid — the relaxation is claimed, not automatic.",
                Evidence = evidence, OurPosition = $"Udyam {ctx.Profile?.UdyamNumber}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Low,
                Citation = CiteMse,
            });
            return;
        }

        if (ctx.Tender.Qualification?.StartupRelaxation == true && ctx.IsStartup)
        {
            f.Add(new FitFinding
            {
                Code = "TURNOVER_STARTUP_RELAXED", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Ok,
                Title = "Turnover criterion relaxed for recognised startups",
                Detail = "This tender offers the startup relaxation and your DPIIT recognition is on "
                       + "file. Attach the recognition certificate — the relaxation is claimed, not automatic.",
                Evidence = evidence, OurPosition = $"DPIIT {ctx.Profile?.DpiitStartupNumber}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Low,
                Citation = CiteStartup,
            });
            return;
        }

        var ours = ctx.AverageTurnover;
        if (ours is null)
        {
            f.Add(new FitFinding
            {
                Code = "TURNOVER_UNKNOWN", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Unknown,
                Title = "We can't check the turnover requirement",
                Detail = $"This tender requires {Money.Inr(required.Value)}. We don't have your "
                       + "turnover on file, so we can't tell you whether you clear it.",
                Evidence = evidence,
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                ActionLabel = "Add turnover", ActionHref = "/settings?tab=capability",
            });
            return;
        }

        if (ours.Value >= required.Value)
        {
            f.Add(new FitFinding
            {
                Code = "TURNOVER_MET", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Ok,
                Title = "Turnover requirement met",
                Detail = $"Your average annual turnover of {Money.Inr(ours.Value)} clears the "
                       + $"{Money.Inr(required.Value)} this tender asks for.",
                Evidence = evidence, OurPosition = $"{Money.Inr(ours.Value)} average over {ctx.TurnoverYears} year(s)",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
            });
            return;
        }

        var shortfall = required.Value - ours.Value;
        f.Add(new FitFinding
        {
            Code = "TURNOVER_SHORTFALL", Dimension = FitDimensions.Eligibility,
            // A turnover shortfall cannot be closed before a deadline — it is a fact about
            // completed financial years. That is what makes it a blocker rather than a gap.
            Severity = FitSeverities.Blocker,
            Title = "Turnover is below the required threshold",
            Detail = $"This tender requires {Money.Inr(required.Value)}; your average is "
                   + $"{Money.Inr(ours.Value)}, short by {Money.Inr(shortfall)}. "
                   + (ctx.Tender.Qualification?.MseRelaxation == true
                       ? "The tender does offer an MSE relaxation — if you are Udyam-registered, add the number to your profile and this recheck may clear."
                       : "Consider a consortium or JV bid if the tender permits one."),
            Evidence = evidence,
            OurPosition = $"{Money.Inr(ours.Value)} average over {ctx.TurnoverYears} year(s)",
            Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
            ActionLabel = "Review turnover", ActionHref = "/settings?tab=capability",
        });
    }

    private static void EvaluateExperience(List<FitFinding> f, EvalContext ctx)
    {
        var required = ctx.Tender.Qualification?.ExperienceYears;
        if (required is null or <= 0) return;

        var evidence = $"At least {required} year(s) of relevant experience";

        if (ctx.Tender.Qualification?.MseRelaxation == true && ctx.IsMse)
        {
            f.Add(new FitFinding
            {
                Code = "EXPERIENCE_MSE_RELAXED", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Ok,
                Title = "Experience criterion relaxed for MSEs",
                Detail = "This tender offers the MSE relaxation, which covers prior experience as well "
                       + "as turnover. Claim it explicitly in your bid with the Udyam certificate attached.",
                Evidence = evidence, OurPosition = $"Udyam {ctx.Profile?.UdyamNumber}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Low,
                Citation = CiteMse,
            });
            return;
        }

        if (ctx.YearsInBusiness is null)
        {
            f.Add(new FitFinding
            {
                Code = "EXPERIENCE_UNKNOWN", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Unknown,
                Title = "We can't check the experience requirement",
                Detail = $"This tender wants {required} year(s) of experience. Add your date of "
                       + "incorporation and we'll check this automatically on every tender.",
                Evidence = evidence,
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                ActionLabel = "Add incorporation date", ActionHref = "/settings?tab=capability",
            });
            return;
        }

        if (ctx.YearsInBusiness >= required)
        {
            f.Add(new FitFinding
            {
                Code = "EXPERIENCE_MET", Dimension = FitDimensions.Eligibility,
                Severity = FitSeverities.Ok,
                Title = "Experience requirement met",
                Detail = $"You've been trading {ctx.YearsInBusiness} year(s) against the "
                       + $"{required} this tender asks for.",
                Evidence = evidence, OurPosition = $"{ctx.YearsInBusiness} years since incorporation",
                // Years-in-business is not the same thing as years of experience IN THIS
                // CATEGORY, which is what a tender usually means. Say so rather than overclaim.
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Medium,
            });
            return;
        }

        f.Add(new FitFinding
        {
            Code = "EXPERIENCE_SHORTFALL", Dimension = FitDimensions.Eligibility,
            Severity = FitSeverities.Blocker,
            Title = "Not enough years of experience",
            Detail = $"This tender wants {required} year(s); your company has been trading "
                   + $"{ctx.YearsInBusiness}. This isn't closable before the deadline — check "
                   + "whether the tender allows a consortium or accepts a parent company's record.",
            Evidence = evidence, OurPosition = $"{ctx.YearsInBusiness} years since incorporation",
            Source = FitSources.OrgProfile, Confidence = FitConfidence.Medium,
        });
    }

    // ── Documentary ─────────────────────────────────────────────────────────

    private static void EvaluateCertifications(List<FitFinding> f, EvalContext ctx)
    {
        var required = ctx.Tender.Qualification?.Certifications;
        if (required is null || required.Length == 0) return;

        var deadline = ctx.Tender.ClosingDate;

        foreach (var raw in required.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            var wanted = CredentialMatching.Normalise(raw);
            var held = ctx.Credentials.FirstOrDefault(c => CredentialMatching.Matches(c.Code, wanted));

            if (held is null)
            {
                f.Add(new FitFinding
                {
                    Code = "CERTIFICATION_MISSING", Dimension = FitDimensions.Documentary,
                    // Not held and required = disqualifying. Certifying bodies do not turn
                    // around an ISO audit inside a bid window, so this is a blocker, not a gap.
                    Severity = FitSeverities.Blocker,
                    Title = $"{raw} — not on your record",
                    Detail = $"This tender requires {raw} and we don't have it in your credentials. "
                           + "If you do hold it, add it and we'll stop flagging it.",
                    Evidence = raw, OurPosition = "Not recorded",
                    Source = FitSources.OrgProfile, Confidence = FitConfidence.Medium,
                    ActionLabel = "Add credential", ActionHref = "/settings?tab=capability",
                });
                continue;
            }

            // The single highest-value rule in the engine, and the one no other part of the
            // product can answer: a certificate valid today but lapsed by the bid date is worth
            // nothing, and nobody notices until the bid is rejected.
            if (deadline is not null && held.ExpiresBy(deadline.Value))
            {
                var days = held.ValidUntil!.Value.DayNumber - ctx.Today.DayNumber;
                f.Add(new FitFinding
                {
                    Code = "CERTIFICATION_EXPIRES_BEFORE_DEADLINE", Dimension = FitDimensions.Documentary,
                    Severity = FitSeverities.Blocker,
                    Title = $"{raw} expires before this bid closes",
                    Detail = $"Your {held.Code} is valid until {held.ValidUntil:dd MMM yyyy}, but bids "
                           + $"close on {deadline:dd MMM yyyy}. "
                           + (days < 0
                               ? "It has already lapsed — renew before bidding."
                               : $"That's {days} day(s) from today; start the renewal now."),
                    Evidence = raw,
                    OurPosition = $"Held, valid until {held.ValidUntil:dd MMM yyyy}",
                    Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
                    ActionLabel = "Update credential", ActionHref = "/settings?tab=capability",
                });
                continue;
            }

            f.Add(new FitFinding
            {
                Code = "CERTIFICATION_HELD", Dimension = FitDimensions.Documentary,
                Severity = FitSeverities.Ok,
                Title = $"{raw} — on file",
                Detail = held.ValidUntil is null
                    ? $"You hold {held.Code}. No expiry recorded."
                    : $"You hold {held.Code}, valid until {held.ValidUntil:dd MMM yyyy} — past this "
                    + "tender's closing date.",
                Evidence = raw,
                OurPosition = held.ValidUntil is null ? "Held" : $"Held, valid until {held.ValidUntil:dd MMM yyyy}",
                Source = FitSources.OrgProfile,
                // "Medium": the match between the tender's wording and our stored code is a
                // normalised string comparison, not an identity.
                Confidence = FitConfidence.Medium,
            });
        }
    }

    private static void EvaluateRequiredDocuments(List<FitFinding> f, EvalContext ctx)
    {
        var required = ctx.Tender.Qualification?.RequiredDocuments;
        if (required is null || required.Length == 0) return;

        // Deliberately ONE finding rather than one per line. These are mostly generic bid-pack
        // items ("scanned copy of the ATC", "BoQ compliance sheet") that no profile could ever
        // hold, and emitting ten "unknown" rows would bury the findings that mean something.
        var unmatched = required
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Where(d => !ctx.Credentials.Any(c => CredentialMatching.Matches(c.Code, CredentialMatching.Normalise(d))))
            .ToList();

        if (unmatched.Count == 0) return;

        f.Add(new FitFinding
        {
            Code = "DOCUMENTS_TO_PREPARE", Dimension = FitDimensions.Documentary,
            Severity = FitSeverities.Watch,
            Title = $"{unmatched.Count} document(s) to prepare for the bid pack",
            Detail = "These are listed by the tender and aren't matched to anything on your "
                   + "credential record. Most are bid-pack items you assemble per tender rather "
                   + "than certificates you hold: " + string.Join("; ", unmatched.Take(8))
                   + (unmatched.Count > 8 ? $"; and {unmatched.Count - 8} more." : "."),
            Evidence = string.Join("; ", unmatched),
            Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
            ActionLabel = "Open document vault", ActionHref = "/documents",
        });
    }

    // ── Financial ───────────────────────────────────────────────────────────

    private static void EvaluateEmd(List<FitFinding> f, EvalContext ctx)
    {
        var emd = ctx.Tender.Financial?.Emd;
        var amount = emd?.Amount ?? ctx.Tender.EmdAmount;
        if (emd?.Required != true && amount is null or <= 0) return;

        // MSEs are exempt from bid security on GeM and under the PP Policy. Reporting a headroom
        // blocker against an amount the customer never has to block would be wrong in the
        // direction that costs them a bid.
        if (ctx.IsMse)
        {
            f.Add(new FitFinding
            {
                Code = "EMD_MSE_EXEMPT", Dimension = FitDimensions.Financial,
                Severity = FitSeverities.Ok,
                Title = "You should be exempt from EMD",
                Detail = amount is null or <= 0
                    ? "This tender requires bid security, but MSEs are exempt. Claim the exemption "
                    + "with your Udyam certificate rather than transferring funds."
                    : $"This tender's EMD is {Money.Inr(amount.Value)}, but MSEs are exempt. Claim the "
                    + "exemption with your Udyam certificate rather than blocking the money.",
                Evidence = amount is null or <= 0 ? "EMD required" : $"EMD {Money.Inr(amount.Value)}",
                OurPosition = $"Udyam {ctx.Profile?.UdyamNumber}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Low,
                Citation = CiteMseEmd,
            });
            return;
        }

        if (amount is null or <= 0)
        {
            f.Add(new FitFinding
            {
                Code = "EMD_AMOUNT_UNKNOWN", Dimension = FitDimensions.Financial,
                Severity = FitSeverities.Unknown,
                Title = "EMD required, amount not extracted",
                Detail = "This tender requires bid security but we couldn't read the amount from its "
                       + "documents. Check the tender PDF before you plan the cash.",
                Evidence = "EMD required",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                Citation = CiteGfr,
            });
            return;
        }

        var headroom = ctx.Profile?.EmdHeadroom;
        if (headroom is null)
        {
            f.Add(new FitFinding
            {
                Code = "EMD_HEADROOM_UNKNOWN", Dimension = FitDimensions.Financial,
                Severity = FitSeverities.Unknown,
                Title = $"EMD of {Money.Inr(amount.Value)}",
                Detail = "You'll need to block this until the bid is decided. Tell us your EMD "
                       + "headroom and we'll warn you when a tender exceeds it.",
                Evidence = $"EMD {Money.Inr(amount.Value)}",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                ActionLabel = "Set EMD headroom", ActionHref = "/settings?tab=capability",
                Citation = CiteGfr,
            });
            return;
        }

        if (amount.Value > headroom.Value)
        {
            f.Add(new FitFinding
            {
                Code = "EMD_EXCEEDS_HEADROOM", Dimension = FitDimensions.Financial,
                // A gap, not a blocker: unlike a completed financial year, capital can be
                // arranged inside a bid window.
                Severity = FitSeverities.Gap,
                Title = "EMD is more than your stated headroom",
                Detail = $"This tender asks you to block {Money.Inr(amount.Value)}; you've told us you "
                       + $"can cover {Money.Inr(headroom.Value)}. Arrange the difference "
                       + $"({Money.Inr(amount.Value - headroom.Value)}) or a bank guarantee before bidding.",
                Evidence = $"EMD {Money.Inr(amount.Value)}",
                OurPosition = $"Headroom {Money.Inr(headroom.Value)}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
                Citation = CiteGfr,
            });
            return;
        }

        f.Add(new FitFinding
        {
            Code = "EMD_WITHIN_HEADROOM", Dimension = FitDimensions.Financial,
            Severity = FitSeverities.Ok,
            Title = $"EMD of {Money.Inr(amount.Value)} is within your headroom",
            Detail = $"Blocking this leaves {Money.Inr(headroom.Value - amount.Value)} of your stated "
                   + "EMD capacity for other live bids.",
            Evidence = $"EMD {Money.Inr(amount.Value)}",
            OurPosition = $"Headroom {Money.Inr(headroom.Value)}",
            Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
        });
    }

    private static void EvaluateEpbg(List<FitFinding> f, EvalContext ctx)
    {
        var epbg = ctx.Tender.Financial?.Epbg;
        if (epbg?.Required != true) return;

        var exposure = BidCostEstimator.EpbgExposure(ctx.Tender);
        var available = ctx.AvailableBgLine;

        if (exposure is null || available is null)
        {
            f.Add(new FitFinding
            {
                Code = "EPBG_REQUIRED", Dimension = FitDimensions.Financial,
                Severity = FitSeverities.Watch,
                Title = "Performance bank guarantee required",
                Detail = epbg.Percentage is not null
                    ? $"{epbg.Percentage}% of the contract value"
                      + (epbg.DurationMonths is not null ? $", held for {epbg.DurationMonths} months." : ".")
                      + " This ties up your BG line after award, not at bid time."
                    : "The tender requires an ePBG after award. Check the percentage and duration in the PDF.",
                Evidence = "ePBG required",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                ActionLabel = available is null ? "Set BG limit" : null,
                ActionHref = available is null ? "/settings?tab=capability" : null,
            });
            return;
        }

        if (exposure > available)
        {
            f.Add(new FitFinding
            {
                Code = "EPBG_EXCEEDS_BG_LINE", Dimension = FitDimensions.Financial,
                Severity = FitSeverities.Gap,
                Title = "ePBG would exceed your available bank-guarantee line",
                Detail = $"Winning this needs roughly {Money.Inr(exposure.Value)} of BG"
                       + (epbg.DurationMonths is not null ? $" for {epbg.DurationMonths} months" : "")
                       + $"; you have {Money.Inr(available.Value)} unused. Talk to your banker before "
                       + "you bid, not after you win.",
                Evidence = $"ePBG {epbg.Percentage}% of contract value",
                OurPosition = $"{Money.Inr(available.Value)} of BG line unused",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Medium,
            });
            return;
        }

        f.Add(new FitFinding
        {
            Code = "EPBG_WITHIN_BG_LINE", Dimension = FitDimensions.Financial,
            Severity = FitSeverities.Ok,
            Title = "ePBG fits inside your bank-guarantee line",
            Detail = $"Roughly {Money.Inr(exposure.Value)} against {Money.Inr(available.Value)} unused.",
            Evidence = $"ePBG {epbg.Percentage}% of contract value",
            OurPosition = $"{Money.Inr(available.Value)} of BG line unused",
            Source = FitSources.OrgProfile, Confidence = FitConfidence.Medium,
        });
    }

    // ── Operational ─────────────────────────────────────────────────────────

    private static void EvaluateReach(List<FitFinding> f, EvalContext ctx)
    {
        var state = ctx.Tender.State;
        var states = ctx.Profile?.ServiceableStates;
        if (string.IsNullOrWhiteSpace(state) || states is not { Length: > 0 }) return;

        if (states.Contains(state, StringComparer.OrdinalIgnoreCase))
        {
            f.Add(new FitFinding
            {
                Code = "STATE_SERVICED", Dimension = FitDimensions.Operational,
                Severity = FitSeverities.Ok,
                Title = $"You cover {state}",
                Detail = $"{state} is on your serviceable list.",
                Evidence = $"Delivery in {state}", OurPosition = "Serviceable",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
            });
            return;
        }

        f.Add(new FitFinding
        {
            Code = "STATE_NOT_SERVICED", Dimension = FitDimensions.Operational,
            // Not disqualifying — nothing stops you bidding outside your usual footprint. It
            // changes your cost base, which is a pricing input, not a barrier.
            Severity = FitSeverities.Watch,
            Title = $"{state} isn't on your serviceable list",
            Detail = $"You can still bid, but delivery, installation and any on-site support in "
                   + $"{state} will cost more than your usual footprint. Price that in.",
            Evidence = $"Delivery in {state}",
            OurPosition = $"You cover: {string.Join(", ", states.Take(6))}"
                        + (states.Length > 6 ? $" and {states.Length - 6} more" : ""),
            Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
        });
    }

    private static void EvaluateCategory(List<FitFinding> f, EvalContext ctx)
    {
        var category = ctx.Tender.Category;
        var supplied = ctx.Profile?.CategoriesSupplied;
        if (string.IsNullOrWhiteSpace(category) || supplied is not { Length: > 0 }) return;

        // Both sides come from the same closed 40-category taxonomy, so this is an exact
        // comparison rather than a judgement — which is also why a free-text value on either
        // side would silently match nothing.
        if (supplied.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            f.Add(new FitFinding
            {
                Code = "CATEGORY_IN_SCOPE", Dimension = FitDimensions.Operational,
                Severity = FitSeverities.Ok,
                Title = "This is what you supply",
                Detail = $"\"{category}\" is on your list of supplied categories.",
                Evidence = category, OurPosition = "In scope",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
            });
            return;
        }

        f.Add(new FitFinding
        {
            Code = "CATEGORY_OUTSIDE_SCOPE", Dimension = FitDimensions.Operational,
            // A watch, never a blocker. Plenty of real wins are a first tender in an adjacent
            // category, and an engine that discouraged those would be wrong about the business
            // rather than about the rules.
            Severity = FitSeverities.Watch,
            Title = $"\"{category}\" isn't one of your usual categories",
            Detail = "Nothing stops you bidding, but check that the technical specifications are "
                   + "genuinely within reach before you commit the EMD and the prep time.",
            Evidence = category,
            OurPosition = $"You supply: {string.Join(", ", supplied.Take(5))}"
                        + (supplied.Length > 5 ? $" and {supplied.Length - 5} more" : ""),
            Source = FitSources.OrgProfile, Confidence = FitConfidence.High,
        });
    }

    private static void EvaluateTimeline(List<FitFinding> f, EvalContext ctx)
    {
        if (ctx.Tender.ClosingDate is not DateOnly close) return;
        var days = close.DayNumber - ctx.Today.DayNumber;

        if (days < 0)
        {
            f.Add(new FitFinding
            {
                Code = "TENDER_CLOSED", Dimension = FitDimensions.Operational,
                Severity = FitSeverities.Blocker,
                Title = "This tender has closed",
                Detail = $"Bids closed on {close:dd MMM yyyy}.",
                Evidence = $"Closes {close:dd MMM yyyy}",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
            });
            return;
        }

        if (days <= TightDeadlineDays)
        {
            f.Add(new FitFinding
            {
                Code = "DEADLINE_TIGHT", Dimension = FitDimensions.Operational,
                Severity = FitSeverities.Watch,
                Title = days == 0 ? "Closes today" : $"Only {days} day(s) to submit",
                Detail = "Anything on this list that needs a third party — a certifying body, an OEM "
                       + "letter, a bank guarantee — realistically can't be closed in that window. "
                       + "Decide now whether to bid as you stand.",
                Evidence = $"Closes {close:dd MMM yyyy}",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
            });
        }
    }

    // ── Commercial ──────────────────────────────────────────────────────────

    private static void EvaluateCommercial(List<FitFinding> f, EvalContext ctx)
    {
        var c = ctx.Tender.Commercial;
        if (c is null) return;

        if (c.ReverseAuction?.Enabled == true)
            f.Add(new FitFinding
            {
                Code = "REVERSE_AUCTION", Dimension = FitDimensions.Commercial,
                Severity = FitSeverities.Watch,
                Title = "This goes to a reverse auction",
                Detail = "Your opening price isn't your final price — decide your floor before the "
                       + "auction opens, not during it."
                       + (string.IsNullOrWhiteSpace(c.ReverseAuction.Rule) ? "" : $" Rule: {c.ReverseAuction.Rule}"),
                Evidence = "Reverse auction enabled",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
            });

        if (c.BidSplitting)
            f.Add(new FitFinding
            {
                Code = "BID_SPLITTING", Dimension = FitDimensions.Commercial,
                Severity = FitSeverities.Watch,
                Title = "The award may be split between bidders",
                Detail = "Winning may mean winning a share of the quantity, not all of it. Check that "
                       + "your pricing still works at a fraction of the volume.",
                Evidence = "Bid splitting enabled",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
            });
    }

    private static void EvaluatePreferences(List<FitFinding> f, EvalContext ctx)
    {
        var c = ctx.Tender.Compliance;
        if (c is null) return;

        if (c.MsePreference && ctx.IsMse)
            f.Add(new FitFinding
            {
                Code = "MSE_PREFERENCE_AVAILABLE", Dimension = FitDimensions.Commercial,
                Severity = FitSeverities.Ok,
                Title = "MSE purchase preference is available to you",
                Detail = "If you finish within 15% of the lowest bid, you can match L1 and take a share "
                       + "of the order. That widens the price you can realistically win at."
                       + (c.PurchasePreferencePercent is not null
                           ? $" This tender states {c.PurchasePreferencePercent}%."
                           : ""),
                Evidence = "MSE purchase preference applies",
                OurPosition = $"Udyam {ctx.Profile?.UdyamNumber}",
                Source = FitSources.OrgProfile, Confidence = FitConfidence.Low,
                Citation = CiteMse,
            });
        else if (c.MsePreference && !ctx.IsMse)
            f.Add(new FitFinding
            {
                Code = "MSE_PREFERENCE_AGAINST_US", Dimension = FitDimensions.Commercial,
                Severity = FitSeverities.Watch,
                Title = "MSE bidders get a purchase preference here",
                Detail = "An MSE finishing within 15% of your price can match it and take a share of "
                       + "the order. Being L1 doesn't guarantee the full quantity.",
                Evidence = "MSE purchase preference applies",
                OurPosition = ctx.Profile is null ? "No Udyam number on file" : "Not registered as an MSE",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.Medium,
                Citation = CiteMse,
            });

        if (c.MiiPreference)
            f.Add(new FitFinding
            {
                Code = "MII_PREFERENCE", Dimension = FitDimensions.Commercial,
                Severity = FitSeverities.Watch,
                Title = "Make in India preference applies",
                Detail = "Local-content declarations are part of the bid. Have your supplier's local "
                       + "content percentage documented before you submit — it is checked, not assumed.",
                Evidence = "PPP-MII preference applies",
                Source = FitSources.TenderStructured, Confidence = FitConfidence.High,
                Citation = CiteMii,
            });
    }

    // ── Evaluation context ──────────────────────────────────────────────────

    /// <summary>Everything the rules read, resolved once. Keeps each rule to its own logic
    /// rather than repeating null-handling and relaxation checks ten times.</summary>
    private sealed class EvalContext(
        TenderDetailDto tender,
        OrgCapabilityProfile? profile,
        IReadOnlyList<OrgCredential> credentials,
        OrgBidHistory history,
        DateOnly today)
    {
        public TenderDetailDto Tender { get; } = tender;
        public OrgCapabilityProfile? Profile { get; } = profile;
        public IReadOnlyList<OrgCredential> Credentials { get; } = credentials;
        public OrgBidHistory History { get; } = history;
        public DateOnly Today { get; } = today;

        /// <summary>Self-asserted, which is why every relaxation finding that depends on it is
        /// marked low confidence and tells the bidder to carry the certificate.</summary>
        public bool IsMse => !string.IsNullOrWhiteSpace(Profile?.UdyamNumber);

        public bool IsStartup => !string.IsNullOrWhiteSpace(Profile?.DpiitStartupNumber);

        /// <summary>Tenders ask for AVERAGE annual turnover over the last three financial years,
        /// so that is what we compare — averaging only the years actually supplied rather than
        /// treating a missing year as zero, which would manufacture a shortfall.</summary>
        public decimal? AverageTurnover
        {
            get
            {
                decimal[] years = [Profile?.TurnoverFy1 ?? 0, Profile?.TurnoverFy2 ?? 0, Profile?.TurnoverFy3 ?? 0];
                var supplied = years.Where(v => v > 0).ToArray();
                return supplied.Length == 0 ? null : supplied.Sum() / supplied.Length;
            }
        }

        public int TurnoverYears =>
            new[] { Profile?.TurnoverFy1, Profile?.TurnoverFy2, Profile?.TurnoverFy3 }
                .Count(v => v is > 0);

        public int? YearsInBusiness => Profile?.IncorporationDate is not DateOnly d
            ? null
            : (int)((Today.DayNumber - d.DayNumber) / 365.25);

        public decimal? AvailableBgLine => Profile?.BgLimit is null
            ? null
            : Math.Max(0, Profile.BgLimit.Value - (Profile.BgUtilised ?? 0));

        /// <summary>
        /// Is there enough on file to call this an answer rather than a shrug? The bar is
        /// deliberately low — one real operand — because demanding a complete profile would
        /// leave the paid tab saying "insufficient data" to nearly everyone, and a customer who
        /// has told us their turnover is entitled to the turnover verdict.
        /// </summary>
        public bool CanEvaluate =>
            AverageTurnover is not null
            || Credentials.Count > 0
            || Profile?.EmdHeadroom is not null
            || Profile?.IncorporationDate is not null;
    }
}

/// <summary>
/// The org's own bidding record, derived from <c>bids</c> rather than stored — a second copy
/// would drift. Currently informational; the rules read it for context rather than for verdicts.
/// </summary>
public sealed record OrgBidHistory(
    int BidsSubmitted,
    int BidsWon,
    decimal? LargestWonValue,
    IReadOnlyList<string> CategoriesWon)
{
    public static readonly OrgBidHistory Empty = new(0, 0, null, []);
}

/// <summary>What <see cref="TenderFitRules.Evaluate"/> returns.</summary>
public sealed record FitResult(
    string Verdict,
    string VerdictReason,
    IReadOnlyList<FitFinding> Findings,
    BidCostEstimate Cost,
    string RuleSetVersion);
