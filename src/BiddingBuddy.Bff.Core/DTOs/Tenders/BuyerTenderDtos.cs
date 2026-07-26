namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

// ── The JSONB long tail ──────────────────────────────────────────────────────
//
// Everything in the §4 field inventory that is written and read as a whole and never
// filtered on. Serialized into tender_drafts.detail.

/// <summary>One required document inside a bid cover.</summary>
public record CoverDocumentDto(
    string Name,
    string Description,
    bool Mandatory,
    string[] PermittedFileTypes,
    int? MaxSizeMb);

/// <summary>A bid cover (envelope). Cover 1 is technical, Cover 2 financial, in the usual two-cover
/// system; the count and names come from <c>bidding_system</c>.</summary>
public record CoverDto(
    int CoverNumber,
    string Name,
    CoverDocumentDto[] Documents);

/// <summary>A line item in the structured Bill of Quantities.</summary>
/// <remarks>
/// Structured, never a spreadsheet blob. The BOQ arriving as a rigid XLS whose format must not be
/// altered is one of the authoring pain points this feature exists to remove — and a structured BOQ
/// is also what makes the notice machine-matchable against supplier interests.
/// </remarks>
public record BoqItemDto(
    string ItemCode,
    string Description,
    string Unit,
    decimal Quantity,
    decimal? EstimatedRate,
    decimal? Amount);

public record TechSpecDto(
    string Group,
    string Name,
    string Value,
    string? Tolerance,
    bool Mandatory);

/// <summary>A delivery or service location with its own quantity — multi-consignee is the norm,
/// not the exception, in government supply orders.</summary>
public record ConsigneeDto(
    string Name,
    string Address,
    string State,
    string? Pincode,
    decimal? Quantity);

public record EligibilityDto(
    decimal? MinAnnualTurnover,
    int? TurnoverYears,
    int? SimilarWorkCount,
    decimal? SimilarWorkMinValue,
    int? ExperienceLookbackYears,
    decimal? MinNetWorth,
    string[] RequiredRegistrations,
    bool JointVentureAllowed,
    int? MaxJvMembers,
    bool SubcontractingAllowed,
    decimal? SubcontractingCapPct,
    bool OemAuthorisationRequired,
    string? ContractorClass,
    string? AdditionalCriteria);

/// <summary>A file attached to the notice. Stored in R2 under the org's prefix like every other
/// org-uploaded document — NOT in the AWS tender bucket, which belongs to the scraping pipeline.</summary>
public record TenderAttachmentDto(
    string Id,
    string Type,          // nit | tender_document | boq | drawing | annexure | corrigendum
    string FileName,
    string ObjectKey,
    long SizeBytes,
    DateTime UploadedAt);

public record TenderContactDto(
    string AuthorityName,
    string Designation,
    string Email,
    string Phone,
    string Address);

/// <summary>Make/model restrictions carry a MANDATORY written justification: restricting a
/// government tender to a brand is the single most audit-sensitive specification choice there is.</summary>
public record BrandRestrictionDto(
    string Make,
    string? Model,
    string Justification);

/// <summary>
/// The whole JSONB payload of <c>tender_drafts.detail</c>. One record so the column is written and
/// read atomically — partial updates to a JSONB blob are how these silently lose data.
/// </summary>
public record TenderDraftDetail
{
    public CoverDto[] Covers { get; init; } = [];
    public BoqItemDto[] Items { get; init; } = [];
    public TechSpecDto[] TechnicalSpecifications { get; init; } = [];
    public ConsigneeDto[] Consignees { get; init; } = [];
    public EligibilityDto? Eligibility { get; init; }
    public TenderAttachmentDto[] Attachments { get; init; } = [];
    public TenderContactDto? Contact { get; init; }
    public BrandRestrictionDto[] BrandRestrictions { get; init; } = [];
    public string? DeliveryPeriod { get; init; }
    public string? WarrantyPeriod { get; init; }
    public string? InspectionRegime { get; init; }
    public string? PaymentTerms { get; init; }
    public string? LiquidatedDamages { get; init; }
    public string? ArbitrationClause { get; init; }
    public bool SampleRequired { get; init; }
    public bool ReverseAuction { get; init; }
    public bool AllowResubmission { get; init; } = true;
    public bool AllowWithdrawal { get; init; } = true;
    public string? TaxTreatment { get; init; }      // inclusive | exclusive
    public string? HsnSacCodes { get; init; }
    public string Language { get; init; } = "en";

    // ── Award (server-owned) ────────────────────────────────────────────────
    //
    // Written only by the award endpoint, never by a client save — the service preserves these
    // across a PATCH so a stale Detail from an open browser tab cannot erase a recorded award.
    // Rule 159 requires contract award information to be published, so they are projected publicly
    // rather than staying private to the department.

