namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A tender notice being authored by a buyer organization, and — once published — the mutable
/// head of a chain of immutable versions in <see cref="TenderVersion"/>.
///
/// <para><b>Why the draft is the aggregate root and not <c>tenders</c>.</b> Tenders in this system
/// are global: neither the Mongo document nor the Postgres <c>tenders</c> row carries an org id,
/// because every one of them was scraped from a portal and belongs to nobody. A department's own
/// notice is the first tender that has an owner, and it has one from the moment the form is opened
/// — long before it is published anywhere. So ownership hangs off the draft.</para>
///
/// <para><b>Postgres owns the truth, Mongo owns the view.</b> On publish this projects into the
/// Mongo <c>tenders</c> collection with <c>source.platform = "direct"</c>, which is what puts it on
/// the public portal and through the matching rail with no new read-side code. That projection is an
/// upsert and is *supposed* to be mutable — a corrigendum must change what suppliers see. The
/// immutability requirement lives here instead, in the hash-chained version rows, which is the
/// artefact an auditor actually asks for.</para>
///
/// Maps to <c>tender_drafts</c> (migration 0031).
/// </summary>
public class TenderDraft
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>
    /// Our generated, URL-safe reference: <c>TA-2026-000123</c>. Unique platform-wide.
    ///
    /// <para>Deliberately NOT the department's own file number. Those look like
    /// <c>F.No.12-3/2026-Admin</c>, and a slash in an identifier is already why the enrichment-status
    /// endpoint has to take its id in a request body rather than a route segment. Generating our own
    /// means this id never needs escaping in a URL, a filename, or an S3 key.</para>
    /// </summary>
    public string ReferenceCode { get; set; } = string.Empty;

    /// <summary>The department's own file number, verbatim, for display and correspondence.
    /// Free text — slashes and all — because it is never used as an identifier.</summary>
    public string? DepartmentReference { get; set; }

    /// <summary>draft | published | awarded | cancelled. A corrigendum does NOT move a tender out
    /// of <c>published</c> — it bumps the version. Cancellation is terminal and is itself recorded
    /// as a corrigendum, because bidders must be told.</summary>
    public string Status { get; set; } = "draft";

    // ── B. Basic details ─────────────────────────────────────────────────────
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ScopeOfWork { get; set; } = string.Empty;
    public string? TenderType { get; set; }
    public string? ProcurementCategory { get; set; }
    public string? FormOfContract { get; set; }
    public string? BiddingSystem { get; set; }
    public string? EvaluationMethod { get; set; }

    /// <summary>QCBS only. Financial weightage is <c>100 - this</c> and is never stored, so the
    /// two cannot drift apart.</summary>
    public decimal? TechnicalWeightage { get; set; }

    public string? GfrRuleCited { get; set; }

    /// <summary>
    /// MUST be a value from the canonical taxonomy. BiddingBuddyServices rewrites an off-taxonomy
    /// category silently on write, and alert matching is an exact match — so a free-text category
    /// does not degrade the tender's reach, it makes the tender match nobody, forever. The BFF
    /// rejects rather than rewrites, and the authoring form only offers canonical values.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>Canonical state name, same constraint and same reason as <see cref="Category"/>.</summary>
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Pincode { get; set; }

    // ── E. Fees and financials ───────────────────────────────────────────────
    public decimal? EstimatedValue { get; set; }
    public bool ValueDisclosed { get; set; } = true;
    public decimal? EmdAmount { get; set; }
    public decimal? EmdPercentage { get; set; }
    public string? EmdMode { get; set; }
    public string[] EmdExemptions { get; set; } = [];
    public decimal? TenderFee { get; set; }
    public string[] TenderFeeExemptions { get; set; } = [];
    public decimal? PerformanceSecurityPct { get; set; }
    public int? BidValidityDays { get; set; }

    // ── F. Critical dates — server-authoritative, cross-validated ────────────
    public DateTime? PublishedAt { get; set; }
    public DateTime? DocDownloadStart { get; set; }
    public DateTime? DocDownloadEnd { get; set; }
    public DateTime? ClarificationStart { get; set; }
    public DateTime? ClarificationEnd { get; set; }
    public DateTime? PrebidMeetingAt { get; set; }
    public string? PrebidVenue { get; set; }
    public DateTime? BidSubmissionStart { get; set; }
    public DateTime? BidSubmissionEnd { get; set; }
    public DateTime? TechnicalOpeningAt { get; set; }
    public DateTime? FinancialOpeningAt { get; set; }

    // ── H. Statutory compliance — the audit-proofing layer ───────────────────
    public decimal? MseReservationPct { get; set; }
    public bool MiiApplicable { get; set; }
    public decimal? MiiLocalContentPct { get; set; }
    public string? MiiClassRestriction { get; set; }
    public bool LbsDeclarationRequired { get; set; }
    public bool StartupRelaxation { get; set; }
    public bool IntegrityPactApplicable { get; set; }
    public string? IntegrityPactMonitor { get; set; }
    public string? GemarptsReference { get; set; }

    /// <summary>
    /// The long tail of the field inventory — covers, line items, technical specifications,
    /// eligibility criteria, document list, contact block. Written and read as a whole and never
    /// searched, so it is one JSONB rather than forty columns no query names.
    /// Shape: <see cref="DTOs.Tenders.TenderDraftDetail"/>.
    /// </summary>
    public string Detail { get; set; } = "{}";

    /// <summary>
    /// The compliance rule set this draft was last validated against, pinned at publish.
    ///
    /// <para>Rules change. Without pinning, a compliance engine silently evaluates a two-year-old
    /// tender under today's thresholds and produces a confidently wrong audit answer — which is
    /// worse than producing none.</para>
    /// </summary>
    public string? RuleSetVersion { get; set; }

    /// <summary>Mongo <c>_id</c> of the projected tender. Set once, at first publish, so every
    /// corrigendum re-projects to the same document and existing read paths keep resolving.</summary>
    public string? MongoTenderId { get; set; }

    /// <summary>0 while unpublished; then the highest <see cref="TenderVersion.Version"/>.</summary>
    public int CurrentVersion { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public ICollection<TenderVersion> Versions { get; set; } = [];
    public ICollection<Corrigendum> Corrigenda { get; set; } = [];
    public ICollection<TenderCommitteeMember> CommitteeMembers { get; set; } = [];
}
