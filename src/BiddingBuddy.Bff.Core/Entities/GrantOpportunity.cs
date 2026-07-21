namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// The Postgres shadow index for a grant opportunity. Mongo (BiddingBuddyServices) remains
/// authoritative for the full record; this row exists so matching, alerting and deep-links have
/// something relational and indexable — the role <see cref="Tender"/> plays for the tender line.
///
/// <para><b>No <c>OrgId</c>.</b> Grant opportunities are a global corpus, like tenders. Org-scoped
/// grant tables carry their own tenancy.</para>
/// </summary>
public class GrantOpportunity
{
    /// <summary>
    /// Generated UUID. Load-bearing: <c>notification_reminders.entity_id</c> is <c>UUID NOT NULL</c>
    /// and <c>user_notifications.entity_id</c> is <c>uuid</c>.
    /// </summary>
    public Guid Id { get; set; }

    // ── Natural key from the source ──────────────────────────────────────────
    public string Platform { get; set; } = "grants-gov";
    public string PlatformGrantId { get; set; } = default!;

    /// <summary>BiddingBuddyServices Mongo <c>_id</c>. Set-once — never overwritten once populated.</summary>
    public string? MongoGrantId { get; set; }

    public string? OpportunityNumber { get; set; }
    public string? SourceUrl { get; set; }

    public string Title { get; set; } = default!;
    public string? Summary { get; set; }
    public string? AgencyName { get; set; }
    public string? AgencyCode { get; set; }
    public string? Category { get; set; }

    // ── Money (USD). NULL means "not published", NEVER zero. ─────────────────
    public string Currency { get; set; } = "USD";
    public decimal? AwardCeiling { get; set; }
    public decimal? AwardFloor { get; set; }
    public decimal? EstimatedTotalProgramFunding { get; set; }
    public int? ExpectedNumberOfAwards { get; set; }
    public bool? CostSharingRequired { get; set; }

    // ── Dates. DateOnly because the source publishes no cutoff TIME. ─────────
    public DateOnly? PostedDate { get; set; }
    public DateOnly? CloseDate { get; set; }
    public DateOnly? LoiDueDate { get; set; }
    public DateOnly? ArchiveDate { get; set; }

    /// <summary>The source's verbatim deadline prose — the only place a real cutoff can live.</summary>
    public string? CloseDateExplanation { get; set; }

    public bool IsRolling { get; set; }

    /// <summary>Verbatim source labels. Never paraphrased — they decide who may legally apply.</summary>
    public string[]? ApplicantTypesRaw { get; set; }

    /// <summary>Derived Grants.gov codes — what queries actually filter on.</summary>
    public string[]? ApplicantTypeCodes { get; set; }

    public bool? TribalGovernmentsEligible { get; set; }
    public bool? TribalOrganizationsEligible { get; set; }
    public bool? Nonprofit501C3Eligible { get; set; }
    public bool? IsTribalSetAside { get; set; }
    public bool? NativeLedPriority { get; set; }

    public string[]? AssistanceListingNumbers { get; set; }
    public string[]? FundingInstruments { get; set; }

    public bool IsForecast { get; set; }

    /// <summary><c>forecasted</c> | <c>posted</c> | <c>closed</c> | <c>archived</c>.</summary>
    public string Status { get; set; } = "posted";

    public int AiScore { get; set; }
    public string? AiSummary { get; set; }
    public string[]? AiTags { get; set; }

    /// <summary>NULL = not yet evaluated against alert rules.</summary>
    public DateTime? AlertsScannedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
