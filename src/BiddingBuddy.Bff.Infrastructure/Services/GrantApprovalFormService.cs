using System.Globalization;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Fills the embedded "Approval to Proceed" Word template from a grant application (Postgres) joined to
/// its source grant (Mongo, via <see cref="IGrantServicesClient"/>). See <see cref="IGrantApprovalFormService"/>.
/// </summary>
public class GrantApprovalFormService(
    BffDbContext db,
    IGrantServicesClient grants,
    ILogger<GrantApprovalFormService> logger) : IGrantApprovalFormService
{
    // Matches the <LogicalName> pinned in BiddingBuddy.Bff.Infrastructure.csproj.
    private const string TemplateResourceName = "GrantApprovalForm.Template.docx";

    // US grants — figures are USD; the corpus stores no per-grant currency worth trusting here.
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    public async Task<GrantApprovalFormResult> BuildAsync(Guid applicationId, Guid orgId, CancellationToken ct = default)
    {
        // 1. Org-scoped load (mirrors GrantApplicationService.LoadAsync). A wrong-org id is
        //    indistinguishable from a missing one → KeyNotFoundException → 404 via GlobalExceptionHandler.
        var app = await db.GrantApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Application not found.");

        // 2. Join the source grant when the application is linked to one. Degrade to the application's
        //    own snapshot on any miss — a null id, an archived grant (null), or an upstream outage
        //    (UpstreamServiceException). The form is still useful without the richer funding figures.
        GrantSearchItemDto? grant = null;
        if (!string.IsNullOrWhiteSpace(app.MongoGrantId))
        {
            try
            {
                grant = await grants.GetRawGrantAsync(app.MongoGrantId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Approval form: could not load grant {MongoGrantId} for application {ApplicationId}; using the application snapshot.",
                    app.MongoGrantId, applicationId);
            }
        }

        // 3. Build the token map (grant-first, snapshot fallback, blank otherwise) and fill the template.
        var bytes = FillTemplate(BuildTokens(app, grant));

        var fileName = FileNameSanitizer.Sanitize($"Grant Approval Form - {grant?.Title ?? app.Title}.docx");
        return new GrantApprovalFormResult(bytes, fileName);
    }

    private static Dictionary<string, string> BuildTokens(GrantApplication app, GrantSearchItemDto? g)
    {
        var funding = g?.Funding;
        var elig = g?.Eligibility;

        return new Dictionary<string, string>
        {
            ["{{OpportunityTitle}}"]       = Coalesce(g?.Title, app.Title),
            ["{{FundingAgency}}"]          = Coalesce(g?.Agency?.Name, app.AgencyName),
            ["{{ApplicationDeadline}}"]    = FormatDeadline(g?.Timeline, app.Deadline),
            ["{{SubmissionPortal}}"]       = Coalesce(g?.Source?.SourceUrl, app.SourceUrl),
            ["{{TotalFundingAvailable}}"]  = Money(funding?.EstimatedTotalProgramFunding),
            ["{{ExpectedNumberOfAwards}}"] = funding?.ExpectedNumberOfAwards?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["{{RangeOfAwardValues}}"]     = FormatRange(funding?.AwardFloor, funding?.AwardCeiling),
            ["{{MatchCostShareRequired}}"] = FormatCostShare(elig?.CostSharingRequired, elig?.CostSharePercentage, app.CostSharePct),
            ["{{TotalBudget}}"]            = Money(app.AmountRequested),
        };
    }

    private static string Coalesce(string? a, string? b) => (a ?? b ?? string.Empty).Trim();

    // Null money means "the agency published no figure" — render blank, never $0.
    private static string Money(decimal? v) => v.HasValue ? v.Value.ToString("C0", Usd) : string.Empty;

    private static string FormatRange(decimal? floor, decimal? ceiling)
    {
        if (floor.HasValue && ceiling.HasValue) return $"{Money(floor)} – {Money(ceiling)}";
        if (ceiling.HasValue) return $"Up to {Money(ceiling)}";
        if (floor.HasValue) return $"From {Money(floor)}";
        return string.Empty;
    }

    private static string FormatCostShare(bool? required, decimal? pct, decimal? snapshotPct)
    {
        if (required == true) return pct.HasValue ? $"Yes — {Pct(pct.Value)}" : "Yes";
        if (required == false) return "No";
        // The grant said nothing (or there is no grant) → fall back to the application's cost-share %.
        return snapshotPct.HasValue ? Pct(snapshotPct.Value) : string.Empty;
    }

    private static string Pct(decimal v) => $"{v.ToString("0.#", CultureInfo.InvariantCulture)}%";

    private static string FormatDeadline(GrantTimelineItemDto? t, DateOnly? snapshot)
    {
        if (t?.CloseAt is DateTime close) return close.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(t?.CloseDateExplanation)) return t!.CloseDateExplanation!.Trim();
        if (t?.IsRolling == true) return "Rolling";
        if (snapshot.HasValue) return snapshot.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        return string.Empty;
    }

    // ── Template fill ────────────────────────────────────────────────────────────

    private static byte[] FillTemplate(IReadOnlyDictionary<string, string> tokens)
    {
        var template = LoadTemplate();

        // Open the docx on an expandable copy so the package can grow as text is written.
        using var ms = new MemoryStream();
        ms.Write(template, 0, template.Length);
        ms.Position = 0;

        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is not null)
            {
                foreach (var para in body.Descendants<Paragraph>())
                    ReplaceInParagraph(para, tokens);
            }
        }

        return ms.ToArray();
    }

    private static byte[] LoadTemplate()
    {
        var asm = typeof(GrantApprovalFormService).Assembly;
        using var stream = asm.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded template '{TemplateResourceName}' was not found in {asm.FullName}.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // Substitute on the paragraph's aggregated run text, not on individual runs: Word can fragment a
    // "{{Token}}" across several <w:r> runs, so a per-run replace would silently miss it. The template's
    // value cells hold a single run, so collapsing the substituted text into the first run and clearing
    // the rest keeps formatting intact.
    private static void ReplaceInParagraph(Paragraph para, IReadOnlyDictionary<string, string> tokens)
    {
        var texts = para.Descendants<Text>().ToList();
        if (texts.Count == 0) return;

        var combined = string.Concat(texts.Select(t => t.Text));
        if (!combined.Contains("{{")) return;

        var replaced = combined;
        foreach (var (token, value) in tokens)
            replaced = replaced.Replace(token, value);

        if (replaced == combined) return;

        texts[0].Text = replaced;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = string.Empty;
    }
}
