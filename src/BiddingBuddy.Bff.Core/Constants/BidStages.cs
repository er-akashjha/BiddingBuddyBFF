namespace BiddingBuddy.Bff.Core.Constants;

/// <summary>
/// Canonical bid workflow stages. This is the single source of truth on the backend and
/// must stay in lockstep with the <c>bids_stage_check</c> constraint (migration 0013) and
/// the frontend <c>bidStages</c> module. <see cref="StatusCategoryFor"/> mirrors the
/// generated <c>bids.status_category</c> column.
/// </summary>
public static class BidStages
{
    public const string StatusOpen = "open";
    public const string StatusClosed = "closed";

    /// <summary>Active pipeline stages, in workflow order.</summary>
    public static readonly IReadOnlyList<string> Open =
        ["identified", "reviewing", "preparing", "approval", "submitted"];

    /// <summary>Terminal stages — folded into the "Closed" group in the UI.</summary>
    public static readonly IReadOnlyList<string> Closed =
        ["won", "lost", "dropped"];

    /// <summary>Every valid stage, open then closed, in display order.</summary>
    public static readonly IReadOnlyList<string> All =
        [.. Open, .. Closed];

    /// <summary>True if <paramref name="stage"/> is one of the canonical stages.</summary>
    public static bool IsValid(string? stage) =>
        stage is not null && All.Contains(stage);

    /// <summary>
    /// The status category a stage maps to. Matches the generated DB column exactly so the
    /// two never disagree.
    /// </summary>
    public static string StatusCategoryFor(string stage) =>
        Closed.Contains(stage) ? StatusClosed : StatusOpen;

    /// <summary>Valid status-category filter values.</summary>
    public static bool IsValidStatusCategory(string? category) =>
        category is StatusOpen or StatusClosed;
}
