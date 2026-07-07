namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A registered mobile device that can receive push (FCM). Belongs to a user, not an
/// org (a device follows the person across their orgs). One row per FCM token.
/// <c>push_enabled</c> is the app's per-device "Push notifications" switch; the
/// BidProcessor sets <c>revoked_at</c> when FCM reports the token unregistered.
/// </summary>
public class UserDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Platform { get; set; } = default!;   // ios | android
    public string FcmToken { get; set; } = default!;
    public string? AppVersion { get; set; }
    public bool PushEnabled { get; set; } = true;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public User User { get; set; } = default!;
}
