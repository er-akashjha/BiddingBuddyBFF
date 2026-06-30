namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Idempotency ledger for the deadline / expiry notification scan. One row per
/// (entity, milestone) reminder that has already been emitted, so the periodic
/// <c>DeadlineScanWorker</c> never re-sends the same reminder on a later tick.
///
/// A reminder is "claimed" with <c>INSERT … ON CONFLICT DO NOTHING</c> against the
/// UNIQUE (entity_type, entity_id, reminder_key) constraint: the row is sent only
/// when the insert actually creates a row, which makes the claim atomic even across
/// concurrent scans / multiple BFF instances.
/// </summary>
public class NotificationReminder
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>bid | invoice | compliance_document | delivery_milestone | emd</summary>
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }

    /// <summary>The milestone within the entity, e.g. BID_DUE_SOON, BID_OVERDUE, COMPLIANCE_EXPIRING.</summary>
    public string ReminderKey { get; set; } = default!;

    public DateTime SentAt { get; set; }
}