    public string? AwardWinnerName { get; init; }
    public decimal? AwardedValue { get; init; }
    public string? LoaReference { get; init; }
    public DateTime? AwardedOn { get; init; }
    public string? AwardRemarks { get; init; }
}

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>
/// Create or update a draft. Every field is optional and only non-null fields are applied, so the
/// multi-step authoring form can PATCH one section at a time without carrying the rest — and
/// without a half-filled step blanking a step the officer already completed.
/// </summary>
public record SaveTenderDraftDto
{
    public string? DepartmentReference { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ScopeOfWork { get; init; }
    public string? TenderType { get; init; }
    public string? ProcurementCategory { get; init; }
    public string? FormOfContract { get; init; }
    public string? BiddingSystem { get; init; }
    public string? EvaluationMethod { get; init; }
    public decimal? TechnicalWeightage { get; init; }
    public string? GfrRuleCited { get; init; }

    public string? Category { get; init; }
    public string? State { get; init; }
    public string? City { get; init; }
    public string? Pincode { get; init; }

    public decimal? EstimatedValue { get; init; }
    public bool? ValueDisclosed { get; init; }
    public decimal? EmdAmount { get; init; }
    public decimal? EmdPercentage { get; init; }
    public string? EmdMode { get; init; }
    public string[]? EmdExemptions { get; init; }
    public decimal? TenderFee { get; init; }
    public string[]? TenderFeeExemptions { get; init; }
    public decimal? PerformanceSecurityPct { get; init; }
    public int? BidValidityDays { get; init; }

    public DateTime? DocDownloadStart { get; init; }
    public DateTime? DocDownloadEnd { get; init; }
    public DateTime? ClarificationStart { get; init; }
    public DateTime? ClarificationEnd { get; init; }
    public DateTime? PrebidMeetingAt { get; init; }
    public string? PrebidVenue { get; init; }
    public DateTime? BidSubmissionStart { get; init; }
    public DateTime? BidSubmissionEnd { get; init; }
    public DateTime? TechnicalOpeningAt { get; init; }
    public DateTime? FinancialOpeningAt { get; init; }

    public decimal? MseReservationPct { get; init; }
    public bool? MiiApplicable { get; init; }
    public decimal? MiiLocalContentPct { get; init; }
    public string? MiiClassRestriction { get; init; }
    public bool? LbsDeclarationRequired { get; init; }
    public bool? StartupRelaxation { get; init; }
    public bool? IntegrityPactApplicable { get; init; }
    public string? IntegrityPactMonitor { get; init; }
    public string? GemarptsReference { get; init; }

    /// <summary>Replaces the whole detail blob when present. Null leaves it untouched.</summary>
    public TenderDraftDetail? Detail { get; init; }
}

/// <summary>
/// Publish request. <paramref name="AcknowledgeWarnings"/> makes the officer say so explicitly when
/// warnings are outstanding. Errors are never overridable — a tender that cannot reach any supplier
/// is not a publishable tender.
/// </summary>
public record PublishTenderDto(bool AcknowledgeWarnings = false);

public record IssueCorrigendumDto(
    string Type,
    string Reason,
    SaveTenderDraftDto Changes,
    bool AcknowledgeWarnings = false);

public record SaveCommitteeMemberDto(
    Guid? UserId,
    string Committee,
    string MemberName,
    string Designation,
    string Email,
    bool IsChair);

public record AwardTenderDto(
    string WinnerName,
    decimal? AwardedValue,
    string? LoaReference,
    DateTime? AwardedOn,
    string? Remarks);

// ── Responses ────────────────────────────────────────────────────────────────

/// <summary>
/// One finding from the compliance engine. Carries the CITATION, not just a message: the point of
/// the engine is that the department's file records which rule was applied, so an auditor reads the
/// authority rather than our opinion.
/// </summary>
public record ComplianceFindingDto(
    string Code,
    string Severity,      // error | warning | info
    string Field,
    string Message,
    string Citation);

public record ValidationResultDto(
    bool CanPublish,
    string RuleSetVersion,
    ComplianceFindingDto[] Findings)
{
    public int ErrorCount => Findings.Count(f => f.Severity == "error");
    public int WarningCount => Findings.Count(f => f.Severity == "warning");
}

public record TenderDraftListItemDto(
    Guid Id,
    string ReferenceCode,
    string? DepartmentReference,
    string Title,
    string Status,
    string? Category,
    string? State,
    decimal? EstimatedValue,
    DateTime? BidSubmissionEnd,
    DateTime? PublishedAt,
    int CurrentVersion,
    int CorrigendumCount,
    string? MongoTenderId,
    DateTime UpdatedAt);

public record TenderVersionDto(
    Guid Id,
    int Version,
    string Reason,
    string ContentHash,
    string PrevChainHash,
    string ChainHash,
    string RuleSetVersion,
    string PublishedByName,
    DateTime CreatedAt);

public record CorrigendumDto(
    Guid Id,
    int CorrigendumNo,
    string Type,
    string Reason,
    FieldChangeDto[] Changes,
    DateTime? NotifiedAt,
    string CreatedByName,
    DateTime CreatedAt);

public record FieldChangeDto(string Field, string Label, string? OldValue, string? NewValue);

public record CommitteeMemberDto(
    Guid Id,
    Guid? UserId,
    string Committee,
    string MemberName,
    string Designation,
    string Email,
    bool IsChair);

public record AuditEventDto(
    long Id,
    string Action,
    string ActorName,
    string ActorRole,
    FieldChangeDto[] Changes,
    DateTime CreatedAt);

/// <summary>The full authoring view of one draft.</summary>
public record TenderDraftDetailDto
{
    public Guid Id { get; init; }
    public Guid OrgId { get; init; }
    public string ReferenceCode { get; init; } = string.Empty;
    public string? DepartmentReference { get; init; }
    public string Status { get; init; } = "draft";

    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ScopeOfWork { get; init; } = string.Empty;
    public string? TenderType { get; init; }
    public string? ProcurementCategory { get; init; }
    public string? FormOfContract { get; init; }
    public string? BiddingSystem { get; init; }
    public string? EvaluationMethod { get; init; }
    public decimal? TechnicalWeightage { get; init; }
    public string? GfrRuleCited { get; init; }

