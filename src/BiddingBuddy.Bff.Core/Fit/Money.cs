using System.Globalization;

namespace BiddingBuddy.Bff.Core.Fit;

/// <summary>
/// Rupee formatting for finding text.
///
/// <para>Server-side rather than in the client because these strings travel: the same sentence
/// has to read identically in the web app, in the exported bid/no-bid note and in an email. A
/// finding is a self-contained statement, so the number is baked in rather than templated out.</para>
///
/// <para>Lakh/crore, not thousands/millions — the reader is an Indian bidder reading an Indian
/// tender, and "₹1.2Cr" is the unit they'll type into their own spreadsheet.</para>
/// </summary>
public static class Money
{
    public static string Inr(decimal value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 10_000_000m => $"₹{Trim(value / 10_000_000m)}Cr",
            >= 100_000m    => $"₹{Trim(value / 100_000m)}L",
            >= 1_000m      => $"₹{Trim(value / 1_000m)}K",
            _              => $"₹{value.ToString("0.##", CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>One decimal place, but no trailing ".0" — "₹2Cr" rather than "₹2.0Cr".</summary>
    private static string Trim(decimal v) =>
        Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
