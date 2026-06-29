using System.Text;
using System.Text.RegularExpressions;

namespace BiddingBuddy.Bff.Api.Seo;

/// <summary>
/// Shared helpers for the SEO surfaces (dynamic sitemap + bot HTML rendering).
/// Slugify mirrors the UI's slugify() so server-emitted tender URLs match the
/// SPA's canonical links exactly.
/// </summary>
public static partial class SeoHelpers
{
    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    /// <summary>lowercase, non-alphanumeric → single dash, trimmed, capped at 70 (mirror of UI slugify).</summary>
    public static string Slugify(string? text)
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

    /// <summary>Recover the tender GUID from a slug param ("&lt;slug&gt;-&lt;id&gt;") or a bare id.</summary>
    public static string ExtractTenderId(string? slugOrId)
    {
        if (string.IsNullOrWhiteSpace(slugOrId)) return string.Empty;
        var m = GuidRegex().Match(slugOrId);
        return m.Success ? m.Value : slugOrId;
    }

    /// <summary>XML-escape a value for sitemap &lt;loc&gt; output.</summary>
    public static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

    /// <summary>HTML-escape text for safe insertion into rendered markup.</summary>
    public static string HtmlEscape(string? s) =>
        string.IsNullOrEmpty(s)
            ? string.Empty
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}