    public string? Category { get; init; }
    public string? State { get; init; }
    public string? City { get; init; }
    public string? Pincode { get; init; }

    public decimal? EstimatedValue { get; init; }
    public bool ValueDisclosed { get; init; }
    public decimal? EmdAmount { get; init; }
    public decimal? EmdPercentage { get; init; }
    public string? EmdMode { get; init; }
    public string[] EmdExemptions { get; init; } = [];
    public decimal? TenderFee { get; init; }
    public string[] TenderFeeExemptions { get; init; } = [];
    public decimal? PerformanceSecurityPct { get; init; }
    public int? BidValidityDays { get; init; }

    public DateTime? PublishedAt { get; init; }
    public DateTime? DocDownloadStart { get; init; }
    public DateTime? DocDownloadEnd { get; init; }
    public DateTime? ClarificationStart { get; init; }
    public DateTime? ClarificationEnd { get; init; }
    public DateTime? PrebidMeetingAt { get; init; }
    public string? PrebidVenue { get; init; }
    public DateTime? BidSubmissionStart { get; init; }
    public DateTime? BidSubmissionEnd { get; init; }
    public DateTime? TechnicalOpeningAt { get; init; }
    public DateTime? FinancialOpeningAt { get; init; }

    public decimal? MseReservationPct { get; init; }
    public bool MiiApplicable { get; init; }
    public decimal? MiiLocalContentPct { get; init; }
    public string? MiiClassRestriction { get; init; }
    public bool LbsDeclarationRequired { get; init; }
    public bool StartupRelaxation { get; init; }
    public bool IntegrityPactApplicable { get; init; }
    public string? IntegrityPactMonitor { get; init; }
    public string? GemarptsReference { get; init; }

    public TenderDraftDetail Detail { get; init; } = new();

    public string? MongoTenderId { get; init; }
    public int CurrentVersion { get; init; }
    public string? RuleSetVersion { get; init; }

    public CommitteeMemberDto[] Committee { get; init; } = [];
    public CorrigendumDto[] Corrigenda { get; init; } = [];
    public TenderVersionDto[] Versions { get; init; } = [];

    public string CreatedByName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// The downloadable audit file — everything an inspector needs to verify the tender independently,
/// including the hash chain so they can replay it without trusting us.
/// </summary>
public record TenderAuditFileDto(
    TenderDraftDetailDto Tender,
    TenderVersionDto[] Versions,
    CorrigendumDto[] Corrigenda,
    AuditEventDto[] Events,
    ChainVerificationDto ChainVerification,
    DateTime GeneratedAt);

/// <summary>
/// Result of replaying the hash chain over every stored version.
/// <paramref name="FirstBrokenVersion"/> is the first version whose recomputed hash disagrees with
/// the stored one, and is null when the chain is intact.
/// </summary>
public record ChainVerificationDto(
    bool Intact,
    int VersionsChecked,
    int? FirstBrokenVersion,
    string Method);

/// <summary>
/// Canonical dropdown values for the authoring form. Served from the same constants the validator
/// enforces, so the form cannot offer a value that publish would then reject.
/// </summary>
/// <remarks>
/// Every list here is backed by a CHECK constraint in migration <c>0031</c> (or, for categories and
/// states, by the taxonomy BiddingBuddyServices owns). They are served rather than left for the
/// client to hardcode because a client-side copy drifts silently: the form keeps offering a value
/// the database has stopped accepting, and the officer meets a 500 on publish with a constraint
/// name in it.
/// </remarks>
public record BuyerTenderOptionsDto(
    string[] Categories,
    string[] States,
    string[] TenderTypes,
    string[] ProcurementCategories,
    string[] BiddingSystems,
    string[] EvaluationMethods,
    string[] FormsOfContract,
    string[] EmdModes,
    string[] EntityTypes,
    string[] CorrigendumTypes,
    string[] CommitteeKinds,
    string[] MiiClassRestrictions,
    string RuleSetVersion);
