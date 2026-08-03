namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One recorded action against a buyer-side entity: who, when, what changed from what to what.
///
/// <para><b>Why this has no foreign key to what it describes.</b> An audit file has to survive the
/// deletion of its subject. A FK with <c>ON DELETE CASCADE</c> would erase precisely the evidence an
/// inspector came to look at, and one without a cascade would block the delete instead — so the
/// reference is a loose <c>(EntityType, EntityId)</c> pair by design, not by omission.</para>
///
/// <para>The actor's name and role are DENORMALISED for the same reason: resolving them at read
/// time reports today's role, not the one the change was actually made under, and a user who has
/// since left the organization would resolve to nothing at all.</para>
///
/// Maps to <c>audit_events</c> (migration 0031).
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>tender_draft | corrigendum | committee.</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    /// <summary>created | updated | published | corrigendum_issued | awarded | cancelled | committee_changed.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>NULL for a system action. No FK — see the class remarks.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Captured at write time, not resolved at read time.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>The org role the actor held when they acted.</summary>
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>Field-level diff as JSON: <c>[{ field, label, oldValue, newValue }]</c>.</summary>
    public string Changes { get; set; } = "[]";

    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
