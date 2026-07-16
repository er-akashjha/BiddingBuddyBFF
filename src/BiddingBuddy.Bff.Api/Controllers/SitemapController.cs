using System.Text;
using BiddingBuddy.Bff.Api.Seo;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Dynamic XML sitemaps for search engines, served at the site root
/// (<c>/sitemap.xml</c>) so Google can discover every public tender page.
///
/// A static file can't track the ~50k+ tenders that change daily, so this is
/// generated live from the shared Mongo corpus (via BiddingBuddyServices) and
/// cached. Layout is a sitemap index → one "pages" child + N tender chunks of
/// <see cref="TenderChunkSize"/> URLs each (well under the 50k-per-file limit).
///
/// Anonymous + excluded from OrgContextMiddleware (see its skip list).
/// URLs use the SEO slug form <c>/explore/&lt;slug&gt;-&lt;id&gt;</c> to match the SPA's canonical links.
/// </summary>
[ApiController]
[AllowAnonymous]
public class SitemapController(
    IBiddingBuddyServicesClient servicesClient,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<SitemapController> logger) : ControllerBase
{
    /// <summary>URLs per tender chunk file. 50,000 is the spec max; 10k leaves headroom and keeps each fetch light.</summary>
    private const int TenderChunkSize = 10_000;

    /// <summary>Page size for the keyset walk of the upstream enumerate endpoint.</summary>
    private const int FetchPageSize = 1_000;

    private const string TenderLinesKey = "sitemap:tender-lines";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    /// <summary>Static, indexable public routes (relative paths).</summary>
    private static readonly string[] StaticPaths = ["/", "/explore", "/about", "/contact", "/privacy", "/terms"];

    private string BaseUrl => (config["Frontend:BaseUrl"] ?? "https://tendersagent.com").TrimEnd('/');

    /// <summary>Sitemap index — lists the pages sitemap plus one sitemap per tender chunk.</summary>
    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var lines = await GetAllTenderUrlLinesAsync(ct);
            var chunks = (int)Math.Ceiling(lines.Count / (double)TenderChunkSize);

            var sb = new StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
            AppendSitemapRef(sb, $"{BaseUrl}/sitemap-pages.xml");
            AppendSitemapRef(sb, $"{BaseUrl}/sitemap-hubs.xml");
            for (var i = 1; i <= chunks; i++)
                AppendSitemapRef(sb, $"{BaseUrl}/sitemap-tenders-{i}.xml");
            sb.AppendLine("</sitemapindex>");
            return XmlContent(sb.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build sitemap index");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Static public pages (landing, explore, marketing/legal).</summary>
    [HttpGet("/sitemap-pages.xml")]
    public Task<IActionResult> Pages() =>
        CachedXml("sitemap:pages", () =>
        {
            var sb = new StringBuilder();
            OpenUrlSet(sb);
            foreach (var path in StaticPaths)
            {
                var loc = path == "/" ? BaseUrl : $"{BaseUrl}{path}";
                AppendUrl(sb, loc, null);
            }
            sb.AppendLine("</urlset>");
            return Task.FromResult(sb.ToString());
        });

    /// <summary>Programmatic hub pages — one URL per canonical category + state (+ the explore root).</summary>
    [HttpGet("/sitemap-hubs.xml")]
    public async Task<IActionResult> Hubs(CancellationToken ct)
    {
        try
        {
            var cached = await cache.GetOrCreateAsync("sitemap:hubs", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                var categories = await servicesClient.GetTenderFacetOptionsAsync("category", null, 0, ct);
                var states     = await servicesClient.GetTenderFacetOptionsAsync("state", null, 0, ct);

                var sb = new StringBuilder();
                OpenUrlSet(sb);
                AppendUrl(sb, $"{BaseUrl}/explore", null);
                AppendFacetHubs(sb, "category", categories);
                AppendFacetHubs(sb, "state", states);
                sb.AppendLine("</urlset>");
                return sb.ToString();
            });
            return XmlContent(cached!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build hubs sitemap");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private void AppendFacetHubs(StringBuilder sb, string field, IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in values)
        {
            var slug = SeoHelpers.Slugify(v);
            if (slug.Length == 0 || !seen.Add(slug)) continue;   // dedup slug collisions
            AppendUrl(sb, $"{BaseUrl}/explore/{field}/{slug}", null);
        }
    }

    /// <summary>One chunk of tender detail URLs. 404 when the chunk index is past the data.</summary>
    [HttpGet("/sitemap-tenders-{index:int}.xml")]
    public async Task<IActionResult> Tenders(int index, CancellationToken ct)
    {
        if (index < 1) return NotFound();

        try
        {
            var lines = await GetAllTenderUrlLinesAsync(ct);
            var slice = lines.Skip((index - 1) * TenderChunkSize).Take(TenderChunkSize).ToList();
            if (slice.Count == 0) return NotFound();

            var sb = new StringBuilder();
            OpenUrlSet(sb);
            foreach (var line in slice) sb.AppendLine(line);
            sb.AppendLine("</urlset>");
            return XmlContent(sb.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build tender sitemap chunk {Index}", index);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-rendered <c>&lt;url&gt;</c> lines for EVERY tender (full archive — closed
    /// included), built once via a keyset walk of the enumerate endpoint and cached.
    /// Keyset (afterId cursor on _id) scales to the whole corpus without the deep-skip
    /// 400 that the search endpoint hits past ~10k records. lastmod = tender UpdatedAt.
    /// </summary>
    private async Task<List<string>> GetAllTenderUrlLinesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TenderLinesKey, out List<string>? cached) && cached is not null)
            return cached;

        var lines = new List<string>(60_000);
        string? afterId = null;
        while (true)
        {
            var batch = await servicesClient.EnumerateTendersAsync(afterId, FetchPageSize, ct);
            if (batch.Count == 0) break;

            foreach (var t in batch)
            {
                var slug = SeoHelpers.Slugify(t.Title);
                var seg = slug.Length > 0 ? $"{slug}-{t.Id}" : t.Id;
                var loc = SeoHelpers.XmlEscape($"{BaseUrl}/explore/{seg}");
                var lastmod = t.UpdatedAt == default ? null : t.UpdatedAt.ToString("yyyy-MM-dd");
                lines.Add(lastmod is null
                    ? $"  <url><loc>{loc}</loc></url>"
                    : $"  <url><loc>{loc}</loc><lastmod>{lastmod}</lastmod></url>");
                afterId = t.Id;
            }

            if (batch.Count < FetchPageSize) break;
        }

        logger.LogInformation("Sitemap: enumerated {Count} tenders", lines.Count);
        cache.Set(TenderLinesKey, lines, CacheTtl);
        return lines;
    }

    private async Task<IActionResult> CachedXml(string key, Func<Task<string>> build)
    {
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
            return XmlContent(cached);

        try
        {
            var xml = await build();
            cache.Set(key, xml, CacheTtl);
            return XmlContent(xml);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build sitemap {Key}", key);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private ContentResult XmlContent(string xml)
    {
        Response.Headers.CacheControl = $"public, max-age={(int)CacheTtl.TotalSeconds}";
        return new ContentResult { Content = xml, ContentType = "application/xml; charset=utf-8", StatusCode = 200 };
    }

    private static void OpenUrlSet(StringBuilder sb)
    {
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
    }

    private static void AppendSitemapRef(StringBuilder sb, string loc)
    {
        sb.Append("  <sitemap><loc>").Append(SeoHelpers.XmlEscape(loc)).AppendLine("</loc></sitemap>");
    }

    private static void AppendUrl(StringBuilder sb, string loc, string? lastmod)
    {
        sb.Append("  <url><loc>").Append(SeoHelpers.XmlEscape(loc)).Append("</loc>");
        if (!string.IsNullOrEmpty(lastmod))
            sb.Append("<lastmod>").Append(lastmod).Append("</lastmod>");
        sb.AppendLine("</url>");
    }
}
