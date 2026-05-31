using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Notifications;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(Guid orgId, Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task UpdatePreferencesAsync(Guid userId, UpdatePreferencesDto dto, CancellationToken ct = default);
}
