using System.Globalization;
using System.Text;
using System.Text.Json;
using BiddingBuddy.Bff.Api.Seo;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Server-rendered HTML for the public pages, served to crawlers that can't run
/// JavaScript — Google's render budget and especially AI answer engines (GPTBot,
/// OAI-SearchBot, ClaudeBot, PerplexityBot, Google-Extended), which execute no JS
/// at all and would otherwise see the SPA's empty &lt;div id="root"&gt;.
///
/// This is "dynamic rendering": Caddy detects bot user-agents and routes their
/// page requests here; real users still get the fast SPA from nginx. The HTML is
/// built directly from the same public tender data the SPA's API uses (no headless
/// browser — keeps memory tiny on the constrained box), so bot content mirrors user
/// content (Google-sanctioned, not cloaking).
///
/// Anonymous; rate-limited via the shared "public" policy so a crawler sweeping
/// ~50k tender pages can't overload the box. No in-process HTML cache (would blow
/// the BFF's 256MB cap at 50k pages); Cache-Control lets any future CDN/edge cache.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public")]
public class SsrController(IBiddingBuddyServicesClient servicesClient, IConfiguration config) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonLdOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private string BaseUrl => (config["Frontend:BaseUrl"] ?? "https://tendersagent.com").TrimEnd('/');
    private string Abs(string path) => path.StartsWith("http") ? path : $"{BaseUrl}{(path.StartsWith('/') ? "" : "/")}{path}";

    // ── Routes ──────────────────────────────────────────────────────────────────

    [HttpGet("/")]
    public IActionResult Landing()
    {
        var jsonLd = JsonArray(
            new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org", ["@type"] = "Organization",
                ["name"] = "Tenders Agent", ["url"] = Abs("/"),
                ["logo"] = Abs("/images/logo/app-icon.png"),
                ["description"] = SiteDescription,
            },
            new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org", ["@type"] = "WebSite",
                ["name"] = "Tenders Agent", ["url"] = Abs("/"),
                ["potentialAction"] = new Dictionary<string, object?>
                {
                    ["@type"] = "SearchAction",
                    ["target"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "EntryPoint",
                        ["urlTemplate"] = Abs("/explore?q={search_term_string}"),
                    },
                    ["query-input"] = "required name=search_term_string",
                },
            });

        var body = new StringBuilder();
        body.Append("<main>");
        body.Append("<h1>Discover &amp; win Government e-Marketplace (GeM) tenders</h1>");
        body.Append($"<p>{E(SiteDescription)}</p>");
        body.Append("<p><a href=\"").Append(E(Abs("/explore"))).Append("\">Explore live GeM tenders</a></p>");

        body.Append("<h2>How it works</h2><ul>");
        foreach (var (t, d) in new[]
        {
            ("Discover", "Browse every live GeM tender, filtered to what your business can actually win."),
            ("Analyse", "AI reads each tender — eligibility, win probability, risks and required documents."),
            ("Win", "Build and track your bid from discovery to award, with deadline reminders."),
        })
            body.Append("<li><strong>").Append(E(t)).Append("</strong> — ").Append(E(d)).Append("</li>");
        body.Append("</ul>");

        body.Append("<h2>What you get</h2><ul>");
        foreach (var (t, d) in new[]
        {
            ("Tender discovery", "Search the full GeM corpus by category, state, value and deadline — refreshed daily."),
            ("AI compliance analysis", "Know your eligibility gaps and required documents before you commit to a bid."),
            ("Win probability", "A data-driven score for every tender, matched to your category and track record."),
            ("Bid management", "A clear pipeline from discovery to delivery, with checklists and team assignments."),
            ("Competitor intelligence", "See who you are up against and how they bid across categories and states."),
            ("Deadline alerts", "Never miss a closing date or corrigendum on a tender you are tracking."),
        })
            body.Append("<li><strong>").Append(E(t)).Append("</strong> — ").Append(E(d)).Append("</li>");
        body.Append("</ul></main>");

        return Html(Page(
            "Tenders Agent — Discover & Win Government (GeM) Tenders",
            SiteDescription, "/", body.ToString(), jsonLd));
    }

    [HttpGet("/explore")]
    public async Task<IActionResult> Explore(CancellationToken ct)
    {
        var paged = await servicesClient.SearchTendersPagedAsync(
            new TenderSearchQueryDto { Page = 1, PageSize = 50, SortBy = "published", SortOrder = "desc" }, ct);

        var body = new StringBuilder();
        body.Append("<main>");
        body.Append("<h1>Explore Government (GeM) tenders</h1>");
        body.Append($"<p>Browse {paged.TotalCount:N0} live Government e-Marketplace tenders by category, state and value. ");
        body.Append("Each listing links to full eligibility, financial and timeline detail.</p>");
        body.Append("<ul>");
        foreach (var t in paged.Items)
        {
            var href = Abs($"/explore/{Seg(t.Title, t.Id)}");
            var meta = string.Join(" · ", new[] { t.Category, t.State, FmtValue(t.TenderValue) }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            body.Append("<li><a href=\"").Append(E(href)).Append("\">").Append(E(t.Title)).Append("</a>");
            if (meta.Length > 0) body.Append(" — ").Append(E(meta));
            body.Append("</li>");
        }
        body.Append("</ul></main>");

        return Html(Page(
            "Explore Government (GeM) Tenders — Tenders Agent",
            "Browse live Government e-Marketplace (GeM) tenders by category, state and value. Search thousands of open government tenders and bidding opportunities across India.",
            "/explore", body.ToString()));
    }

    [HttpGet("/explore/{slug}")]
    public async Task<IActionResult> TenderDetail(string slug, CancellationToken ct)
    {
        var id = SeoHelpers.ExtractTenderId(slug);

        PublicTenderDetailDto t;
        try
        {
            var tender = await servicesClient.GetTenderAsync(id, ct);
            t = tender.ToPublic();
        }
        catch (InvalidOperationException) { return NotFoundPage(); }
        catch (FormatException) { return NotFoundPage(); }

        var canonicalPath = $"/explore/{Seg(t.Title, t.Id)}";
        var closed = t.ClosingDate is { } cd && cd < DateOnly.FromDateTime(DateTime.UtcNow);

        var descParts = new[]
        {
            string.IsNullOrWhiteSpace(t.Category) ? "Government (GeM) tender" : $"{t.Category} tender",
            !string.IsNullOrWhiteSpace(t.BuyerOrgName) ? $"by {t.BuyerOrgName}" : (string.IsNullOrWhiteSpace(t.Ministry) ? null : $"by {t.Ministry}"),
            string.IsNullOrWhiteSpace(t.State) ? null : $"in {t.State}",
            closed ? "(closed)" : (t.ClosingDate is { } c ? $"closing {FmtDate(c)}" : null),
        }.Where(s => !string.IsNullOrWhiteSpace(s));
        var description = $"{string.Join(" ", descParts)}. View eligibility, value and documents on Tenders Agent.";

        var jsonLd = JsonArray(
            new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org", ["@type"] = "GovernmentService",
                ["name"] = t.Title,
                ["description"] = string.IsNullOrWhiteSpace(t.Description) ? description : t.Description,
                ["serviceType"] = string.IsNullOrWhiteSpace(t.Category) ? null : t.Category,
                ["serviceArea"] = string.IsNullOrWhiteSpace(t.State) ? null
                    : new Dictionary<string, object?> { ["@type"] = "AdministrativeArea", ["name"] = t.State },
                ["provider"] = new[] { t.BuyerOrgName, t.Department, t.Ministry }
                        .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) is { } prov
                    ? new Dictionary<string, object?> { ["@type"] = "GovernmentOrganization", ["name"] = prov }
                    : null,
                ["datePosted"] = t.PublishedDate?.ToString("yyyy-MM-dd"),
                ["url"] = Abs(canonicalPath),
            },
            new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList",
                ["itemListElement"] = new object[]
                {
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Home", ["item"] = Abs("/") },
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = "Explore tenders", ["item"] = Abs("/explore") },
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 3, ["name"] = t.Title, ["item"] = Abs(canonicalPath) },
                },
            });

        var body = new StringBuilder();
        body.Append("<main>");
        body.Append("<p><a href=\"").Append(E(Abs("/explore"))).Append("\">← Back to tenders</a></p>");
        body.Append("<h1>").Append(E(t.Title)).Append("</h1>");

        var sub = string.Join(" · ", new[] { t.BuyerOrgName, string.Join(", ", new[] { t.City, t.State }.Where(s => !string.IsNullOrWhiteSpace(s))), t.Category, t.Status }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (sub.Length > 0) body.Append("<p>").Append(E(sub)).Append("</p>");

        // Key facts
        body.Append("<h2>Key facts</h2><ul>");
        AppendFact(body, "Tender value", FmtValue(t.TenderValue));
        AppendFact(body, "EMD", FmtValue(t.EmdAmount));
        AppendFact(body, "Published", t.PublishedDate is { } pd ? FmtDate(pd) : null);
        AppendFact(body, "Closing date", t.ClosingDate is { } cld ? FmtDate(cld) : null);
        AppendFact(body, "Status", t.Status);
        AppendFact(body, "Ministry", t.Ministry);
        AppendFact(body, "Department", t.Department);
        AppendFact(body, "Office", t.Office);
        body.Append("</ul>");

        if (!string.IsNullOrWhiteSpace(t.Description))
            body.Append("<h2>Description</h2><p>").Append(E(t.Description)).Append("</p>");

        // Eligibility / qualification
        if (t.Qualification is { } q)
        {
            body.Append("<h2>Eligibility &amp; requirements</h2><ul>");
            AppendFact(body, "Experience required", q.ExperienceYears is { } y ? $"{y} years" : null);
            AppendFact(body, "Past performance", q.PastPerformancePercentage is { } p ? $"{p}%" : null);
            AppendFact(body, "Startup relaxation", q.StartupRelaxation ? "Yes" : "No");
            AppendFact(body, "MSE relaxation", q.MseRelaxation ? "Yes" : "No");
            if (q.Certifications is { Length: > 0 })
                AppendFact(body, "Certifications", string.Join(", ", q.Certifications));
            if (q.RequiredDocuments is { Length: > 0 })
                AppendFact(body, "Documents required", string.Join(", ", q.RequiredDocuments));
            body.Append("</ul>");
        }

        // Commercial / compliance
        if (t.Commercial is not null || t.Compliance is not null)
        {
            body.Append("<h2>Commercial terms</h2><ul>");
            if (t.Commercial is { } cc)
            {
                AppendFact(body, "Bid type", cc.BidType);
                AppendFact(body, "Evaluation", cc.EvaluationMethod);
            }
            if (t.Compliance is { } cm)
            {
                AppendFact(body, "Make in India (MII)", cm.MiiPreference ? "Yes" : "No");
                AppendFact(body, "MSE preference", cm.MsePreference ? "Yes" : "No");
            }
            body.Append("</ul>");
        }

        // Line items
        if (t.Items is { Count: > 0 })
        {
            body.Append("<h2>Line items (").Append(t.Items.Count).Append(")</h2><ul>");
            foreach (var it in t.Items.Take(20))
            {
                var qty = it.Quantity is { } qn ? $" — Qty: {qn}{(string.IsNullOrWhiteSpace(it.Unit) ? "" : " " + it.Unit)}" : "";
                body.Append("<li>").Append(E(it.Name ?? "Item")).Append(E(qty)).Append("</li>");
            }
            body.Append("</ul>");
        }

        if (t.SourceDocuments is { Count: > 0 })
            body.Append("<p>").Append(t.SourceDocuments.Count).Append(" tender document(s) available — sign up to download.</p>");

        body.Append("<p><a href=\"").Append(E(Abs("/explore"))).Append("\">Browse more GeM tenders</a></p>");
        body.Append("</main>");

        // Full archive: closed tenders stay indexable (the page shows their status);
        // only genuine 404s are noindex. `closed` still drives the meta description.
        return Html(Page(
            $"{t.Title} — GeM Tender | Tenders Agent",
            description, canonicalPath, body.ToString(), jsonLd));
    }

    [HttpGet("/about")]
    public IActionResult About()
    {
        var body = new StringBuilder();
        body.Append("<main><h1>About Tenders Agent</h1>");
        body.Append("<p>Tenders Agent aggregates live Government e-Marketplace (GeM) tenders and uses AI to surface eligibility, win probability and required documents so Indian businesses win more public contracts.</p>");
        body.Append("<h2>What we do</h2><ul>");
        foreach (var (t, d) in new[]
        {
            ("Aggregate", "We continuously gather live GeM tenders and refresh the corpus every day."),
            ("Analyse", "Our AI reads each tender for eligibility, required documents and win probability."),
            ("Win together", "Teams plan, assign and track bids from discovery to award in one workspace."),
        })
            body.Append("<li><strong>").Append(E(t)).Append("</strong> — ").Append(E(d)).Append("</li>");
        body.Append("</ul></main>");

        return Html(Page(
            "About Tenders Agent — GeM Tender Intelligence for Indian Businesses",
            "Tenders Agent aggregates live Government e-Marketplace (GeM) tenders and uses AI to surface eligibility, win probability and required documents so Indian businesses win more public contracts.",
            "/about", body.ToString()));
    }

    [HttpGet("/contact")]
    public IActionResult Contact()
    {
        var body = "<main><h1>Contact Tenders Agent</h1>"
            + "<p>Questions about GeM tender discovery, AI analysis, pricing or partnerships? "
            + "Email <a href=\"mailto:hello@biddingbuddy.in\">hello@biddingbuddy.in</a>.</p></main>";

        return Html(Page(
            "Contact Tenders Agent",
            "Get in touch with the Tenders Agent team for questions about GeM tender discovery, AI analysis, pricing or partnerships.",
            "/contact", body));
    }

    [HttpGet("/privacy")]
    public IActionResult Privacy() => Html(Page(
        "Privacy Policy — Tenders Agent",
        "How Tenders Agent collects, uses and protects your information.",
        "/privacy",
        "<main><h1>Privacy Policy</h1><p>This Privacy Policy explains what information Tenders Agent collects, how we use it, and the choices you have. See the full policy in the app.</p></main>"));

    [HttpGet("/terms")]
    public IActionResult Terms() => Html(Page(
        "Terms of Service — Tenders Agent",
        "The terms that govern your access to and use of Tenders Agent.",
        "/terms",
        "<main><h1>Terms of Service</h1><p>These terms govern your access to and use of Tenders Agent. See the full terms in the app.</p></main>"));

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private const string SiteDescription =
        "Discover, track and win Government e-Marketplace (GeM) tenders. Tenders Agent gives Indian businesses AI-powered tender search, eligibility analysis and bid management.";

    private static void AppendFact(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("<li><strong>").Append(E(label)).Append(":</strong> ").Append(E(value)).Append("</li>");
    }

    private string Seg(string? title, Guid id)
    {
        var slug = SeoHelpers.Slugify(title);
        return slug.Length > 0 ? $"{slug}-{id}" : id.ToString();
    }

    private static string E(string? s) => SeoHelpers.HtmlEscape(s);

    private static string FmtDate(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static string? FmtValue(decimal? v)
    {
        if (v is not { } n || n <= 0) return null;
        if (n >= 10_000_000) return $"₹{n / 10_000_000:0.0}Cr";
        if (n >= 100_000) return $"₹{n / 100_000:0.0}L";
        if (n >= 1_000) return $"₹{n / 1_000:0}K";
        return $"₹{n:0}";
    }

    private string JsonArray(params Dictionary<string, object?>[] objects)
    {
        // Drop null-valued keys (WhenWritingNull doesn't apply to dictionary entries),
        // so JSON-LD doesn't carry noisy "field": null pairs.
        var cleaned = objects
            .Select(o => o.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value))
            .ToArray();
        return JsonSerializer.Serialize(cleaned, JsonLdOpts);
    }

    private ContentResult Html(string html)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }

    private ContentResult NotFoundPage()
    {
        var html = Page(
            "Tender not found — Tenders Agent",
            "This tender could not be found. Browse other live Government e-Marketplace (GeM) tenders on Tenders Agent.",
            "/explore",
            "<main><h1>Tender not found</h1><p>It may have been removed, or the link is incorrect. "
                + "<a href=\"" + E(Abs("/explore")) + "\">Back to tenders</a>.</p></main>",
            noindex: true);
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 404 };
    }

    private string Page(string title, string description, string canonicalPath, string bodyHtml,
        string? jsonLdJson = null, bool noindex = false)
    {
        var canonical = Abs(canonicalPath);
        var ogImage = Abs("/images/logo/app-icon.png");
        var robots = noindex ? "noindex, follow" : "index, follow";
        var jsonLdTag = jsonLdJson is null ? "" : $"<script type=\"application/ld+json\">{jsonLdJson}</script>";

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(title)}}</title>
        <meta name="description" content="{{E(description)}}">
        <meta name="robots" content="{{robots}}">
        <link rel="canonical" href="{{E(canonical)}}">
        <meta property="og:site_name" content="Tenders Agent">
        <meta property="og:type" content="website">
        <meta property="og:title" content="{{E(title)}}">
        <meta property="og:description" content="{{E(description)}}">
        <meta property="og:url" content="{{E(canonical)}}">
        <meta property="og:image" content="{{E(ogImage)}}">
        <meta name="twitter:card" content="summary_large_image">
        {{jsonLdTag}}
        </head>
        <body>
        {{bodyHtml}}
        <footer><p>Tenders Agent — <a href="{{E(Abs("/explore"))}}">Explore GeM tenders</a> · <a href="{{E(Abs("/about"))}}">About</a> · <a href="{{E(Abs("/contact"))}}">Contact</a></p></footer>
        </body>
        </html>
        """;
    }
}
