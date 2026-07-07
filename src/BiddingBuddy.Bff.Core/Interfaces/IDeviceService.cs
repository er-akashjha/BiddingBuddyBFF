using BiddingBuddy.Bff.Core.DTOs.Devices;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IDeviceService
{
    /// <summary>Upsert a device by FCM token: re-points it to this user, refreshes
    /// last-seen/app-version, and clears any prior revocation. Call on every launch.</summary>
    Task<DeviceDto> RegisterAsync(Guid userId, RegisterDeviceDto dto, CancellationToken ct = default);

    /// <summary>Remove a device (logout). No-op if it isn't the caller's.</summary>
    Task UnregisterAsync(Guid userId, string fcmToken, CancellationToken ct = default);

    /// <summary>Flip the per-device push switch (the app's notification-settings toggle).</summary>
    Task SetPushEnabledAsync(Guid userId, string fcmToken, bool enabled, CancellationToken ct = default);

    /// <summary>List the caller's active devices (for a "signed-in devices" view).</summary>
    Task<IReadOnlyList<DeviceDto>> ListAsync(Guid userId, CancellationToken ct = default);
}
