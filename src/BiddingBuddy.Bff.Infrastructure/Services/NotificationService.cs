using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// In-app notification inbox service (what /api/notifications drives).
/// Operates on <see cref="UserNotification"/> (table <c>user_notifications</c>).
/// </summary>
public class NotificationService(BffDbContext db) : INotificationService
{
    public async Task<PagedResult<UserNotificationDto>> ListAsync(
        Guid orgId, Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.UserNotifications.Where(n => n.OrgId == orgId && n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(ct);
        var pg = Math.Max(1, page);
        var sz = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pg - 1) * sz).Take(sz)
            .Select(n => new UserNotificationDto(
                n.Id, n.Type, n.Title, n.Body,
                n.EntityType, n.EntityId,
                n.IsRead, n.ReadAt, n.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<UserNotificationDto>(items, total, pg, sz);
    }

    public async Task MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
    {
        var notification = await db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAllReadAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var unread = await db.UserNotifications
            .Where(n => n.OrgId == orgId && n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        if (unread.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var prefs = await db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        return prefs.Select(p => new NotificationPreferenceDto(p.Channel, p.IsEnabled, p.EventTypes)).ToList();
    }

    public async Task UpdatePreferencesAsync(Guid userId, UpdatePreferencesDto dto, CancellationToken ct = default)
    {
        // Load org for this user to get OrgId — take first active org
        var orgId = await db.OrgMembers
            .Where(m => m.UserId == userId && m.Status == "active")
            .Select(m => m.OrgId)
            .FirstOrDefaultAsync(ct);

        foreach (var update in dto.Channels)
        {
            var pref = await db.NotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Channel == update.Channel, ct);

            if (pref is null)
            {
                pref = new NotificationPreference
                {
                    UserId     = userId,
                    OrgId      = orgId,
                    Channel    = update.Channel,
                    IsEnabled  = update.IsEnabled,
                    EventTypes = update.EventTypes,
                };
                db.NotificationPreferences.Add(pref);
            }
            else
            {
                pref.IsEnabled  = update.IsEnabled;
                pref.EventTypes = update.EventTypes;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
