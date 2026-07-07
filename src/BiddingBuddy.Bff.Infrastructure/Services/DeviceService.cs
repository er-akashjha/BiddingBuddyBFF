using BiddingBuddy.Bff.Core.DTOs.Devices;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class DeviceService(BffDbContext db) : IDeviceService
{
    private static readonly HashSet<string> Platforms = new(StringComparer.Ordinal) { "ios", "android" };

    public async Task<DeviceDto> RegisterAsync(Guid userId, RegisterDeviceDto dto, CancellationToken ct = default)
    {
        var platform = (dto.Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (!Platforms.Contains(platform))
            throw new ArgumentException("Platform must be 'ios' or 'android'.");
        if (string.IsNullOrWhiteSpace(dto.FcmToken))
            throw new ArgumentException("FcmToken is required.");

        // Upsert by token — a device may change hands (user B signs in on user A's phone),
        // so re-point ownership and clear any prior revocation.
        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.FcmToken == dto.FcmToken, ct);
        if (device is null)
        {
            device = new UserDevice { FcmToken = dto.FcmToken, CreatedAt = DateTime.UtcNow };
            db.UserDevices.Add(device);
        }

        device.UserId = userId;
        device.Platform = platform;
        device.AppVersion = dto.AppVersion;
        device.LastSeenAt = DateTime.UtcNow;
        device.RevokedAt = null;
        device.RevocationReason = null;
        // Preserve an existing push_enabled choice; new rows default to enabled.

        await db.SaveChangesAsync(ct);
        return ToDto(device);
    }

    public async Task UnregisterAsync(Guid userId, string fcmToken, CancellationToken ct = default)
    {
        await db.UserDevices
            .Where(d => d.FcmToken == fcmToken && d.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SetPushEnabledAsync(Guid userId, string fcmToken, bool enabled, CancellationToken ct = default)
    {
        await db.UserDevices
            .Where(d => d.FcmToken == fcmToken && d.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.PushEnabled, enabled), ct);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var devices = await db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(ct);
        return devices.Select(ToDto).ToList();
    }

    private static DeviceDto ToDto(UserDevice d) => new(
        d.Id,
        d.Platform,
        d.FcmToken.Length <= 6 ? d.FcmToken : d.FcmToken[^6..],
        d.AppVersion,
        d.PushEnabled,
        d.LastSeenAt);
}
