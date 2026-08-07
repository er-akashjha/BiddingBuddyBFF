namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// What the organization can actually do — the other half of every eligibility question.
///
/// <para>The enrichment pipeline produces one artifact per tender, globally: the same text for
/// every customer. That is the right shape for "what does this tender demand" and structurally
/// incapable of answering "can <i>we</i> bid it". This row is the missing operand. Without it,
/// <c>tenders.eligibility_score</c> is a column the only writer sets to null and the UI renders
/// anyway.</para>
///
/// <para>One row per org (<see cref="OrgId"/> is the primary key). Absent = the org has not filled
/// it in, which the fit engine reports as <c>insufficient_data</c> rather than guessing — an
/// unfilled profile must never read as a failed check.</para>
///
/// <para>Credentials live in <see cref="OrgCredential"/>, not here, because expiry has to be
/// indexable and each one links to the vault document that proves it.</para>
/// </summary>
public class OrgCapabilityProfile
{
    public Guid OrgId { get; set; }

    // ── Financial standing ──────────────────────────────────────────────────
    // Three years because that is what Indian tenders ask for ("average annual turnover of the
    // last three financial years"). Fy1 = most recently completed FY.

    public decimal? TurnoverFy1 { get; set; }
    public decimal? TurnoverFy2 { get; set; }
    public decimal? TurnoverFy3 { get; set; }

    /// <summary>e.g. "FY 2025-26". Stored, not computed — an org onboarding in April has a
    /// different "last completed FY" than one onboarding in March, and inferring it wrong
    /// silently shifts every turnover comparison by a year.</summary>
    public string? TurnoverFy1Label { get; set; }

    public decimal? NetWorth { get; set; }

    /// <summary>Experience years are derived from this rather than typed, so they cannot go
    /// stale between the day it is entered and the day a tender is evaluated.</summary>
    public DateOnly? IncorporationDate { get; set; }

    // ── Statutory registrations that unlock RELAXATIONS ──────────────────────
    // Not merely descriptive: an MSE registration lowers turnover and experience thresholds
    // (PP Policy for MSEs Order 2012) and exempts EMD. Without them the engine has to assume
    // the strictest reading — honest, but the wrong answer for most of our customers.

    public string? UdyamNumber { get; set; }
    /// <summary>micro | small | medium.</summary>
    public string? UdyamCategory { get; set; }
    /// <summary>DPIIT/DIPP startup recognition number — unlocks the startup relaxations.</summary>
    public string? DpiitStartupNumber { get; set; }
    public string? NsicNumber { get; set; }

    // ── Reach and capacity ──────────────────────────────────────────────────

    /// <summary>Canonical 36-state vocabulary ONLY. Matching is exact, so a free-text value
    /// here matches nothing, forever, with no error.</summary>
    public string[]? ServiceableStates { get; set; }

    /// <summary>Canonical 40-category taxonomy ONLY. Same exact-match constraint.</summary>
    public string[]? CategoriesSupplied { get; set; }

    /// <summary>Working capital the org can block as EMD. A tender whose EMD exceeds this is a
    /// real blocker that nothing else in the product knows about.</summary>
    public decimal? EmdHeadroom { get; set; }

    public decimal? BgLimit { get; set; }
    public decimal? BgUtilised { get; set; }

    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
