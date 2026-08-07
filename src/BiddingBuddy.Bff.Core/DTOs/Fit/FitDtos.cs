using BiddingBuddy.Bff.Core.Fit;

namespace BiddingBuddy.Bff.Core.DTOs.Fit;

/// <summary>
/// The paid artifact, as the client receives it.
/// </summary>
public record FitReportDto(
    Guid TenderId,
    string TenderTitle,

    /// <summary>go | go_with_gaps | blocked | insufficient_data. Never a percentage.</summary>
    string Verdict,
    string VerdictReason,

    IReadOnlyList<FitFinding> Findings,
    FitCountsDto Counts,

    BidCostEstimate Cost,

    /// <summary>The LLM block. Null until the async worker returns; the report above is complete
    /// and correct without it.</summary>
    string? Narrative,
    string NarrativeState,

    /// <summary>Pinned rule-set version + when it ran. Shown on the report, because a verdict
    /// without a date and a rule set is an assertion rather than a record.</summary>
    string RuleSetVersion,
    DateTime GeneratedAt,

    /// <summary>The tender moved (a corrigendum, or a re-scrape) since this was computed, so it
    /// may now be wrong. A re-run in this state is free.</summary>
    bool IsStale,
    string? StaleReason,

    /// <summary>How much of the org's capability profile is filled in. Drives the "this is why
    /// the answer is thin" prompt rather than leaving the user to guess.</summary>
    Capability.CompletenessDto ProfileCompleteness,

    /// <summary>Whether producing this report moved the AI meter, and where the meter now
    /// stands. False for a re-read, for a stale re-run, and for insufficient_data.</summary>
    bool Charged,
    Tenders.AiUsageDto AiUsage);

/// <summary>Severity tallies, computed once server-side so the badge counts can't disagree with
/// the list beneath them.</summary>
public record FitCountsDto(int Blockers, int Gaps, int Watch, int Ok, int Unknown);
