using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/tenders")]
[Authorize]
[Produces("application/json")]
public class TendersController(
    ITenderService tenderService,
    IBiddingBuddyServicesClient servicesClient,
    IOrganizationService organizations,
    ITenderFileStorage tenderFileStorage,
    IAiQuotaService aiQuota,
    IPlanService planService) : BffControllerBase
{
    /// <summary>Tender list from BiddingBuddyServices (MongoDB). Only provided filters are forwarded.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TenderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        // The authenticated discovery list only shows still-biddable tenders (deadline at least
        // ~2 days out); closed / closing-too-soon tenders are hidden for every sort. Forcing the
        // flag here (rather than trusting the client) is what makes that a guarantee. Guest
        // (PublicTendersController) and SSR paths don't set it, so they are unaffected.
        var result = await servicesClient.SearchTendersAsync(query with { OnlyBiddable = true }, ct);
        return Ok(result);
    }

    /// <summary>Paged tender list from BiddingBuddyServices (MongoDB). Forwards pagination metadata to the client.</summary>
    [HttpGet("paged")]
    [ProducesResponseType(typeof(PagedTenderListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPaged([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        // See List above — the authenticated list is always scoped to still-biddable tenders.
        var result = await servicesClient.SearchTendersPagedAsync(query with { OnlyBiddable = true }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Distinct filter option values (categories, states) present in the tender data.
    /// Drives the UI filter dropdowns so they stay in sync with the scraped data.
    /// </summary>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(TenderFacetsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Facets([FromQuery] int limit = 15, CancellationToken ct = default)
    {
        var result = await servicesClient.GetTenderFacetsAsync(limit, ct);
        return Ok(result);
    }

    /// <summary>
    /// Type-ahead options for a single facet field ("category" or "state").
    /// Empty <c>search</c> → default top-<c>limit</c> set; a non-empty search returns
    /// every matching value (pass <c>limit=0</c> for no cap). Backs the multi-select filters.
    /// </summary>
    [HttpGet("facet-options")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FacetOptions(
        [FromQuery] string field,
        [FromQuery] string? search,
        [FromQuery] int limit = 15,
        CancellationToken ct = default)
    {
        var values = await servicesClient.GetTenderFacetOptionsAsync(field, search, limit, ct);
        return Ok(values);
    }

    /// <summary>
    /// Full tender detail by ID from BiddingBuddyServices. AI fields are masked
    /// (<c>aiLocked: true</c>) until the org unlocks this tender for the current month —
    /// pass <c>unlockAi=true</c> to consume one monthly AI credit and unmask (the same
    /// usage key as <c>/api/analysis/tenders/{id}</c>, so neither double-charges).
    /// Unlimited-AI plans auto-unlock on view. Quota exhausted → 403 UPGRADE_REQUIRED.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TenderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct, [FromQuery] bool unlockAi = false)
    {
        var tender = await servicesClient.GetTenderAsync(id.ToString(), ct);
        if (tender is null) return Ok(tender);

        var plan = await planService.GetPlanForAsync(CurrentOrgId, ct);

        // Unlimited plans never ration, so masking every tender behind a click would be
        // pure friction — auto-unlock (usage is still recorded for fair-use monitoring).
        var wantsUnlock = unlockAi || plan.AiSummariesPerMonth is null;

        bool unlocked;
        if (wantsUnlock && !HasAnyAi(tender))
        {
            // Nothing behind the paywall for this tender — the pipeline never enriched it, or
            // enriched it to nothing. Unmask (there is nothing to hide) without spending a
            // credit. Charging here sells an empty screen.
            unlocked = await aiQuota.IsUnlockedAsync(
                CurrentOrgId, PlanFeatures.AiSummary, id.ToString(), ct);
        }
        else if (wantsUnlock)
        {
            var verdict = await aiQuota.TryConsumeAsync(
                CurrentOrgId, CurrentUserId, PlanFeatures.AiSummary, id.ToString(), ct);
            if (!verdict.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error        = "You've used all AI summaries included in your plan this month.",
                    code         = "UPGRADE_REQUIRED",
                    feature      = PlanFeatures.AiSummary,
                    requiredPlan = PlanCatalog.NextPlanUp(plan.PlanCode),
                    currentPlan  = plan.PlanCode,
                    used         = verdict.Used,
                    quota        = verdict.Quota,
                });
            unlocked = true;
        }
        else
        {
            unlocked = await aiQuota.IsUnlockedAsync(
                CurrentOrgId, PlanFeatures.AiSummary, id.ToString(), ct);
        }

        if (!unlocked) return Ok(MaskAi(tender));

        // The eligibility verdict is a Growth+ feature even once unlocked.
        if (!plan.HasFeature(PlanFeatures.EligibilityCheck))
            tender = tender with
            {
                EligibilityScore = null,
                AiAnalysis = tender.AiAnalysis is null
                    ? null
                    : tender.AiAnalysis with { EligibilityBreakdown = null },
            };

        return Ok(tender);
    }

    /// <summary>
    /// Is there anything behind the paywall for this tender? AiScore is excluded deliberately —
    /// it is the teaser shown on every plan, so a tender carrying only a score has nothing an
    /// unlock would reveal.
    /// </summary>
    private static bool HasAnyAi(TenderDetailDto t) =>
        !string.IsNullOrWhiteSpace(t.AiSummary)
        || t.AiTags is { Length: > 0 }
        || t.RiskScore is not null
        || t.EligibilityScore is not null
        || t.AiAnalysis is not null;

    /// <summary>AiScore stays as the teaser on every plan; everything else AI is nulled.</summary>
    private static TenderDetailDto MaskAi(TenderDetailDto t) => t with
    {
        EligibilityScore = null,
        RiskScore        = null,
        WinProbability   = null,
        AiSummary        = null,
        AiTags           = null,
        AiAnalysis       = null,
        AiLocked         = true,
    };

    /// <summary>
    /// Generate a short-lived presigned URL for a scraped tender document (PDF) in S3.
    /// The bytes never flow through the BFF. Uses the document's stored s3Key when
    /// present, otherwise reconstructs it from the platform tender id + document id
    /// (for tenders enriched before s3Key was persisted).
    /// Pass <c>inline=true</c> to open in-browser (view); default is an attachment (download).
    /// </summary>
    [HttpGet("{id:guid}/documents/{documentId}/download-url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDocumentDownloadUrl(
        Guid id, string documentId, CancellationToken ct, [FromQuery] bool inline = false)
    {
        var tender = await servicesClient.GetRawTenderAsync(id.ToString(), ct);
        if (tender is null) return NotFound();

        var doc = tender.Documents?.FirstOrDefault(d => d.DocumentId == documentId);
        if (doc is null) return NotFound();

        var bucket = !string.IsNullOrWhiteSpace(doc.S3Bucket)
            ? doc.S3Bucket!
            : tenderFileStorage.DefaultBucket;

        var key = !string.IsNullOrWhiteSpace(doc.S3Key)
            ? doc.S3Key!
            : tenderFileStorage.ReconstructKey(tender.Source?.PlatformTenderId ?? string.Empty, documentId);

        var fileName = !string.IsNullOrWhiteSpace(doc.FileName) ? doc.FileName! : $"{documentId}.pdf";

        var presigned = await tenderFileStorage.CreatePresignedGetAsync(bucket, key, fileName, inline: inline, ct);
        return Ok(new { url = presigned.Url, expiresAt = presigned.ExpiresAt });
    }

    /// <summary>
    /// Award result (winner + full competitive price ladder) for a tender, proxied from
    /// BiddingBuddyServices, plus the caller-org's own position on that ladder. 404 until the
    /// gem-results pipeline has recorded an award. The route carries the Mongo id, so we resolve
    /// the platform + platform-tender-id off the raw tender first.
    /// </summary>
    [HttpGet("{id:guid}/result")]
    [RequirePlanFeature(PlanFeatures.Competitors)]   // result/award history is the Growth+ pitch
    [ProducesResponseType(typeof(TenderResultViewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetResult(Guid id, CancellationToken ct)
    {
        var tender = await servicesClient.GetRawTenderAsync(id.ToString(), ct);
        var platform = tender?.Source?.Platform;
        var platformTenderId = tender?.Source?.PlatformTenderId;
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(platformTenderId))
            return NotFound();

        var result = await servicesClient.GetTenderResultAsync(platform!, platformTenderId!, ct);
        if (result is null) return NotFound();

        var org = await organizations.GetAsync(CurrentOrgId, CurrentUserId, ct);
        return Ok(TenderResultView.Build(result, org.GemSellerName, org.Name));
    }

    /// <summary>Save a tender to the org with optional notes, tags and custom score.</summary>
    [HttpPost("{id:guid}/save")]
    [ProducesResponseType(typeof(OrgTenderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.SaveAsync(id, CurrentOrgId, CurrentUserId, dto, ct);
        return Ok(settings);
    }

    /// <summary>Remove a tender from the org's saved list.</summary>
    [HttpDelete("{id:guid}/save")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unsave(Guid id, CancellationToken ct)
    {
        await tenderService.UnsaveAsync(id, CurrentOrgId, ct);
        return NoContent();
    }

    /// <summary>Update org-specific notes, tags and custom score for a saved tender.</summary>
    [HttpPatch("{id:guid}/settings")]
    [ProducesResponseType(typeof(OrgTenderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] SaveTenderDto dto, CancellationToken ct)
    {
        var settings = await tenderService.UpdateSettingsAsync(id, CurrentOrgId, dto, ct);
        return Ok(settings);
    }
}
