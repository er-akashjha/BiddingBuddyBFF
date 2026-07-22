using System.Text;

namespace BiddingBuddy.Bff.Core.Helpers;

/// <summary>
/// Normalizes the two fields that identify a company at signup, so
/// "ACME Private Limited" and "Acme Pvt. Ltd." are recognised as the same claim.
/// </summary>
public static class OrgIdentity
{
    /// <summary>
    /// Legal-form noise. Removed before comparing names because they carry no
    /// distinguishing information — every second Indian SME name ends in some
    /// spelling of "private limited", and which spelling a user typed says
    /// nothing about whether it is the same company.
    /// </summary>
    private static readonly HashSet<string> FormTokens = new(StringComparer.Ordinal)
    {
        "private", "pvt", "limited", "ltd", "llp", "plc",
        "inc", "incorporated", "corp", "corporation",
        "company", "co", "and", "the",
    };

    /// <summary>
    /// Uppercase, whitespace stripped; null for blank input.
    ///
    /// <para>The SQL side of the comparison is <c>upper(replace(gstin, ' ', ''))</c>, which is
    /// what migration 0030 indexes. Keep the C# query expression in that same order
    /// (<c>.Replace(" ", "").ToUpper()</c>) or EF renders a different expression and the
    /// probe quietly stops using the index.</para>
    /// </summary>
    public static string? NormalizeGstin(string? gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin)) return null;

        var sb = new StringBuilder(gstin.Length);
        foreach (var c in gstin)
        {
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// Lowercased, punctuation folded to spaces, legal-form tokens dropped, single-spaced.
    /// <c>"Acme Pvt. Ltd."</c> and <c>"ACME PRIVATE LIMITED"</c> both become <c>"acme"</c>.
    /// Returns "" for input that is entirely noise, which never matches anything.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Fold anything that isn't a letter or digit to a space: this collapses
        // "Acme-Tech", "Acme.Tech" and "Acme & Tech" onto the same token stream.
        var folded = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            folded.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var kept = folded.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !FormTokens.Contains(t));

        return string.Join(' ', kept);
    }

    /// <summary>
    /// The first significant token of a name — the prefix the SQL candidate query
    /// matches on (<c>lower(name) LIKE 'acme%'</c>).
    ///
    /// <para>Prefix rather than substring is what lets a btree index serve the query. The
    /// cost is asymmetry: "The Acme Company" finds "Acme Pvt Ltd" (its first significant
    /// token is still "acme"), but not the reverse, because the stored name starts with
    /// "the". Acceptable for a soft warning — anyone who supplies a GSTIN is caught
    /// regardless of spelling.</para>
    ///
    /// <para>Returns null when there is no usable token, which callers treat as
    /// "no name signal" rather than "matches everything".</para>
    /// </summary>
    public static string? NamePrefix(string? name)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length == 0) return null;

        var first = normalized.Split(' ', 2)[0];

        // A one- or two-character token ("hp", "3m") is a legitimate company name but a
        // terrible prefix — it would drag in every org starting with those letters. Keep
        // it anyway: the candidate list is capped and the C# pass does the real comparison.
        return first;
    }
}
