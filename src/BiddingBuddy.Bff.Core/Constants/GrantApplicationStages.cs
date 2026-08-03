namespace BiddingBuddy.Bff.Core.Constants;

/// <summary>
/// The grant application stage vocabulary. Case-sensitive — these exact strings back the
/// <c>ck_grant_applications_stage</c> CHECK constraint and the generated <c>status_category</c>.
/// Distinct from <c>BidStages</c> (the tender pipeline): a grant is qualified, planned, written,
/// reviewed, submitted, then awarded/declined/dropped.
/// </summary>
public static class GrantApplicationStages
{
    public static readonly string[] All =
        ["Qualifying", "Planning", "Writing", "Review", "Submitted", "Awarded", "Declined", "Dropped"];

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool IsValid(string? stage) => stage is not null && Set.Contains(stage);

    /// <summary>Terminal stages — these roll the generated <c>status_category</c> up to "closed".</summary>
    public static bool IsClosed(string stage) => stage is "Awarded" or "Declined" or "Dropped";

    public static bool IsValidStatusCategory(string? category)
        => category is "open" or "closed";
}
