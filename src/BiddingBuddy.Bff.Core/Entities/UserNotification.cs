namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// The in-app notification inbox (what the UI lists at /api/notifications).
/// Renamed from <c>Notification</c> to free that name for the dispatch-event
/// table introduced by the notification subsystem (see <see cref="NotificationEvent"/>
/// or rather the new <see cref="NotificationDispatch"/> family).
///
/// Maps to PostgreSQL table <c>user_notifications</c>.
/// </summary>
public class UserNotification
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User User { get; set; } = default!;
}
