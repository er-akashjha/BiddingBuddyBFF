namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>A resolved notification target: who to notify + how to reach them.</summary>
public record NotificationAudienceMember(Guid UserId, string Name, string? Email);

/// <summary>
/// Resolves org members into notification recipients. Shared by the deadline scan
/// and the inline bid hooks so role → user fan-out lives in one place (mirrors the
/// recipient query the tender-match digest already uses).
/// </summary>
public interface INotificationAudienceResolver
{
    /// <summary>
    /// Active members of <paramref name="orgId"/> whose role is in <paramref name="roles"/>,
    /// excluding <paramref name="excludeUserId"/> (e.g. the actor who triggered the event).
    /// </summary>
    Task<IReadOnlyList<NotificationAudienceMember>> ByRolesAsync(
        Guid orgId, IReadOnlyCollection<string> roles, Guid? excludeUserId = null, CancellationToken ct = default);

    /// <summary>A single user as a recipient, or null if the user is missing / inactive.</summary>
    Task<NotificationAudienceMember?> ByUserAsync(Guid userId, CancellationToken ct = default);
}
