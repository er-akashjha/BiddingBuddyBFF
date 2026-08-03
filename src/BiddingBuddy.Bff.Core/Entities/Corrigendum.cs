namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// An amendment to a published tender. Date extensions and corrections are a constant, first-class
/// workflow in government procurement — not an exception path — and bidders may have to resubmit
/// because of one, so every corrigendum notifies the suppliers our matching rail already told about
/// the tender.
///
/// <para>Append-only: a corrigendum never edits its predecessor, and issuing one appends a
/// <see cref="TenderVersion"/> rather than mutating the last.</para>
///
/// Maps to <c>corrigenda</c> (migration 0031).
/// </summary>
public class Corrigendum
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }

    /// <summary>The version row this corrigendum produced. Nullable only for the instant between
    /// the two inserts inside one transaction.</summary>
    public Guid? VersionId { get; set; }

    /// <summary>1-based, per draft. This is the number the notice is known by ("Corrigendum 2").</summary>
    public int CorrigendumNo { get; set; }

    /// <summary>date_extension | amendment | cancellation | retender.</summary>
    public string Type { get; set; } = "amendment";

    /// <summary>Mandatory. An unexplained amendment to a live tender is the single most
    /// audit-sensitive thing a procuring officer can do.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Field-level diff as JSON: <c>[{ field, label, oldValue, newValue }]</c>. Rendered as
    /// the diff bidders see, and replayable against the preceding snapshot.</summary>
    public string Changes { get; set; } = "[]";

    /// <summary>When the bidder notification went out. NULL means it has not — which is a real
    /// state worth being able to see, since an extension nobody was told about is not an extension.</summary>
    public DateTime? NotifiedAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public TenderDraft Draft { get; set; } = default!;
}
