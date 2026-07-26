using System.Text.Json;
using BiddingBuddy.Bff.Core.Compliance;
using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Buyer-side tender authoring and publication. See <see cref="IBuyerTenderService"/> for the
/// Phase-1 scope boundary.
/// </summary>
public class BuyerTenderService(
    BffDbContext db,
    IBiddingBuddyServicesClient services,
    IInternalPipelineService pipeline,
    INotificationPublisher notifications,
    IMemoryCache cache,
    ILogger<BuyerTenderService> logger) : IBuyerTenderService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private const string TaxonomyCacheKey = "buyer:taxonomy";

    // ── Options ─────────────────────────────────────────────────────────────

    public async Task<BuyerTenderOptionsDto> GetOptionsAsync(CancellationToken ct = default)
    {
        var taxonomy = await GetTaxonomyAsync(ct);

        return new BuyerTenderOptionsDto(
            Categories: taxonomy.Categories,
            States: taxonomy.States,
            TenderTypes: ["open", "limited", "single", "global", "eoi", "rfp", "rfq", "two_stage"],
            ProcurementCategories: ["goods", "works", "services", "consultancy"],
            BiddingSystems: ["single_cover", "two_cover", "three_cover", "multi_cover"],
            EvaluationMethods: ["l1", "qcbs", "lcs", "fixed_budget", "single_source"],
            FormsOfContract: ["item_rate", "percentage", "lump_sum", "turnkey", "rate_contract", "piece_work"],
            EmdModes: ["bg", "dd", "online", "e_bg", "exempt"],
            EntityTypes: ["central", "state", "psu", "ulb", "autonomous", "cooperative", "trust", "private"],
            // Each of these three mirrors a CHECK constraint in migration 0031. Served rather than
            // hardcoded client-side, because a client copy drifts silently — the form keeps offering
            // a value the database has stopped accepting, and the officer meets a 500 with a
            // constraint name in it at the worst possible moment.
            CorrigendumTypes: ["date_extension", "amendment", "cancellation", "retender"],
            CommitteeKinds: ["opening", "technical", "financial", "monitor"],
            MiiClassRestrictions: ["class_i_only", "class_i_and_ii", "none"],
            RuleSetVersion: TenderComplianceRules.Version);
    }

    /// <summary>
    /// The canonical taxonomy, cached for an hour.
    ///
    /// <para>Fetched from BiddingBuddyServices rather than duplicated here — that store owns the
    /// list and silently rewrites anything off it, so a local copy that drifted would let the
    /// authoring form offer a category the store then discards, publishing a tender that matches no
    /// supplier interests at all with nothing reporting it.</para>
    ///
    /// <para>Cached because it is a compile-time constant upstream, and the authoring form asks for
    /// it on every page load.</para>
    /// </summary>
    private async Task<TenderTaxonomyDto> GetTaxonomyAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<TenderTaxonomyDto>(TaxonomyCacheKey, out var cached) && cached is not null)
            return cached;

        var taxonomy = await services.GetTenderTaxonomyAsync(ct);
        cache.Set(TaxonomyCacheKey, taxonomy, TimeSpan.FromHours(1));
        return taxonomy;
    }

    // ── Read ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TenderDraftListItemDto>> ListAsync(
        Guid orgId, string? status, string? search, CancellationToken ct = default)
    {
        var q = db.TenderDrafts.AsNoTracking().Where(d => d.OrgId == orgId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(d => d.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(d =>
                EF.Functions.ILike(d.Title, term) ||
                EF.Functions.ILike(d.ReferenceCode, term) ||
                (d.DepartmentReference != null && EF.Functions.ILike(d.DepartmentReference, term)));
        }

        var rows = await q
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new
            {
                Draft = d,
                CorrigendumCount = db.Corrigenda.Count(c => c.DraftId == d.Id),
            })
            .ToListAsync(ct);

        return rows.Select(r => new TenderDraftListItemDto(
            r.Draft.Id,
            r.Draft.ReferenceCode,
            r.Draft.DepartmentReference,
            r.Draft.Title,
            r.Draft.Status,
            r.Draft.Category,
            r.Draft.State,
            r.Draft.EstimatedValue,
            r.Draft.BidSubmissionEnd,
            r.Draft.PublishedAt,
            r.Draft.CurrentVersion,
            r.CorrigendumCount,
            r.Draft.MongoTenderId,
            r.Draft.UpdatedAt)).ToList();
    }

    public async Task<TenderDraftDetailDto?> GetAsync(Guid orgId, Guid draftId, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        return draft is null ? null : await MapDetailAsync(draft, ct);
    }

    /// <summary>
    /// Loads a draft scoped to the org.
    ///
    /// <para>The org filter is in the WHERE clause, not a check after the fetch. Returning null for
    /// "belongs to someone else" and for "does not exist" alike is what stops this endpoint being
    /// used to probe whether a given draft id exists in another department.</para>
    /// </summary>
    private Task<TenderDraft?> LoadAsync(Guid orgId, Guid draftId, CancellationToken ct)
        => db.TenderDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OrgId == orgId, ct);

    // ── Create / update ─────────────────────────────────────────────────────

    public async Task<TenderDraftDetailDto> CreateAsync(
        Guid orgId, Guid userId, string actorRole, SaveTenderDraftDto dto, CancellationToken ct = default)
    {
        var draft = new TenderDraft
        {
            OrgId = orgId,
            ReferenceCode = await NextReferenceCodeAsync(ct),
            Status = "draft",
            CreatedBy = userId,
            Detail = JsonSerializer.Serialize(new TenderDraftDetail(), Json),
        };

        Apply(draft, dto);
        db.TenderDrafts.Add(draft);

        // Linked by NAVIGATION, not by scalar id. Assigning `DraftId = draft.Id` before SaveChanges
        // hands EF a zero-GUID it cannot order the INSERTs from, which is exactly the bug that broke
        // every workspace creation once already (see the EF insert-ordering incident).
        db.TenderOwnership.Add(new TenderOwnership { OrgId = orgId, Draft = draft, Relationship = "owner" });

        await db.SaveChangesAsync(ct);

        await RecordAuditAsync(orgId, draft.Id, "created", userId, actorRole,
            [new FieldChangeDto("referenceCode", "Reference", null, draft.ReferenceCode)], ct);

        logger.LogInformation(
            "Buyer tender draft {ReferenceCode} created by {UserId} in org {OrgId}",
            draft.ReferenceCode, userId, orgId);

        return await MapDetailAsync(draft, ct);
    }

    /// <summary>
    /// Allocates the next reference code from a Postgres sequence.
    /// </summary>
    /// <remarks>
    /// A sequence rather than <c>MAX(...) + 1</c>: two officers clicking "New tender" in the same
    /// second would race on the latter, and the unique index would turn that race into a 500 on an
    /// empty form. Sequences also do not roll back, so a discarded draft burns a number — which is
    /// correct for an audit-facing identifier, since a gap is visible and a reused number is not.
    /// </remarks>
    private async Task<string> NextReferenceCodeAsync(CancellationToken ct)
    {
        var next = await db.Database
            .SqlQuery<long>($"SELECT nextval('tender_reference_seq') AS \"Value\"")
            .SingleAsync(ct);

        return $"TA-{DateTime.UtcNow.Year}-{next:D6}";
    }

    public async Task<TenderDraftDetailDto?> UpdateAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, SaveTenderDraftDto dto,
        CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        if (draft.Status is "awarded" or "cancelled")
            throw new InvalidOperationException(
                $"This tender is {draft.Status} and can no longer be edited.");

        // A PUBLISHED tender is not edited in place. Changing what suppliers were told, without a
        // corrigendum and without notifying them, is the exact failure the corrigendum workflow
        // exists to prevent — and it would leave the hash chain describing a document that no
        // longer matches what is being served.
        if (draft.Status == "published")
            throw new InvalidOperationException(
                "This tender is published. Amend it by issuing a corrigendum so bidders are notified.");

        var changes = Diff(draft, dto);
        Apply(draft, dto);
        await db.SaveChangesAsync(ct);

        if (changes.Count > 0)
            await RecordAuditAsync(orgId, draft.Id, "updated", userId, actorRole, changes, ct);

        return await MapDetailAsync(draft, ct);
    }

    public async Task<bool> DeleteDraftAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return false;

        // No hard delete of anything published. The audit trail's whole value is that it cannot be
        // made to disappear; a published tender is cancelled, which is itself recorded.
        if (draft.Status != "draft")
            throw new InvalidOperationException(
                "Only an unpublished draft can be deleted. Cancel the tender instead.");

        db.TenderDrafts.Remove(draft);
        await db.SaveChangesAsync(ct);

        // Written AFTER the delete and deliberately outlives it — audit_events holds no FK to its
        // subject precisely so this row survives.
        await RecordAuditAsync(orgId, draftId, "deleted", userId, actorRole,
            [new FieldChangeDto("status", "Status", "draft", "deleted")], ct);

        return true;
    }

    // ── Validation ──────────────────────────────────────────────────────────

    public async Task<ValidationResultDto?> ValidateAsync(Guid orgId, Guid draftId, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        return draft is null ? null : await RunValidationAsync(draft, selfPublishing: false, ct);
    }

    private async Task<ValidationResultDto> RunValidationAsync(
        TenderDraft draft, bool selfPublishing, CancellationToken ct)
    {
        var taxonomy = await GetTaxonomyAsync(ct);
        var detail = DeserializeDetail(draft.Detail);

        var findings = TenderComplianceRules.Evaluate(
            draft,
            detail,
            taxonomy.Categories.ToHashSet(StringComparer.Ordinal),
            taxonomy.States.ToHashSet(StringComparer.Ordinal),
            selfPublishing,
            DateTime.UtcNow);

        return new ValidationResultDto(
            CanPublish: findings.All(f => f.Severity != "error"),
            RuleSetVersion: TenderComplianceRules.Version,
            Findings: [.. findings]);
    }

    // ── Publish ─────────────────────────────────────────────────────────────

    public async Task<TenderDraftDetailDto?> PublishAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, PublishTenderDto dto,
        CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        if (draft.Status != "draft")
            throw new InvalidOperationException($"This tender is already {draft.Status}.");

        var validation = await RunValidationAsync(draft, selfPublishing: draft.CreatedBy == userId, ct);

        // Errors are never overridable; warnings are, but only by saying so. An officer who
        // acknowledges a short notice period has made a decision that the audit file now records —
        // which is the point.
        if (!validation.CanPublish || (validation.WarningCount > 0 && !dto.AcknowledgeWarnings))
            throw new ValidationFailedException(validation);

        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        var now = DateTime.UtcNow;

        draft.Status = "published";
        draft.PublishedAt = now;
        draft.PublishedBy = userId;
        draft.RuleSetVersion = TenderComplianceRules.Version;

        // The version row is appended BEFORE the projection. If projecting to Mongo fails, the
        // department has an auditable record that they published and a retryable state — the
        // opposite order would leave a tender visible to suppliers with no version behind it, which
        // is the failure that cannot be reconciled afterwards.
        var version = await AppendVersionAsync(draft, org, "published", userId, ct);
        await db.SaveChangesAsync(ct);

        await ProjectAsync(draft, org, ct);
        await db.SaveChangesAsync(ct);

        await RecordAuditAsync(orgId, draft.Id, "published", userId, actorRole,
        [
            new FieldChangeDto("status", "Status", "draft", "published"),
            new FieldChangeDto("version", "Version", "0", version.Version.ToString()),
            new FieldChangeDto("chainHash", "Chain hash", null, version.ChainHash),
            new FieldChangeDto("acknowledgedWarnings", "Warnings acknowledged",
                null, validation.WarningCount.ToString()),
        ], ct);

        await NotifyPublishedAsync(draft, org, userId, ct);

        logger.LogInformation(
            "Buyer tender {ReferenceCode} published as version {Version} (mongo {MongoId}) by {UserId}",
            draft.ReferenceCode, version.Version, draft.MongoTenderId, userId);

        return await MapDetailAsync(draft, ct);
    }

    /// <summary>
    /// Appends the next hash-chained version. Caller saves.
    /// </summary>
    private async Task<TenderVersion> AppendVersionAsync(
        TenderDraft draft, Organization org, string reason, Guid userId, CancellationToken ct)
    {
        var prev = await db.TenderVersions
            .Where(v => v.DraftId == draft.Id)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        var snapshot = JsonSerializer.Serialize(BuildSnapshot(draft, org), Json);
        var contentHash = TenderHashChain.ComputeContentHash(snapshot);
        var prevChainHash = prev?.ChainHash ?? string.Empty;

        var version = new TenderVersion
        {
            Draft = draft,                     // navigation, not scalar — see CreateAsync
            Version = (prev?.Version ?? 0) + 1,
            Reason = reason,
            Snapshot = snapshot,
            ContentHash = contentHash,
            PrevChainHash = prevChainHash,
            ChainHash = TenderHashChain.ComputeChainHash(prevChainHash, contentHash),
            RuleSetVersion = TenderComplianceRules.Version,
            PublishedBy = userId,
        };

        db.TenderVersions.Add(version);
        draft.CurrentVersion = version.Version;
        return version;
    }

    /// <summary>
    /// The canonical snapshot that gets hashed. Everything a reader needs to reconstruct what was
    /// published, and nothing that varies between two reads of the same state — no timestamps
    /// generated here, no ids that are allocated at save time.
    /// </summary>
    private object BuildSnapshot(TenderDraft d, Organization org) => new
    {
        referenceCode = d.ReferenceCode,
        departmentReference = d.DepartmentReference,
        buyer = new
        {
            name = org.Name,
            entityType = org.EntityType,
            ministry = org.Ministry,
            department = org.Department,
            office = org.Office,
            state = org.State,
        },
        d.Title,
        d.Description,
        d.ScopeOfWork,
        d.TenderType,
        d.ProcurementCategory,
        d.FormOfContract,
        d.BiddingSystem,
        d.EvaluationMethod,
        d.TechnicalWeightage,
        d.GfrRuleCited,
        d.Category,
        d.State,
        d.City,
        d.Pincode,
        d.EstimatedValue,
        d.ValueDisclosed,
        d.EmdAmount,
        d.EmdPercentage,
        d.EmdMode,
        d.EmdExemptions,
        d.TenderFee,
        d.PerformanceSecurityPct,
        d.BidValidityDays,
        dates = new
        {
            d.DocDownloadStart,
            d.DocDownloadEnd,
            d.ClarificationStart,
            d.ClarificationEnd,
            d.PrebidMeetingAt,
            d.BidSubmissionStart,
            d.BidSubmissionEnd,
            d.TechnicalOpeningAt,
            d.FinancialOpeningAt,
        },
        compliance = new
        {
            d.MseReservationPct,
            d.MiiApplicable,
            d.MiiLocalContentPct,
            d.MiiClassRestriction,
            d.LbsDeclarationRequired,
            d.StartupRelaxation,
            d.IntegrityPactApplicable,
            d.IntegrityPactMonitor,
            d.GemarptsReference,
        },
        detail = DeserializeDetail(d.Detail),
    };

    /// <summary>
    /// Projects the published tender into both stores that make it visible:
    /// Mongo (the public portal, /explore, SEO hubs, tender detail) and Postgres
    /// (the supplier matching rail).
    /// </summary>
    /// <remarks>
    /// This is where "a department publishing with us reaches suppliers on day one" actually
    /// happens, and it needs BOTH writes — the read surfaces resolve out of Mongo, but
    /// <c>TenderMatchScanWorker</c> scans the Postgres <c>tenders</c> table for rows with
    /// <c>alerts_scanned_at IS NULL</c>. A Mongo-only projection would be publicly visible and would
    /// alert nobody.
    /// </remarks>
    private async Task ProjectAsync(TenderDraft draft, Organization org, CancellationToken ct)
    {
        var mongoId = await services.UpsertDirectTenderAsync(BuildProjection(draft, org), ct);
        draft.MongoTenderId ??= mongoId;

        var corrigendumCount = await db.Corrigenda.CountAsync(c => c.DraftId == draft.Id, ct);

        // Reuses the pipeline's own upsert rather than writing the row here. That path already
        // detects a moved closing date or a raised corrigendum count and notifies every org with a
        // bid on the tender — which is exactly the right behaviour for a corrigendum, and
        // re-implementing the mirror here would silently lose it.
        await pipeline.UpsertTenderAsync(new UpsertTenderDto(
            GemTenderId: draft.ReferenceCode,
            MongoTenderId: mongoId,
            Title: draft.Title,
            Description: draft.Description,
            BuyerOrgName: org.Name,
            BuyerOrgIdGem: null,
            State: draft.State,
            City: draft.City,
            Category: draft.Category,
            SubCategory: null,
            TenderValue: draft.ValueDisclosed ? draft.EstimatedValue : null,
            EmdAmount: draft.EmdAmount,
            PublishedDate: ToDateOnly(draft.PublishedAt),
            ClosingDate: ToDateOnly(draft.BidSubmissionEnd),
            DeliveryDays: null,
            Status: draft.Status switch
            {
                "cancelled" => "cancelled",
                "awarded" => "awarded",
                _ => "active",
            },
            CorrigendumCount: corrigendumCount,
            AiScore: null,
            EligibilityScore: null,
            WinProbability: null,
            RiskScore: null,
            AiSummary: null,
            AiTags: null,
            RawData: null,
            Platform: "direct"), ct);
    }

    private DirectTenderUpsertDto BuildProjection(TenderDraft d, Organization org)
    {
        var detail = DeserializeDetail(d.Detail);

        return new DirectTenderUpsertDto
        {
            Source = new DirectTenderSourceDto
            {
                Platform = "direct",
                PlatformTenderId = d.ReferenceCode,
                ExternalBidNumber = d.DepartmentReference ?? string.Empty,
                ImportedAt = DateTimeOffset.UtcNow,
                Version = Math.Max(d.CurrentVersion, 1),
            },
            Title = d.Title,
            Summary = d.Description,
            Category = new DirectTenderCategoryDto
            {
                Primary = d.Category ?? string.Empty,
                Tags = d.ProcurementCategory is { } pc ? [pc] : [],
            },
            Organization = new DirectTenderOrganizationDto
            {
                Ministry = org.Ministry ?? string.Empty,
                Department = org.Department ?? string.Empty,
                Organization = org.Name,
                Office = org.Office ?? string.Empty,
                BuyerName = detail.Contact?.AuthorityName ?? string.Empty,
                BuyerDesignation = detail.Contact?.Designation ?? string.Empty,
                BuyerContact = detail.Contact is null
                    ? []
                    : new Dictionary<string, string>
                    {
                        ["email"] = detail.Contact.Email,
                        ["phone"] = detail.Contact.Phone,
                    },
            },
            Location = new DirectTenderLocationDto
            {
                State = d.State ?? string.Empty,
                City = d.City ?? string.Empty,
            },
            Timeline = new DirectTenderTimelineDto
            {
                PublishedAt = ToOffset(d.PublishedAt),
                BidStartAt = ToOffset(d.BidSubmissionStart),
                BidEndAt = ToOffset(d.BidSubmissionEnd),
                BidOpeningAt = ToOffset(d.TechnicalOpeningAt),
                ValidityDays = d.BidValidityDays,
                ContractDuration = detail.DeliveryPeriod,
            },
            Financial = new DirectTenderFinancialDto
            {
                // Withheld when the department chose not to disclose it. Projecting it anyway would
                // publish, to every supplier, the one number they explicitly decided not to share.
                EstimatedBidValue = d.ValueDisclosed ? d.EstimatedValue : null,
                Emd = new DirectTenderEmdDto
                {
                    Required = d.EmdAmount > 0 || d.EmdPercentage > 0,
                    Amount = d.EmdAmount,
                },
                Epbg = new DirectTenderEpbgDto
                {
                    Required = d.PerformanceSecurityPct > 0,
                    Percentage = (double?)d.PerformanceSecurityPct,
                },
            },
            Commercial = new DirectTenderCommercialDto
            {
                EvaluationMethod = d.EvaluationMethod ?? string.Empty,
                BidType = d.TenderType ?? string.Empty,
                ReverseAuction = new DirectTenderReverseAuctionDto { Enabled = detail.ReverseAuction },
            },
            Compliance = new DirectTenderComplianceDto
            {
                MiiPreference = d.MiiApplicable,
                MsePreference = d.MseReservationPct > 0,
                PurchasePreferencePercent = (double?)d.MseReservationPct,
            },
            Qualification = new DirectTenderQualificationDto
            {
                ExperienceYears = detail.Eligibility?.ExperienceLookbackYears,
                StartupRelaxation = d.StartupRelaxation,
                MseRelaxation = d.EmdExemptions.Contains("mse"),
                RequiredDocuments = detail.Eligibility?.RequiredRegistrations ?? [],
            },
            Items = [.. detail.Items.Select(i => new DirectTenderItemDto
            {
                Name = i.Description,
                Category = d.Category ?? string.Empty,
                Unit = i.Unit,
                Quantity = (double)i.Quantity,
                UnitPrice = i.EstimatedRate,
                TotalAmount = i.Amount,
                Specifications = i.ItemCode,
            })],
            TechnicalSpecifications = [.. detail.TechnicalSpecifications.Select(s => new DirectTenderTechSpecDto
            {
                Group = s.Group,
                Name = s.Name,
                Value = s.Value,
            })],
            Documents = [.. detail.Attachments.Select(a => new DirectTenderDocumentDto
            {
                Type = a.Type,
                FileName = a.FileName,
                DocumentId = a.Id,
                // R2, not the AWS tender bucket — see DirectTenderDocumentDto.S3Bucket.
                S3Bucket = "bidding-buddy",
                S3Key = a.ObjectKey,
            })],
            Status = new DirectTenderStatusDto
            {
                State = d.Status switch
                {
                    "cancelled" => "cancelled",
                    "awarded" => "awarded",
                    _ => "open",
                },
                IsCancelled = d.Status == "cancelled",
            },
        };
    }

    // ── Corrigendum ─────────────────────────────────────────────────────────

    public async Task<TenderDraftDetailDto?> IssueCorrigendumAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, IssueCorrigendumDto dto,
        CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        if (draft.Status != "published")
            throw new InvalidOperationException(
                $"Only a published tender can be amended by corrigendum (this one is {draft.Status}).");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException(
                "A corrigendum needs a reason — an unexplained amendment to a live tender is the "
                + "single most audit-sensitive change a procuring officer can make.");

        var changes = Diff(draft, dto.Changes);
        if (changes.Count == 0)
            throw new InvalidOperationException("This corrigendum changes nothing.");

        var previousSubmissionEnd = draft.BidSubmissionEnd;
        Apply(draft, dto.Changes);

        var validation = await RunValidationAsync(draft, selfPublishing: false, ct);
        if (!validation.CanPublish || (validation.WarningCount > 0 && !dto.AcknowledgeWarnings))
            throw new ValidationFailedException(validation);

        // Minimum re-notice. Extending a deadline to tomorrow leaves bidders who already submitted
        // no real chance to revise, which is the objection an extension is usually meant to answer.
        if (draft.BidSubmissionEnd is { } newEnd && newEnd != previousSubmissionEnd)
        {
            var notice = (newEnd - DateTime.UtcNow).TotalDays;
            if (notice < TenderComplianceRules.MinCorrigendumNoticeDays && !dto.AcknowledgeWarnings)
                throw new ValidationFailedException(new ValidationResultDto(
                    CanPublish: false,
                    RuleSetVersion: TenderComplianceRules.Version,
                    Findings:
                    [
                        new ComplianceFindingDto(
                            "CORRIGENDUM_NOTICE_SHORT", "error", "bidSubmissionEnd",
                            $"The revised deadline is {notice:F1} days away. Bidders need at least "
                            + $"{TenderComplianceRules.MinCorrigendumNoticeDays} days after an amendment to respond.",
                            "CVC guidance on tender transparency and minimum bidding time"),
                    ]));
        }

        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);

        var nextNo = await db.Corrigenda.CountAsync(c => c.DraftId == draft.Id, ct) + 1;
        var version = await AppendVersionAsync(draft, org, "corrigendum", userId, ct);

        var corrigendum = new Corrigendum
        {
            Draft = draft,
            CorrigendumNo = nextNo,
            Type = dto.Type,
            Reason = dto.Reason,
            Changes = JsonSerializer.Serialize(changes, Json),
            CreatedBy = userId,
        };
        db.Corrigenda.Add(corrigendum);
        await db.SaveChangesAsync(ct);

        corrigendum.VersionId = version.Id;

        await ProjectAsync(draft, org, ct);
        await db.SaveChangesAsync(ct);

        await RecordAuditAsync(orgId, draft.Id, "corrigendum_issued", userId, actorRole, changes, ct);

        await NotifyCorrigendumAsync(draft, org, corrigendum, changes, ct);

        return await MapDetailAsync(draft, ct);
    }

    // ── Award / cancel ──────────────────────────────────────────────────────

    public async Task<TenderDraftDetailDto?> AwardAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, AwardTenderDto dto,
        CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        if (draft.Status != "published")
            throw new InvalidOperationException($"Only a published tender can be awarded (this one is {draft.Status}).");

        if (string.IsNullOrWhiteSpace(dto.WinnerName))
            throw new InvalidOperationException("Name the successful bidder.");

        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        var detail = DeserializeDetail(draft.Detail);

        // The award is recorded in the detail blob and stamped into the version chain. Rule 159
        // requires contract award information to be published, so it goes into the projection too
        // rather than staying private to the department.
        draft.Detail = JsonSerializer.Serialize(detail with
        {
            AwardWinnerName = dto.WinnerName,
            AwardedValue = dto.AwardedValue,
            LoaReference = dto.LoaReference,
            AwardedOn = dto.AwardedOn ?? DateTime.UtcNow,
            AwardRemarks = dto.Remarks,
        }, Json);
        draft.Status = "awarded";

        await AppendVersionAsync(draft, org, "award", userId, ct);
        await db.SaveChangesAsync(ct);

        await ProjectAsync(draft, org, ct);
        await db.SaveChangesAsync(ct);

        await RecordAuditAsync(orgId, draft.Id, "awarded", userId, actorRole,
        [
            new FieldChangeDto("status", "Status", "published", "awarded"),
            new FieldChangeDto("winner", "Successful bidder", null, dto.WinnerName),
            new FieldChangeDto("awardedValue", "Awarded value", null, dto.AwardedValue?.ToString()),
        ], ct);

        return await MapDetailAsync(draft, ct);
    }

    public async Task<TenderDraftDetailDto?> CancelAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, string reason, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        if (draft.Status is "cancelled" or "awarded")
            throw new InvalidOperationException($"This tender is already {draft.Status}.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Cancelling a tender requires a recorded reason.");

        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        var wasPublished = draft.Status == "published";
        var previous = draft.Status;

        draft.Status = "cancelled";
        await AppendVersionAsync(draft, org, "cancellation", userId, ct);

        var changes = new List<FieldChangeDto>
        {
            new("status", "Status", previous, "cancelled"),
            new("reason", "Reason", null, reason),
        };

        // A cancellation is itself a corrigendum when the tender was public: suppliers were told it
        // existed, so they have to be told it does not.
        if (wasPublished)
        {
            var nextNo = await db.Corrigenda.CountAsync(c => c.DraftId == draft.Id, ct) + 1;
            var corrigendum = new Corrigendum
            {
                Draft = draft,
                CorrigendumNo = nextNo,
                Type = "cancellation",
                Reason = reason,
                Changes = JsonSerializer.Serialize(changes, Json),
                CreatedBy = userId,
            };
            db.Corrigenda.Add(corrigendum);
            await db.SaveChangesAsync(ct);

            await ProjectAsync(draft, org, ct);
            await db.SaveChangesAsync(ct);
            await NotifyCorrigendumAsync(draft, org, corrigendum, changes, ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        await RecordAuditAsync(orgId, draft.Id, "cancelled", userId, actorRole, changes, ct);

        return await MapDetailAsync(draft, ct);
    }

    // ── Committee ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CommitteeMemberDto>> SetCommitteeAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId,
        IReadOnlyList<SaveCommitteeMemberDto> members, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct)
            ?? throw new InvalidOperationException("Tender not found.");

        var existing = await db.TenderCommitteeMembers.Where(m => m.DraftId == draftId).ToListAsync(ct);
        db.TenderCommitteeMembers.RemoveRange(existing);

        var added = members.Select(m => new TenderCommitteeMember
        {
            Draft = draft,
            UserId = m.UserId,
            Committee = m.Committee,
            MemberName = m.MemberName,
            Designation = m.Designation,
            Email = m.Email,
            IsChair = m.IsChair,
        }).ToList();

        db.TenderCommitteeMembers.AddRange(added);
        await db.SaveChangesAsync(ct);

        await RecordAuditAsync(orgId, draftId, "committee_changed", userId, actorRole,
        [
            new FieldChangeDto("committee", "Committee members",
                existing.Count.ToString(), added.Count.ToString()),
        ], ct);

        return [.. added.Select(MapCommittee)];
    }

    // ── Audit file ──────────────────────────────────────────────────────────

    public async Task<TenderAuditFileDto?> GetAuditFileAsync(
        Guid orgId, Guid draftId, CancellationToken ct = default)
    {
        var draft = await LoadAsync(orgId, draftId, ct);
        if (draft is null) return null;

        var versions = await db.TenderVersions.AsNoTracking()
            .Where(v => v.DraftId == draftId)
            .OrderBy(v => v.Version)
            .ToListAsync(ct);

        var (intact, firstBroken) = TenderHashChain.Verify(
            versions.Select(v => (v.Version, v.Snapshot, v.ContentHash, v.PrevChainHash, v.ChainHash)));

        if (!intact)
            logger.LogError(
                "Hash chain verification FAILED for tender {ReferenceCode} at version {Version} — "
                + "the stored version history does not match its own hashes",
                draft.ReferenceCode, firstBroken);

        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.EntityType == "tender_draft" && e.EntityId == draftId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var detail = await MapDetailAsync(draft, ct);

        return new TenderAuditFileDto(
            Tender: detail,
            Versions: [.. await MapVersionsAsync(versions, ct)],
            Corrigenda: detail.Corrigenda,
            Events:
            [
                .. events.Select(e => new AuditEventDto(
                    e.Id, e.Action, e.ActorName, e.ActorRole,
                    DeserializeChanges(e.Changes), e.CreatedAt))
            ],
            ChainVerification: new ChainVerificationDto(
                intact, versions.Count, firstBroken, TenderHashChain.Method),
            GeneratedAt: DateTime.UtcNow);
    }

    // ── Notifications ───────────────────────────────────────────────────────

    private async Task NotifyPublishedAsync(TenderDraft draft, Organization org, Guid userId, CancellationToken ct)
    {
        try
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return;

            var matched = draft.MongoTenderId is null
                ? 0
                : await db.TenderMatches.CountAsync(
                    m => m.Tender.MongoTenderId == draft.MongoTenderId, ct);

            await notifications.SendAsync(new SendNotificationDto(
                Category: "Transactional",
                TemplateCode: "TENDER_PUBLISHED",
                UserId: userId,
                Payload: new Dictionary<string, object>
                {
                    ["FirstName"] = FirstName(user.Name),
                    ["TenderTitle"] = draft.Title,
                    ["ReferenceCode"] = draft.ReferenceCode,
                    ["DepartmentReference"] = draft.DepartmentReference ?? string.Empty,
                    ["SubmissionEnd"] = Format(draft.BidSubmissionEnd),
                    ["PublishedAt"] = Format(draft.PublishedAt),
                    ["Version"] = draft.CurrentVersion,
                    ["DraftId"] = draft.Id.ToString(),
                    // Honest about the fact that matching runs on a schedule: claiming a supplier
                    // count at publish time would be a number invented before the scan has run.
                    ["MatchedSupplierLine"] = matched > 0
                        ? $"{matched} supplier(s) tracking this category have already been alerted."
                        : "Suppliers whose interests match will be alerted at the next scan.",
                    ["Url"] = draft.MongoTenderId is { } mid ? $"/tenders/{mid}" : $"/buyer/tenders/{draft.Id}",
                },
                Recipients:
                [
                    new NotificationRecipientDto("Email", user.Email),
                    new NotificationRecipientDto("InApp", userId.ToString()),
                ]), ct);
        }
        catch (Exception ex)
        {
            // A notification failure must never fail a publication that already committed. The
            // tender is live and the version is written; the email is the least important artefact
            // in this transaction.
            logger.LogWarning(ex,
                "Publish notification failed for tender {ReferenceCode} — the tender IS published",
                draft.ReferenceCode);
        }
    }

    /// <summary>
    /// Tells the suppliers our own matching rail already alerted about this tender that it changed.
    /// </summary>
    /// <remarks>
    /// The recipient set is deliberately "whoever was told this tender exists" — the orgs holding a
    /// <c>tender_matches</c> row — rather than everyone matching the interest rules today. A supplier
    /// who was never told about the tender does not need to hear that its deadline moved.
    /// </remarks>
    private async Task NotifyCorrigendumAsync(
        TenderDraft draft, Organization org, Corrigendum corrigendum,
        IReadOnlyList<FieldChangeDto> changes, CancellationToken ct)
    {
        try
        {
            if (draft.MongoTenderId is null) return;

            var recipients = await db.TenderMatches.AsNoTracking()
                .Where(m => m.Tender.MongoTenderId == draft.MongoTenderId)
                .Join(db.OrgMembers.Where(om => om.Status == "active"),
                    m => m.OrgId, om => om.OrgId, (m, om) => om)
                .Join(db.Users, om => om.UserId, u => u.Id, (om, u) => new { u.Id, u.Name, u.Email })
                .Distinct()
                .ToListAsync(ct);

            if (recipients.Count == 0)
            {
                logger.LogInformation(
                    "Corrigendum {No} on {ReferenceCode}: no supplier was ever alerted to this tender, "
                    + "so there is nobody to notify",
                    corrigendum.CorrigendumNo, draft.ReferenceCode);
                return;
            }

            foreach (var r in recipients)
            {
                await notifications.SendAsync(new SendNotificationDto(
                    Category: "Transactional",
                    TemplateCode: "TENDER_CORRIGENDUM",
                    UserId: r.Id,
                    Payload: new Dictionary<string, object>
                    {
                        ["FirstName"] = FirstName(r.Name),
                        ["TenderTitle"] = draft.Title,
                        ["ReferenceCode"] = draft.ReferenceCode,
                        ["BuyerName"] = org.Name,
                        ["CorrigendumNo"] = corrigendum.CorrigendumNo,
                        ["CorrigendumType"] = Humanize(corrigendum.Type),
                        ["Reason"] = corrigendum.Reason,
                        ["Changes"] = changes,
                        ["NewSubmissionEnd"] = changes.Any(c => c.Field == "bidSubmissionEnd")
                            ? Format(draft.BidSubmissionEnd)
                            : string.Empty,
                        ["MongoTenderId"] = draft.MongoTenderId,
                        ["Url"] = $"/tenders/{draft.MongoTenderId}",
                    },
                    Recipients:
                    [
                        new NotificationRecipientDto("Email", r.Email),
                        new NotificationRecipientDto("InApp", r.Id.ToString()),
                    ]), ct);
            }

            corrigendum.NotifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // NotifiedAt stays null, which is exactly the signal the UI surfaces — an extension
            // nobody was told about is not really an extension, and that should be visible.
            logger.LogWarning(ex,
                "Corrigendum notification failed for {ReferenceCode} corrigendum {No}",
                draft.ReferenceCode, corrigendum.CorrigendumNo);
        }
    }

    // ── Audit ───────────────────────────────────────────────────────────────

    private async Task RecordAuditAsync(
        Guid orgId, Guid entityId, string action, Guid userId, string actorRole,
        IReadOnlyList<FieldChangeDto> changes, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            OrgId = orgId,
            EntityType = "tender_draft",
            EntityId = entityId,
            Action = action,
            ActorId = userId,
            // Captured now, not resolved at read time — see AuditEvent's remarks.
            ActorName = user?.Name ?? "(unknown)",
            ActorRole = actorRole,
            Changes = JsonSerializer.Serialize(changes, Json),
        });

        await db.SaveChangesAsync(ct);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Applies only the non-null fields of <paramref name="dto"/>.
    /// </summary>
    /// <remarks>
    /// Null means "not supplied", never "clear this". That is what lets the multi-step form PATCH
    /// one section without carrying the others — and it means a field genuinely cannot be blanked
    /// through this path, which is the right trade for an audit-facing document.
    /// </remarks>
    private void Apply(TenderDraft d, SaveTenderDraftDto s)
    {
        if (s.DepartmentReference is not null) d.DepartmentReference = s.DepartmentReference;
        if (s.Title is not null) d.Title = s.Title;
        if (s.Description is not null) d.Description = s.Description;
        if (s.ScopeOfWork is not null) d.ScopeOfWork = s.ScopeOfWork;
        if (s.TenderType is not null) d.TenderType = s.TenderType;
        if (s.ProcurementCategory is not null) d.ProcurementCategory = s.ProcurementCategory;
        if (s.FormOfContract is not null) d.FormOfContract = s.FormOfContract;
        if (s.BiddingSystem is not null) d.BiddingSystem = s.BiddingSystem;
        if (s.EvaluationMethod is not null) d.EvaluationMethod = s.EvaluationMethod;
        if (s.TechnicalWeightage is not null) d.TechnicalWeightage = s.TechnicalWeightage;
        if (s.GfrRuleCited is not null) d.GfrRuleCited = s.GfrRuleCited;

        if (s.Category is not null) d.Category = s.Category;
        if (s.State is not null) d.State = s.State;
        if (s.City is not null) d.City = s.City;
        if (s.Pincode is not null) d.Pincode = s.Pincode;

        if (s.EstimatedValue is not null) d.EstimatedValue = s.EstimatedValue;
        if (s.ValueDisclosed is not null) d.ValueDisclosed = s.ValueDisclosed.Value;
        if (s.EmdAmount is not null) d.EmdAmount = s.EmdAmount;
        if (s.EmdPercentage is not null) d.EmdPercentage = s.EmdPercentage;
        if (s.EmdMode is not null) d.EmdMode = s.EmdMode;
        if (s.EmdExemptions is not null) d.EmdExemptions = s.EmdExemptions;
        if (s.TenderFee is not null) d.TenderFee = s.TenderFee;
        if (s.TenderFeeExemptions is not null) d.TenderFeeExemptions = s.TenderFeeExemptions;
        if (s.PerformanceSecurityPct is not null) d.PerformanceSecurityPct = s.PerformanceSecurityPct;
        if (s.BidValidityDays is not null) d.BidValidityDays = s.BidValidityDays;

        if (s.DocDownloadStart is not null) d.DocDownloadStart = Utc(s.DocDownloadStart);
        if (s.DocDownloadEnd is not null) d.DocDownloadEnd = Utc(s.DocDownloadEnd);
        if (s.ClarificationStart is not null) d.ClarificationStart = Utc(s.ClarificationStart);
        if (s.ClarificationEnd is not null) d.ClarificationEnd = Utc(s.ClarificationEnd);
        if (s.PrebidMeetingAt is not null) d.PrebidMeetingAt = Utc(s.PrebidMeetingAt);
        if (s.PrebidVenue is not null) d.PrebidVenue = s.PrebidVenue;
        if (s.BidSubmissionStart is not null) d.BidSubmissionStart = Utc(s.BidSubmissionStart);
        if (s.BidSubmissionEnd is not null) d.BidSubmissionEnd = Utc(s.BidSubmissionEnd);
        if (s.TechnicalOpeningAt is not null) d.TechnicalOpeningAt = Utc(s.TechnicalOpeningAt);
        if (s.FinancialOpeningAt is not null) d.FinancialOpeningAt = Utc(s.FinancialOpeningAt);

        if (s.MseReservationPct is not null) d.MseReservationPct = s.MseReservationPct;
        if (s.MiiApplicable is not null) d.MiiApplicable = s.MiiApplicable.Value;
        if (s.MiiLocalContentPct is not null) d.MiiLocalContentPct = s.MiiLocalContentPct;
        if (s.MiiClassRestriction is not null) d.MiiClassRestriction = s.MiiClassRestriction;
        if (s.LbsDeclarationRequired is not null) d.LbsDeclarationRequired = s.LbsDeclarationRequired.Value;
        if (s.StartupRelaxation is not null) d.StartupRelaxation = s.StartupRelaxation.Value;
        if (s.IntegrityPactApplicable is not null) d.IntegrityPactApplicable = s.IntegrityPactApplicable.Value;
        if (s.IntegrityPactMonitor is not null) d.IntegrityPactMonitor = s.IntegrityPactMonitor;
        if (s.GemarptsReference is not null) d.GemarptsReference = s.GemarptsReference;

        if (s.Detail is not null)
        {
            // The award fields are server-owned and live in the same blob. A client PATCH that
            // carried a stale Detail would otherwise silently erase a recorded award.
            var current = DeserializeDetail(d.Detail);
            d.Detail = JsonSerializer.Serialize(s.Detail with
            {
                AwardWinnerName = current.AwardWinnerName,
                AwardedValue = current.AwardedValue,
                LoaReference = current.LoaReference,
                AwardedOn = current.AwardedOn,
                AwardRemarks = current.AwardRemarks,
            }, Json);
        }
    }

    /// <summary>
    /// Field-level diff of what <paramref name="s"/> would change on <paramref name="d"/>.
    /// Drives both the corrigendum's public diff view and the audit trail.
    /// </summary>
    private static List<FieldChangeDto> Diff(TenderDraft d, SaveTenderDraftDto s)
    {
        var changes = new List<FieldChangeDto>();

        void Cmp<T>(string field, string label, T? incoming, T? current)
        {
            if (incoming is null) return;
            var a = Str(current);
            var b = Str(incoming);
            if (a != b) changes.Add(new FieldChangeDto(field, label, a, b));
        }

        Cmp("title", "Title", s.Title, d.Title);
        Cmp("description", "Description", s.Description, d.Description);
        Cmp("scopeOfWork", "Scope of work", s.ScopeOfWork, d.ScopeOfWork);
        Cmp("departmentReference", "Department reference", s.DepartmentReference, d.DepartmentReference);
        Cmp("tenderType", "Tender type", s.TenderType, d.TenderType);
        Cmp("procurementCategory", "Procurement category", s.ProcurementCategory, d.ProcurementCategory);
        Cmp("formOfContract", "Form of contract", s.FormOfContract, d.FormOfContract);
        Cmp("biddingSystem", "Bidding system", s.BiddingSystem, d.BiddingSystem);
        Cmp("evaluationMethod", "Evaluation method", s.EvaluationMethod, d.EvaluationMethod);
        Cmp("technicalWeightage", "Technical weightage", s.TechnicalWeightage, d.TechnicalWeightage);
        Cmp("gfrRuleCited", "GFR rule cited", s.GfrRuleCited, d.GfrRuleCited);
        Cmp("category", "Category", s.Category, d.Category);
        Cmp("state", "State", s.State, d.State);
        Cmp("city", "City", s.City, d.City);
        Cmp("pincode", "PIN code", s.Pincode, d.Pincode);
        Cmp("estimatedValue", "Estimated value", s.EstimatedValue, d.EstimatedValue);
        Cmp("valueDisclosed", "Value disclosed", s.ValueDisclosed, d.ValueDisclosed);
        Cmp("emdAmount", "EMD amount", s.EmdAmount, d.EmdAmount);
        Cmp("emdPercentage", "EMD percentage", s.EmdPercentage, d.EmdPercentage);
        Cmp("emdMode", "EMD mode", s.EmdMode, d.EmdMode);
        Cmp("tenderFee", "Tender fee", s.TenderFee, d.TenderFee);
        Cmp("performanceSecurityPct", "Performance security %", s.PerformanceSecurityPct, d.PerformanceSecurityPct);
        Cmp("bidValidityDays", "Bid validity (days)", s.BidValidityDays, d.BidValidityDays);
        Cmp("docDownloadStart", "Document download opens", s.DocDownloadStart, d.DocDownloadStart);
        Cmp("docDownloadEnd", "Document download closes", s.DocDownloadEnd, d.DocDownloadEnd);
        Cmp("clarificationStart", "Clarifications open", s.ClarificationStart, d.ClarificationStart);
        Cmp("clarificationEnd", "Clarifications close", s.ClarificationEnd, d.ClarificationEnd);
        Cmp("prebidMeetingAt", "Pre-bid meeting", s.PrebidMeetingAt, d.PrebidMeetingAt);
        Cmp("prebidVenue", "Pre-bid venue", s.PrebidVenue, d.PrebidVenue);
        Cmp("bidSubmissionStart", "Bid submission opens", s.BidSubmissionStart, d.BidSubmissionStart);
        Cmp("bidSubmissionEnd", "Bid submission closes", s.BidSubmissionEnd, d.BidSubmissionEnd);
        Cmp("technicalOpeningAt", "Technical opening", s.TechnicalOpeningAt, d.TechnicalOpeningAt);
        Cmp("financialOpeningAt", "Financial opening", s.FinancialOpeningAt, d.FinancialOpeningAt);
        Cmp("mseReservationPct", "MSE reservation %", s.MseReservationPct, d.MseReservationPct);
        Cmp("miiApplicable", "Make in India applies", s.MiiApplicable, d.MiiApplicable);
        Cmp("miiLocalContentPct", "Minimum local content %", s.MiiLocalContentPct, d.MiiLocalContentPct);
        Cmp("miiClassRestriction", "Local supplier class", s.MiiClassRestriction, d.MiiClassRestriction);
        Cmp("lbsDeclarationRequired", "Land border declaration", s.LbsDeclarationRequired, d.LbsDeclarationRequired);
        Cmp("startupRelaxation", "Startup relaxation", s.StartupRelaxation, d.StartupRelaxation);
        Cmp("integrityPactApplicable", "Integrity Pact", s.IntegrityPactApplicable, d.IntegrityPactApplicable);
        Cmp("integrityPactMonitor", "Independent External Monitor", s.IntegrityPactMonitor, d.IntegrityPactMonitor);
        Cmp("gemarptsReference", "GeMARPTS reference", s.GemarptsReference, d.GemarptsReference);

        if (s.EmdExemptions is not null &&
            !s.EmdExemptions.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(d.EmdExemptions.OrderBy(x => x, StringComparer.Ordinal)))
            changes.Add(new FieldChangeDto("emdExemptions", "EMD exemptions",
                string.Join(", ", d.EmdExemptions), string.Join(", ", s.EmdExemptions)));

        // The detail blob is compared whole. Diffing forty nested fields to name which line item
        // changed would be a lot of code to produce a diff view nobody reads item-by-item; "the
        // tender documents changed" is the honest summary, and the snapshot holds the detail.
        if (s.Detail is not null)
        {
            var incoming = JsonSerializer.Serialize(s.Detail, Json);
            var current = JsonSerializer.Serialize(DeserializeDetail(d.Detail), Json);
            if (TenderHashChain.Canonicalize(incoming) != TenderHashChain.Canonicalize(current))
                changes.Add(new FieldChangeDto("detail", "Tender details, items and documents",
                    "(previous version)", "(amended)"));
        }

        return changes;
    }

    private async Task<TenderDraftDetailDto> MapDetailAsync(TenderDraft d, CancellationToken ct)
    {
        var committee = await db.TenderCommitteeMembers.AsNoTracking()
            .Where(m => m.DraftId == d.Id).ToListAsync(ct);

        var corrigenda = await db.Corrigenda.AsNoTracking()
            .Where(c => c.DraftId == d.Id)
            .OrderByDescending(c => c.CorrigendumNo)
            .ToListAsync(ct);

        var versions = await db.TenderVersions.AsNoTracking()
            .Where(v => v.DraftId == d.Id)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

        var userIds = corrigenda.Select(c => c.CreatedBy)
            .Concat(versions.Select(v => v.PublishedBy))
            .Append(d.CreatedBy)
            .Distinct()
            .ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return new TenderDraftDetailDto
        {
            Id = d.Id,
            OrgId = d.OrgId,
            ReferenceCode = d.ReferenceCode,
            DepartmentReference = d.DepartmentReference,
            Status = d.Status,
            Title = d.Title,
            Description = d.Description,
            ScopeOfWork = d.ScopeOfWork,
            TenderType = d.TenderType,
            ProcurementCategory = d.ProcurementCategory,
            FormOfContract = d.FormOfContract,
            BiddingSystem = d.BiddingSystem,
            EvaluationMethod = d.EvaluationMethod,
            TechnicalWeightage = d.TechnicalWeightage,
            GfrRuleCited = d.GfrRuleCited,
            Category = d.Category,
            State = d.State,
            City = d.City,
            Pincode = d.Pincode,
            EstimatedValue = d.EstimatedValue,
            ValueDisclosed = d.ValueDisclosed,
            EmdAmount = d.EmdAmount,
            EmdPercentage = d.EmdPercentage,
            EmdMode = d.EmdMode,
            EmdExemptions = d.EmdExemptions,
            TenderFee = d.TenderFee,
            TenderFeeExemptions = d.TenderFeeExemptions,
            PerformanceSecurityPct = d.PerformanceSecurityPct,
            BidValidityDays = d.BidValidityDays,
            PublishedAt = d.PublishedAt,
            DocDownloadStart = d.DocDownloadStart,
            DocDownloadEnd = d.DocDownloadEnd,
            ClarificationStart = d.ClarificationStart,
            ClarificationEnd = d.ClarificationEnd,
            PrebidMeetingAt = d.PrebidMeetingAt,
            PrebidVenue = d.PrebidVenue,
            BidSubmissionStart = d.BidSubmissionStart,
            BidSubmissionEnd = d.BidSubmissionEnd,
            TechnicalOpeningAt = d.TechnicalOpeningAt,
            FinancialOpeningAt = d.FinancialOpeningAt,
            MseReservationPct = d.MseReservationPct,
            MiiApplicable = d.MiiApplicable,
            MiiLocalContentPct = d.MiiLocalContentPct,
            MiiClassRestriction = d.MiiClassRestriction,
            LbsDeclarationRequired = d.LbsDeclarationRequired,
            StartupRelaxation = d.StartupRelaxation,
            IntegrityPactApplicable = d.IntegrityPactApplicable,
            IntegrityPactMonitor = d.IntegrityPactMonitor,
            GemarptsReference = d.GemarptsReference,
            Detail = DeserializeDetail(d.Detail),
            MongoTenderId = d.MongoTenderId,
            CurrentVersion = d.CurrentVersion,
            RuleSetVersion = d.RuleSetVersion,
            Committee = [.. committee.Select(MapCommittee)],
            Corrigenda =
            [
                .. corrigenda.Select(c => new CorrigendumDto(
                    c.Id, c.CorrigendumNo, c.Type, c.Reason,
                    DeserializeChanges(c.Changes), c.NotifiedAt,
                    names.GetValueOrDefault(c.CreatedBy, "(unknown)"), c.CreatedAt))
            ],
            Versions =
            [
                .. versions.Select(v => new TenderVersionDto(
                    v.Id, v.Version, v.Reason, v.ContentHash, v.PrevChainHash, v.ChainHash,
                    v.RuleSetVersion, names.GetValueOrDefault(v.PublishedBy, "(unknown)"), v.CreatedAt))
            ],
            CreatedByName = names.GetValueOrDefault(d.CreatedBy, "(unknown)"),
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
        };
    }

    private async Task<List<TenderVersionDto>> MapVersionsAsync(List<TenderVersion> versions, CancellationToken ct)
    {
        var ids = versions.Select(v => v.PublishedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return [.. versions.Select(v => new TenderVersionDto(
            v.Id, v.Version, v.Reason, v.ContentHash, v.PrevChainHash, v.ChainHash,
            v.RuleSetVersion, names.GetValueOrDefault(v.PublishedBy, "(unknown)"), v.CreatedAt))];
    }

    private static CommitteeMemberDto MapCommittee(TenderCommitteeMember m)
        => new(m.Id, m.UserId, m.Committee, m.MemberName, m.Designation, m.Email, m.IsChair);

    private static TenderDraftDetail DeserializeDetail(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TenderDraftDetail>(json, Json) ?? new TenderDraftDetail();
        }
        catch (JsonException)
        {
            return new TenderDraftDetail();
        }
    }

    private static FieldChangeDto[] DeserializeChanges(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FieldChangeDto[]>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Postgres <c>timestamptz</c> via Npgsql requires a UTC <see cref="DateTime"/>; an Unspecified
    /// kind throws at write time rather than being silently coerced.
    /// </summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => value,
        { Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value is null ? null : new DateTimeOffset(Utc(value)!.Value, TimeSpan.Zero);

    private static DateOnly? ToDateOnly(DateTime? value)
        => value is null ? null : DateOnly.FromDateTime(value.Value);

    private static string Format(DateTime? value)
        => value is null ? "—" : value.Value.ToString("dd MMM yyyy, HH:mm 'UTC'");

    private static string FirstName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "there" : name.Split(' ')[0];

    private static string Humanize(string token)
        => string.Join(' ', token.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string? Str<T>(T? value) => value switch
    {
        null => null,
        bool b => b ? "Yes" : "No",
        DateTime dt => dt.ToString("O"),
        decimal d => d.ToString("0.##"),
        _ => value.ToString(),
    };
}
