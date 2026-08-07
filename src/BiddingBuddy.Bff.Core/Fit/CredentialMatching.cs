using System.Text.RegularExpressions;

namespace BiddingBuddy.Bff.Core.Fit;

/// <summary>
/// Matches a tender's wording for a credential against the code an org stored.
///
/// <para>A tender writes "ISO 9001:2015 certification", "ISO-9001 certificate" or "valid ISO 9001
/// QMS certificate" for the same thing. Exact equality would miss all three and report a
/// certificate the org holds as missing — a blocker, which is the most damaging direction for a
/// false positive to point.</para>
///
/// <para>It is deliberately conservative in the other direction too. Matching is anchored on the
/// standard NUMBER (9001, 27001), never on the loose words around it, so "ISO 27001" never
/// satisfies a requirement for "ISO 9001". Every finding produced through this path is marked
/// medium confidence, because a normalised string comparison is not an identity.</para>
/// </summary>
public static partial class CredentialMatching
{
    /// <summary>
    /// Reduce free-text to a comparable token: upper-cased, punctuation flattened, and — for an
    /// ISO standard — collapsed to "ISO &lt;number&gt;", dropping the edition year. A tender
    /// asking for "ISO 9001:2008" is satisfied by a current "ISO 9001:2015"; treating the year as
    /// part of the identity would flag every up-to-date certificate as the wrong one.
    /// </summary>
    public static string Normalise(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.ToUpperInvariant().Replace('-', ' ').Replace('_', ' ');
        s = NonAlphanumeric().Replace(s, " ");
        s = Whitespace().Replace(s, " ").Trim();

        var iso = IsoStandard().Match(s);
        if (iso.Success) return $"ISO {iso.Groups[1].Value}";

        foreach (var (token, canonical) in Aliases)
            if (s.Contains(token, StringComparison.Ordinal))
                return canonical;

        return s;
    }

    /// <summary>
    /// Does a stored credential code satisfy a normalised requirement? Both sides are normalised,
    /// then compared for equality or containment. Containment is one-directional and length-guarded
    /// so a two-character stored code can't satisfy an arbitrary requirement.
    /// </summary>
    public static bool Matches(string storedCode, string normalisedRequirement)
    {
        if (string.IsNullOrWhiteSpace(storedCode) || string.IsNullOrWhiteSpace(normalisedRequirement))
            return false;

        var stored = Normalise(storedCode);
        if (stored.Length == 0) return false;
        if (string.Equals(stored, normalisedRequirement, StringComparison.Ordinal)) return true;

        // A stored "UDYAM" satisfies a requirement normalised to "UDYAM REGISTRATION CERTIFICATE".
        // Guarded at 4 characters so short codes don't sweep up unrelated requirements.
        return stored.Length >= 4 && normalisedRequirement.Contains(stored, StringComparison.Ordinal);
    }

    /// <summary>Terms that mean the same credential under different names. Kept small and
    /// explicit — a large fuzzy alias table is how a matcher starts silently satisfying
    /// requirements it shouldn't.</summary>
    private static readonly (string Token, string Canonical)[] Aliases =
    [
        ("UDYAM", "UDYAM"),
        ("MSME", "UDYAM"),
        ("DPIIT", "DPIIT"),
        ("STARTUP INDIA", "DPIIT"),
        ("NSIC", "NSIC"),
        ("GSTIN", "GST"),
        ("GST", "GST"),
        ("OEM AUTHORIS", "OEM"),
        ("OEM AUTHORIZ", "OEM"),
        ("MANUFACTURER AUTHORIS", "OEM"),
        ("MANUFACTURER AUTHORIZ", "OEM"),
    ];

    [GeneratedRegex(@"ISO\s*(\d{4,5})")]
    private static partial Regex IsoStandard();

    [GeneratedRegex(@"[^A-Z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
