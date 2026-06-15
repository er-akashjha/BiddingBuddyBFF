using BiddingBuddy.Bff.Core.DTOs.Notifications;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>Admin CRUD over notification_templates.</summary>
public interface INotificationTemplateService
{
    Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(string? code, string? channel, CancellationToken ct = default);
    Task<NotificationTemplateDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<NotificationTemplateDto> CreateAsync(CreateNotificationTemplateDto dto, CancellationToken ct = default);
    Task<NotificationTemplateDto> UpdateAsync(Guid id, UpdateNotificationTemplateDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
