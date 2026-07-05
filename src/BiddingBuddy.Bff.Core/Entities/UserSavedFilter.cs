namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A per-user, per-org saved tender-filter set. Two kinds share the table:
///   • <c>last_used</c> — a single auto-upserted snapshot of the filters the user
///     last had applied on the Tenders page (restored on their next visit). Exactly
///     one per (user, org) — enforced by a partial unique index.
///   • <c>named</c> — an explicitly saved, named view the user can re-apply. Many
///     per (user, org).
/// The <see cref="Filters"/> blob is stored as jsonb so the shape can evolve without
/// a migration.
/// </summary>
public class UserSavedFilter
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>"last_used" or "named".</summary>
    public string Kind { get; set; } = SavedFilterKinds.Named;

    /// <summary>Display name for a "named" view; null for the "last_used" snapshot.</summary>
    public string? Name { get; set; }

    public SavedFilterState Filters { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = default!;
    public Organization Organization { get; set; } = default!;
}

public static class SavedFilterKinds
{
    public const string LastUsed = "last_used";
    public const string Named = "named";
}

/// <summary>
/// The tender-filter selection, mirroring the query state of the Tenders page.
/// Persisted as jsonb; all fields optional so partial selections round-trip cleanly.
/// </summary>
public class SavedFilterState
{
    public string? Q { get; set; }
    public string[]? Categories { get; set; }
    public string[]? States { get; set; }
    public string? Status { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Sort { get; set; }
}
