namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One org's verdict on one tender — the artifact the AI credit actually buys.
///
/// <para>Supersedes <see cref="AiAnalysisResult"/>, which is keyed by tender alone (global, so it
/// cannot hold a per-org answer) and which nothing has ever written.</para>
///
/// <para><see cref="Findings"/> is stored as JSONB rather than normalised because a finding is
/// read as a whole and never queried by field. What matters is that it is <i>structured</i>: each
/// carries the source that produced it and a confidence, both of which the client renders, so a
/// model's inference cannot be mistaken for a fact read off the org's profile.</para>
/// </summary>
public class TenderFitReport
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid TenderId { get; set; }

    /// <summary>See <see cref="Fit.FitVerdicts"/>. Never a percentage.</summary>
    public string Verdict { get; set; } = default!;

    /// <summary>One sentence the client shows next to the verdict chip.</summary>
    public string? VerdictReason { get; set; }

    /// <summary>Serialized <c>FitFinding[]</c>.</summary>
    public string Findings { get; set; } = "[]";

    public decimal? CostToBid { get; set; }
    /// <summary>Serialized <c>BidCostLine[]</c> — the itemisation behind <see cref="CostToBid"/>.</summary>
    public string? CostBreakdown { get; set; }

    /// <summary>The rule-set version this was computed under. Pinned for the same reason
    /// <c>TenderComplianceRules.Version</c> is: re-rendering a stored report under newer rules
    /// gives a confidently wrong answer.</summary>
    public string RuleSetVersion { get; set; } = default!;

    // ── Staleness inputs ────────────────────────────────────────────────────
    // A corrigendum can change eligibility outright, so a report computed before one is not
    // merely old — it may be wrong. Drift against the tender's current values banners the
    // report and offers a FREE re-run: the customer must not pay again because the buyer
    // amended the notice.

    public DateTime? ProfileUpdatedAt { get; set; }
    public DateTime? TenderUpdatedAt { get; set; }
    public int? CorrigendumCount { get; set; }

    // ── The one LLM-authored block ──────────────────────────────────────────
    // Null until the async worker returns. The report is complete and correct without it: the
    // verdict above is decided by the rules engine, never by a model. That is what makes this
    // feature safe to sell when a provider is down.

    public string? Narrative { get; set; }
    public string? NarrativeModel { get; set; }
    /// <summary>pending | ready | failed | skipped.</summary>
    public string NarrativeState { get; set; } = "pending";
    public string? NarrativeError { get; set; }

    public DateTime GeneratedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Tender Tender { get; set; } = default!;
}

/// <summary>The <see cref="TenderFitReport.NarrativeState"/> vocabulary.</summary>
public static class NarrativeStates
{
    public const string Pending = "pending";
    public const string Ready   = "ready";
    public const string Failed  = "failed";
    /// <summary>Deliberately not requested — e.g. the org's plan has no narrative entitlement,
    /// or the verdict was <c>insufficient_data</c> and there is nothing to narrate.</summary>
    public const string Skipped = "skipped";

    /// <summary>Validated at the internal callback so a typo from the pipeline can't park a
    /// report in a state the client has no rendering for (an unknown state reads as a
    /// permanent spinner).</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pending, Ready, Failed, Skipped };
}
