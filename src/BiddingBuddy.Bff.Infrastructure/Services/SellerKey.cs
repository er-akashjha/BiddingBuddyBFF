using System.Text.RegularExpressions;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Canonical identity key for a GeM seller name.
///
/// The BFF uses it to find an ORG's own row on an award ladder (to resolve their bid won/lost) and
/// to spot a tracked competitor in a winner. GeM prints seller names as free text, so the same
/// company appears as "M/S ABC PVT. LTD.", "ABC Private Limited" and "Abc Pvt Ltd" — matching on
/// the raw string means an org whose name differs only by legal suffix never resolves.
///
/// ⚠ KEEP IN SYNC — duplicated from BiddingBuddyServices
/// (src/BiddingBuddy.Core/Domain/SellerKey.cs), which computes and indexes this key at ingest,
/// exactly as TenderTaxonomy is duplicated across Services + BidProcessor until a shared contracts
/// package exists. If the two drift, an org matches in market analytics but not in its own bid
/// resolution — the confusing half-broken state. Edit both together.
///
/// Supersedes the older local NormalizeSeller, which stopped at casing/punctuation.
/// </summary>
public static class SellerKey
{
    /// <summary>
    /// Legal-form tokens only. Business descriptors ("Traders", "Enterprises", "Industries") are
    /// NOT here on purpose — "ABC Traders" and "ABC Enterprises" are different firms, and stripping
    /// those would collapse genuinely distinct sellers into one (and, here, could mis-resolve one
    /// org's bid against another firm's ladder row).
    /// </summary>
    private static readonly HashSet<string> LegalFormTokens = new(StringComparer.Ordinal)
    {
        "pvt", "private", "ltd", "limited", "llp", "inc", "incorporated",
        "corp", "corporation", "co", "company", "opc",
    };

    /// <summary>
    /// The "Messrs" prefix, which is noise. Matched against the RAW name, before punctuation is
    /// stripped, because the slash is the only thing distinguishing the prefix "M/S ABC" from a
    /// company whose name genuinely begins with the initials "M S" (e.g. "M S Dhoni Sports").
    /// Once punctuation is gone both look like ["m","s",…] and the two are indistinguishable.
    /// </summary>
    private static readonly Regex MessrsPrefixRx =
        new(@"^\s*(m\s*/\s*s\.?|messrs\.?)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Normalize a seller name to its identity key. Never throws; "" only for blank input.</summary>
    public static string For(string? sellerName)
    {
        if (string.IsNullOrWhiteSpace(sellerName)) return string.Empty;

        var withoutPrefix = MessrsPrefixRx.Replace(sellerName, string.Empty);
        // A name that is ONLY the prefix would otherwise normalize to nothing.
        if (string.IsNullOrWhiteSpace(withoutPrefix)) withoutPrefix = sellerName;

        var cleaned = new string(withoutPrefix.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0) return string.Empty;

        var stripped = tokens.Where(t => !LegalFormTokens.Contains(t)).ToList();

        // A name made ENTIRELY of legal-form tokens is not a name we can shorten — keep the
        // unstripped form rather than collapsing every such seller onto the same empty key.
        return stripped.Count > 0
            ? string.Join(' ', stripped)
            : string.Join(' ', tokens);
    }
}
