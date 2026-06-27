using System.Text;
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

    /// <summary>Page size used when paging the upstream service to fill one chunk (avoids a single huge request).</summary>
    private const int FetchPageSize = 1_000;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    /// <summary>Static, indexable public routes (relative paths).</summary>
    private static readonly string[] StaticPaths = ["/", "/explore", "/about", "/contact", "/privacy", "/terms"];

    private string BaseUrl => (config["Frontend:BaseUrl"] ?? "https://tendersagent.com").TrimEnd('/');

    /// <summary>Sitemap index — lists the pages sitemap plus one sitemap per tender chunk.</summary>
    [HttpGet("/sitemap.xml")]
    public Task<IActionResult> Index(CancellationToken ct) =>
        CachedXml("sitemap:index", async () =>
        {
            var total = await GetTotalTenderCountAsync(ct);
            var chunks = total == 0 ? 0 : (int)Math.Ceiling(total / (double)TenderChunkSize);

            var sb = new StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
            AppendSitemapRef(sb, $"{BaseUrl}/sitemap-pages.xml");
            for (var i = 1; i <= chunks; i++)
                AppendSitemapRef(sb, $"{BaseUrl}/sitemap-tenders-{i}.xml");
            sb.AppendLine("</sitemapindex>");
            return sb.ToString();
        });

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

    /// <summary>One chunk of tender detail URLs. 404 when the chunk index is past the data.</summary>
    [HttpGet("/sitemap-tenders-{index:int}.xml")]
    public async Task<IActionResult> Tenders(int index, CancellationToken ct)
    {
        if (index < 1) return NotFound();

        var cacheKey = $"sitemap:tenders:{index}";
        if (cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return XmlContent(cached);

        var items = await FetchChunkAsync(index, ct);
        if (items.Count == 0) return NotFound();

        // Drop expired tenders so the sitemap only advertises pages worth indexing.
        // We filter by closing date (the codebase's own "live" signal — see
        // MatchingService.IsLive) rather than the status string. Tenders with no
        // closing date are kept (we can't prove them expired). This is a per-chunk
        // filter, so the oldest chunk may render fewer (or zero) URLs as its tenders
        // age out — harmless: Google just sees a smaller/empty urlset.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var sb = new StringBuilder();
        OpenUrlSet(sb);
        foreach (var t in items)
        {
            if (t.ClosingDate is { } closing && closing < today) continue;   // expired → omit
            var slug = Slugify(t.Title);
            var seg = slug.Length > 0 ? $"{slug}-{t.Id}" : t.Id.ToString();
            var lastmod = t.PublishedDate?.ToString("yyyy-MM-dd");
            AppendUrl(sb, $"{BaseUrl}/explore/{seg}", lastmod);
        }
        sb.AppendLine("</urlset>");

        var xml = sb.ToString();
        cache.Set(cacheKey, xml, CacheTtl);
        return XmlContent(xml);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<int> GetTotalTenderCountAsync(CancellationToken ct)
    {
        var page = await servicesClient.SearchTendersPagedAsync(
            new TenderSearchQueryDto { Page = 1, PageSize = 1 }, ct);
        return page.TotalCount;
    }

    /// <summary>Collect up to one chunk worth of tenders, paging the upstream service in safe sub-pages.</summary>
    private async Task<List<TenderListItemDto>> FetchChunkAsync(int chunkIndex, CancellationToken ct)
    {
        var globalOffset = (chunkIndex - 1) * TenderChunkSize;
        var collected = new List<TenderListItemDto>(Math.Min(TenderChunkSize, FetchPageSize));

        // Stable ordering so chunk boundaries don't shuffle between requests.
        var subPagesPerChunk = TenderChunkSize / FetchPageSize;   // 10
        var firstSubPage = globalOffset / FetchPageSize + 1;

        for (var i = 0; i < subPagesPerChunk; i++)
        {
            var result = await servicesClient.SearchTendersPagedAsync(new TenderSearchQueryDto
            {
                Page      = firstSubPage + i,
                PageSize  = FetchPageSize,
                SortBy    = "published",
                SortOrder = "desc",
            }, ct);

            if (result.Items.Count == 0) break;
            collected.AddRange(result.Items);
            if (!result.HasNextPage) break;
        }

        return collected;
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
        sb.Append("  <sitemap><loc>").Append(XmlEscape(loc)).AppendLine("</loc></sitemap>");
    }

    private static void AppendUrl(StringBuilder sb, string loc, string? lastmod)
    {
        sb.Append("  <url><loc>").Append(XmlEscape(loc)).Append("</loc>");
        if (!string.IsNullOrEmpty(lastmod))
            sb.Append("<lastmod>").Append(lastmod).Append("</lastmod>");
        sb.AppendLine("</url>");
    }

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

    /// <summary>Mirror of the UI slugify(): lowercase, non-alphanumeric → single dash, trimmed, capped at 70.</summary>
    private static string Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        var prevDash = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(ch);
                prevDash = false;
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 70) slug = slug[..70].Trim('-');
        return slug;
    }
}
