namespace BiddingBuddy.Bff.Core.Fit;

/// <summary>
/// One checked thing: what the tender demands, what we hold, and how far apart they are.
///
/// <para><b>Why every finding carries its source and its confidence.</b> The tab this feeds is
/// the one a customer pays for, and its predecessor rendered six hardcoded constants — an
/// expired ISO 27001, a ₹3.5Cr turnover, an OEM letter pending with Dell — as confident facts
/// about whoever was looking. The defence against that is not reviewer discipline, it is the
/// type: a finding cannot be constructed without saying where it came from, and the client
/// renders <see cref="Source"/> and <see cref="Confidence"/> rather than hiding them. A model's
/// inference and a subtraction over two known numbers do not get to look alike.</para>
///
/// <para><b>Why citations.</b> Same reason <c>TenderComplianceRules</c> carries them on the buyer
/// side: a finding that says "you may be exempt from EMD" is an opinion; one that says it and
/// cites the PP Policy for MSEs Order 2012 is a reference the bidder can take to their banker.</para>
/// </summary>
public sealed record FitFinding
{
    /// <summary>Stable machine code, e.g. <c>TURNOVER_SHORTFALL</c>. Tests assert on this, the
    /// client keys on it, and it never changes meaning — a rule whose meaning changes gets a new
    /// code and a rule-set version bump.</summary>
    public required string Code { get; init; }

    /// <summary>See <see cref="FitDimensions"/>.</summary>
    public required string Dimension { get; init; }

    /// <summary>See <see cref="FitSeverities"/>.</summary>
    public required string Severity { get; init; }

    public required string Title { get; init; }

    /// <summary>One or two sentences a bidder can act on. Never hedged into meaninglessness:
    /// if we don't know, the severity is <c>unknown</c> and this says exactly what is missing.</summary>
    public required string Detail { get; init; }

    /// <summary>What the tender asks for, in its own terms.</summary>
    public string? Evidence { get; init; }

    /// <summary>The verbatim span from the tender document, when its text is available. Often
    /// null: <c>tender_document_content</c> is purged aggressively to reclaim Atlas storage, so
    /// most tenders can only be evaluated from the structured extraction.</summary>
    public string? EvidenceQuote { get; init; }

    /// <summary>What the org's own record says. Null when the profile has nothing to say, which
    /// is a different statement from "the org fails this" and is rendered differently.</summary>
    public string? OurPosition { get; init; }

    /// <summary>See <see cref="FitSources"/>.</summary>
    public required string Source { get; init; }

    /// <summary>See <see cref="FitConfidence"/>.</summary>
    public required string Confidence { get; init; }

    public string? ActionLabel { get; init; }

    /// <summary>In-app deep link to the remedy — the exact document type to upload, the profile
    /// field to fill. A gap the user cannot navigate to is a complaint, not a finding.</summary>
    public string? ActionHref { get; init; }

    /// <summary>The statutory instrument behind the rule, where one applies.</summary>
    public string? Citation { get; init; }
}

public static class FitDimensions
{
    /// <summary>Can we qualify at all — turnover, experience, reach.</summary>
    public const string Eligibility = "eligibility";
    /// <summary>Money we must find or block — EMD, ePBG.</summary>
    public const string Financial = "financial";
    /// <summary>Paper we must produce — certificates, authorizations, registrations.</summary>
    public const string Documentary = "documentary";
    /// <summary>Can we deliver — timeline, geography, capacity.</summary>
    public const string Operational = "operational";
    /// <summary>How the award is decided — evaluation method, RA, preferences.</summary>
    public const string Commercial = "commercial";
}

public static class FitSeverities
{
    /// <summary>Disqualifying. Bidding without fixing it wastes the EMD and the effort.</summary>
    public const string Blocker = "blocker";
    /// <summary>Fixable before the deadline, but only if started now.</summary>
    public const string Gap = "gap";
    /// <summary>Not a barrier; changes how you should bid.</summary>
    public const string Watch = "watch";
    /// <summary>Checked and satisfied. Shown, because a bidder needs to know what we verified,
    /// not only what we objected to — a screen of nothing but problems reads as a broken tool.</summary>
    public const string Ok = "ok";
    /// <summary>The tender asks for something we cannot check, because the profile is silent or
    /// the tender did not state it in a machine-readable way. Never dressed up as a pass.</summary>
    public const string Unknown = "unknown";
}

public static class FitSources
{
    /// <summary>The tender's structured extraction (durable; survives document purging).</summary>
    public const string TenderStructured = "tender_structured";
    /// <summary>A quoted span from the tender's own document text.</summary>
    public const string TenderClause = "tender_clause";
    /// <summary>A named field on the org's capability profile or credential list.</summary>
    public const string OrgProfile = "org_profile";
    /// <summary>Aggregated real GeM award ladders.</summary>
    public const string AwardData = "award_data";
    /// <summary>An LLM inference. Rendered visually distinct; never decides a verdict.</summary>
    public const string Model = "model";
}

public static class FitConfidence
{
    /// <summary>Both sides were explicit values and the comparison is arithmetic.</summary>
    public const string High = "high";
    /// <summary>One side was normalised or matched rather than read directly.</summary>
    public const string Medium = "medium";
    /// <summary>Heuristic — a string match over free text, or a relaxation whose applicability
    /// depends on facts we hold only by the org's own assertion.</summary>
    public const string Low = "low";
}

public static class FitVerdicts
{
    /// <summary>Nothing found that stops this bid.</summary>
    public const string Go = "go";
    /// <summary>Biddable, but something must be fixed first.</summary>
    public const string GoWithGaps = "go_with_gaps";
    /// <summary>At least one disqualifying finding.</summary>
    public const string Blocked = "blocked";
    /// <summary>We decline to answer. A first-class outcome, not a failure state — the profile
    /// is too thin to compare against, and guessing would produce exactly the confident-wrong
    /// verdict this whole design exists to avoid.</summary>
    public const string InsufficientData = "insufficient_data";
}
