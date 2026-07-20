namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>
/// Resolves what kind of record a tender is — an ordinary bid, or a reverse auction.
///
/// The pipeline stamps <c>source.bidKind</c> going forward, but every tender enriched before
/// that field existed has it null. Rather than gate the feature on a backfill, this re-derives
/// the kind from the bid number, which GeM has always encoded: <c>GEM/{yyyy}/{B|R}/{n}</c>.
/// Stored value wins when present; the derivation is the fallback.
///
/// Deliberately distinct from <c>Commercial.ReverseAuction.Enabled</c>: that flag is AI-inferred
/// and means "this bid's process includes a reverse-auction phase", which an ordinary bid can
/// have. It is also unset on precisely the tenders that ARE reverse auctions, because the AI
/// never runs on them — which is why it can't serve this purpose.
/// </summary>
public static class TenderKind
{
    public const string Bid            = "bid";
    public const string ReverseAuction = "reverse-auction";

    /// <summary>
    /// The kind for a tender, or null when it can't be determined (callers render nothing
    /// rather than guessing). <paramref name="stored"/> is <c>source.bidKind</c>;
    /// <paramref name="platformTenderId"/> is the portal bid number.
    /// </summary>
    public static string? Resolve(string? stored, string? platformTenderId)
    {
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        return DeriveFromBidNumber(platformTenderId);
    }

    /// <summary>Reads the type segment out of a GeM bid number; null for any other shape.</summary>
    public static string? DeriveFromBidNumber(string? platformTenderId)
    {
        if (string.IsNullOrWhiteSpace(platformTenderId)) return null;

        var parts = platformTenderId.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        return parts[2].Trim().ToUpperInvariant() switch
        {
            "R" => ReverseAuction,
            "B" => Bid,
            _   => null,
        };
    }
}
