using System.Text.Json;
using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Capability;
using BiddingBuddy.Bff.Core.DTOs.Fit;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Fit;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Produces and stores the per-org fit report.
///
/// <para><b>The metering rule, in one place.</b> A credit is taken only when a real verdict is
/// newly produced. Everything else is free: re-reading a stored report, a tender already
/// unlocked this month through the detail page (same usage key, so the two surfaces never
/// double-charge), a re-run forced by the buyer's own corrigendum, and — critically — an
/// <c>insufficient_data</c> outcome, because declining to answer is not a product.</para>
/// </summary>
public class TenderFitService(
    BffDbContext db,
    IBiddingBuddyServicesClient servicesClient,
    ICapabilityProfileService capability,
    IAiQuotaService aiQuota,
    IPlanService planService,
    IRabbitMqPublisher rabbit,
    ILogger<TenderFitService> logger) : ITenderFitService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FitReportDto> GetOrComputeAsync(
        Guid orgId, Guid? userId, Guid tenderId, bool forceRerun = false, CancellationToken ct = default)
    {
        var tender = await servicesClient.GetTenderAsync(tenderId.ToString(), ct)
            ?? throw new KeyNotFoundException("Tender not found.");

        var stored = await db.TenderFitReports
            .FirstOrDefaultAsync(r => r.OrgId == orgId && r.TenderId == tenderId, ct);

        var (isStale, staleReason) = Staleness(stored, tender);

        // Serve the stored report untouched unless it is stale or a re-run was asked for. This
        // is the common path — a user re-opening the tab must never be billed for scrolling.
        if (stored is not null && !isStale && !forceRerun)
            return await ToDtoAsync(stored, tender, orgId, charged: false, isStale: false, null, ct);

        var (profile, credentials) = await capability.GetOperandsAsync(orgId, ct);
        var history = await LoadHistoryAsync(orgId, ct);

        var result = TenderFitRules.Evaluate(tender, profile, credentials, history, IstToday());

        // ── Meter ────────────────────────────────────────────────────────────
        // Ordered so the customer is never charged for an answer we couldn't give, or for a
        // recomputation the buyer forced on them.
        var charged = false;
        var alreadyUnlocked = await aiQuota.IsUnlockedAsync(
            orgId, PlanFeatures.AiSummary, tenderId.ToString(), ct);
        var freeRerun = stored is not null && isStale;

        if (result.Verdict != FitVerdicts.InsufficientData && !alreadyUnlocked && !freeRerun)
        {
            var verdict = await aiQuota.TryConsumeAsync(
                orgId, userId, PlanFeatures.AiSummary, tenderId.ToString(), ct);
            if (!verdict.Allowed)
            {
                var plan = await planService.GetPlanForAsync(orgId, ct);
                throw new PlanLimitException(
                    PlanLimitException.UpgradeRequired,
                    "You've used all AI analyses included in your plan this month.",
                    PlanFeatures.AiSummary,
                    PlanCatalog.NextPlanUp(plan.PlanCode), plan.PlanCode,
                    verdict.Used, verdict.Quota);
            }
            charged = !verdict.AlreadyUnlocked;
        }

        TenderFitReport row;
        try
        {
            row = await PersistAsync(stored, orgId, tenderId, tender, result, profile, ct);
        }
        catch (Exception ex)
        {
            // The meter was taken a few lines up. If the write fails, the customer has paid for a
            // report that does not exist — so hand the credit back before rethrowing. On the Free
            // plan that is a third of their month, spent on our outage.
            if (charged)
            {
                var refunded = await aiQuota.RefundAsync(
                    orgId, PlanFeatures.AiSummary, tenderId.ToString(), ct);
                logger.LogError(ex,
                    "Failed to persist the fit report for org {OrgId} / tender {TenderId}; credit refunded: {Refunded}",
                    orgId, tenderId, refunded);
            }
            throw;
        }

        // The narrative is a bonus layer over a verdict that is already complete and stored, so
        // a publish failure is logged and swallowed — the customer's report is unaffected and
        // the state simply stays 'pending'.
        if (row.NarrativeState == NarrativeStates.Pending)
            await RequestNarrativeAsync(row, tender, result, ct);

        return await ToDtoAsync(row, tender, orgId, charged, isStale: false, staleReason: null, ct);
    }

    /// <summary>
    /// Ask the pipeline for the prose layer. Everything the model needs travels in the message:
    /// the worker has no org-scoped read path into Postgres, and giving it one would put tenant
    /// data behind an API key for the sake of one paragraph.
    /// </summary>
    private async Task RequestNarrativeAsync(
        TenderFitReport row, TenderDetailDto tender, FitResult result, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                orgId         = row.OrgId,
                tenderId      = row.TenderId,
                tenderTitle   = tender.Title,
                buyerName     = tender.BuyerOrgName,
                category      = tender.Category,
                state         = tender.State,
                tenderValue   = tender.TenderValue,
                closingDate   = tender.ClosingDate?.ToString("yyyy-MM-dd"),
                verdict       = result.Verdict,
                verdictReason = result.VerdictReason,
                // Findings arrive blockers-first, so the worker's truncation keeps what matters.
                findings      = result.Findings.Select(f => new
                {
                    code = f.Code, severity = f.Severity, title = f.Title, detail = f.Detail,
                }),
                blockedNow    = result.Cost.BlockedNow,
                correlationId = row.Id.ToString(),
            };

            var published = await rabbit.PublishAsync(FitNarrativeQueue, request, row.Id, ct);
            if (!published)
                logger.LogWarning(
                    "Could not queue the AI summary for tender {TenderId}; the report stands without it.",
                    row.TenderId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not queue the AI summary for tender {TenderId}; the report stands without it.",
                row.TenderId);
        }
    }

    /// <summary>
    /// Mirrors <c>BidProcessor.Contracts.Topics.TenderFitNarrative</c>. The two repos share no
    /// assembly, so this string is a contract — changing it on one side alone routes messages
    /// into a queue nobody consumes, and an unroutable message on the default exchange is
    /// silently DISCARDED rather than erroring.
    /// </summary>
    private const string FitNarrativeQueue = "tender.fit-narrative";

    public async Task<FitReportDto?> GetExistingAsync(
        Guid orgId, Guid tenderId, CancellationToken ct = default)
    {
        var stored = await db.TenderFitReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrgId == orgId && r.TenderId == tenderId, ct);
        if (stored is null) return null;

        var tender = await servicesClient.GetTenderAsync(tenderId.ToString(), ct);
        if (tender is null) return null;

        var (isStale, staleReason) = Staleness(stored, tender);
        return await ToDtoAsync(stored, tender, orgId, charged: false, isStale, staleReason, ct);
    }

    public async Task SetNarrativeAsync(
        Guid orgId, Guid tenderId, string? narrative, string? model, string state, string? error,
        CancellationToken ct = default)
    {
        var row = await db.TenderFitReports
            .FirstOrDefaultAsync(r => r.OrgId == orgId && r.TenderId == tenderId, ct);
        if (row is null)
        {
            // The report was deleted (or the org was) while the worker was thinking. Dropping the
            // narrative is correct — there is nothing for it to attach to — but it must not throw,
            // or the worker dead-letters a message it can never succeed at.
            logger.LogWarning(
                "Narrative callback for a fit report that no longer exists (org {OrgId}, tender {TenderId})",
                orgId, tenderId);
            return;
        }

        row.Narrative      = narrative;
        row.NarrativeModel = model;
        row.NarrativeState = state;
        row.NarrativeError = error;
        row.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FitDocumentResult> ExportAsync(
        Guid orgId, Guid tenderId, CancellationToken ct = default)
    {
        // Deliberately GetExisting, not GetOrCompute: you can only export what you already ran.
        // Computing here would make "Download" a second, invisible way to spend a credit.
        var report = await GetExistingAsync(orgId, tenderId, ct)
            ?? throw new KeyNotFoundException(
                "No bid-fit report has been run for this tender yet.");

        var orgName = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct)
            ?? "your organisation";

        var content = FitReportDocumentBuilder.Build(report, orgName);
        var fileName = FileNameSanitizer.Sanitize(
            $"Bid-note {Shorten(report.TenderTitle, 60)}.docx");

        return new FitDocumentResult(content, fileName);
    }

    public async Task<int> PushToChecklistAsync(
        Guid orgId, Guid bidId, Guid tenderId, CancellationToken ct = default)
    {
        var bid = await db.Bids.FirstOrDefaultAsync(b => b.Id == bidId && b.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Bid not found.");

        var report = await GetExistingAsync(orgId, tenderId, ct)
            ?? throw new KeyNotFoundException(
                "No bid-fit report has been run for this tender yet.");

        // Only what a person actually has to DO. Watches are context for pricing and passes are
        // reassurance; putting either on a checklist trains the team to ignore the checklist.
        var actionable = report.Findings
            .Where(f => f.Severity is FitSeverities.Blocker or FitSeverities.Gap)
            .ToList();
        if (actionable.Count == 0) return 0;

        var existingTitles = await db.BidChecklistItems
            .Where(i => i.BidId == bidId)
            .Select(i => i.Title)
            .ToListAsync(ct);
        var seen = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nextOrder = await db.BidChecklistItems
            .Where(i => i.BidId == bidId)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct) ?? 0;

        var added = 0;
        foreach (var f in actionable)
        {
            var title = Shorten(f.Title, 200);
            // Idempotent on the title so a re-run after a corrigendum tops the list up instead of
            // duplicating everything the team has already ticked off.
            if (!seen.Add(title)) continue;

            db.BidChecklistItems.Add(new BidChecklistItem
            {
                BidId = bidId,
                OrgId = orgId,
                Title = title,
                // The tender's closing date: a blocker resolved after bids close is not resolved.
                DueDate   = bid.DueDate,
                SortOrder = ++nextOrder,
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    // ── Staleness ───────────────────────────────────────────────────────────

    /// <summary>
    /// A corrigendum can change eligibility outright — a new certificate requirement, a raised
    /// turnover bar, a moved deadline. A report computed before one is not merely old, it may be
    /// actively wrong, and it is the buyer who made it so. Hence: flagged, and free to re-run.
    /// </summary>
    private static (bool IsStale, string? Reason) Staleness(TenderFitReport? stored, TenderDetailDto tender)
    {
        if (stored is null) return (false, null);

        if (stored.RuleSetVersion != TenderFitRules.Version)
            return (true, $"Our eligibility rules were updated (v{stored.RuleSetVersion} → "
                        + $"v{TenderFitRules.Version}). Re-run to evaluate under the current set.");

        if (stored.CorrigendumCount is int had && had != tender.CorrigendumCount)
            return (true, "This tender has been amended since we last checked it. A corrigendum can "
                        + "change the eligibility criteria, so this report may no longer be accurate.");

        if (stored.TenderUpdatedAt is DateTime seen && tender.UpdatedAt > seen.AddMinutes(1))
            return (true, "The tender's details changed after this report was generated.");

        return (false, null);
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private async Task<TenderFitReport> PersistAsync(
        TenderFitReport? existing, Guid orgId, Guid tenderId, TenderDetailDto tender,
        FitResult result, OrgCapabilityProfile? profile, CancellationToken ct)
    {
        // The report FKs to the Postgres `tenders` mirror, but the tender itself is read from
        // Mongo — a tender can be perfectly readable and simply not mirrored yet. Storing is
        // therefore best-effort: the verdict is still returned, it just isn't cached.
        var mirrored = await db.Tenders.AnyAsync(t => t.Id == tenderId, ct);

        var row = existing ?? new TenderFitReport { OrgId = orgId, TenderId = tenderId };
        row.Verdict          = result.Verdict;
        row.VerdictReason    = result.VerdictReason;
        row.Findings         = JsonSerializer.Serialize(result.Findings, Json);
        row.CostToBid        = result.Cost.HasAnything ? result.Cost.Total : null;
        row.CostBreakdown    = JsonSerializer.Serialize(result.Cost, Json);
        row.RuleSetVersion   = result.RuleSetVersion;
        row.ProfileUpdatedAt = profile?.UpdatedAt;
        row.TenderUpdatedAt  = tender.UpdatedAt;
        row.CorrigendumCount = tender.CorrigendumCount;
        row.GeneratedAt      = DateTime.UtcNow;
        row.UpdatedAt        = DateTime.UtcNow;

        // A re-run invalidates the old prose: it was written about a verdict that has changed.
        row.Narrative      = null;
        row.NarrativeModel = null;
        row.NarrativeError = null;
        row.NarrativeState = result.Verdict == FitVerdicts.InsufficientData
            ? NarrativeStates.Skipped   // nothing to narrate
            : NarrativeStates.Pending;

        if (!mirrored)
        {
            logger.LogInformation(
                "Tender {TenderId} is not mirrored in Postgres; returning the fit verdict without caching it.",
                tenderId);
            return row;
        }

        if (existing is null) db.TenderFitReports.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

    private async Task<FitReportDto> ToDtoAsync(
        TenderFitReport row, TenderDetailDto tender, Guid orgId,
        bool charged, bool isStale, string? staleReason, CancellationToken ct)
    {
        var findings = JsonSerializer.Deserialize<List<FitFinding>>(row.Findings, Json) ?? [];
        var cost = string.IsNullOrWhiteSpace(row.CostBreakdown)
            ? BidCostEstimate.Empty
            : JsonSerializer.Deserialize<BidCostEstimate>(row.CostBreakdown, Json) ?? BidCostEstimate.Empty;

        var (used, quota) = await aiQuota.GetUsageAsync(orgId, PlanFeatures.AiSummary, ct);
        var completeness = (await capability.GetAsync(orgId, ct)).Completeness;

        return new FitReportDto(
            row.TenderId, tender.Title,
            row.Verdict, row.VerdictReason ?? string.Empty,
            findings,
            Count(findings),
            cost,
            row.Narrative, row.NarrativeState,
            row.RuleSetVersion, row.GeneratedAt,
            isStale, staleReason,
            completeness,
            charged, new AiUsageDto(used, quota));
    }

    private static FitCountsDto Count(IReadOnlyList<FitFinding> findings) => new(
        findings.Count(x => x.Severity == FitSeverities.Blocker),
        findings.Count(x => x.Severity == FitSeverities.Gap),
        findings.Count(x => x.Severity == FitSeverities.Watch),
        findings.Count(x => x.Severity == FitSeverities.Ok),
        findings.Count(x => x.Severity == FitSeverities.Unknown));

    // ── Derived inputs ──────────────────────────────────────────────────────

    /// <summary>Derived from `bids` rather than stored — a second copy of the org's own record
    /// would drift from the pipeline that maintains it.</summary>
    private async Task<OrgBidHistory> LoadHistoryAsync(Guid orgId, CancellationToken ct)
    {
        var rows = await db.Bids.AsNoTracking()
            .Where(b => b.OrgId == orgId)
            .Select(b => new { b.Stage, b.WonValue, b.TenderId })
            .ToListAsync(ct);

        if (rows.Count == 0) return OrgBidHistory.Empty;

        var won = rows.Where(r => r.Stage == "won").ToList();
        var categories = won.Count == 0
            ? []
            : await db.Tenders.AsNoTracking()
                .Where(t => won.Select(w => w.TenderId).Contains(t.Id) && t.Category != null)
                .Select(t => t.Category!)
                .Distinct()
                .ToListAsync(ct);

        return new OrgBidHistory(
            rows.Count(r => r.Stage is "submitted" or "won" or "lost"),
            won.Count,
            won.Count == 0 ? null : won.Max(w => w.WonValue),
            categories);
    }

    /// <summary>Every deadline in this product, and the AI quota month, are IST. A UTC "today"
    /// is a day behind for five and a half hours every night, which is enough to call a live
    /// certificate expired or a closing tender closed.</summary>
    private static DateOnly IstToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist));

    private static readonly TimeZoneInfo Ist = ResolveIst();

    private static TimeZoneInfo ResolveIst()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
