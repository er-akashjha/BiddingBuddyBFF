namespace BiddingBuddy.Bff.Core.DTOs.Devices;

/// <summary>Register (or refresh) the caller's push device. Idempotent on the FCM token.</summary>
public record RegisterDeviceDto(string Platform, string FcmToken, string? AppVersion);

/// <summary>Toggle the app's per-device "Push notifications" switch.</summary>
public record SetDevicePushDto(string FcmToken, bool Enabled);

/// <summary>A registered device as returned to the app (token elided to a short suffix).</summary>
public record DeviceDto(
    Guid Id,
    string Platform,
    string TokenSuffix,
    string? AppVersion,
    bool PushEnabled,
    DateTime LastSeenAt);
