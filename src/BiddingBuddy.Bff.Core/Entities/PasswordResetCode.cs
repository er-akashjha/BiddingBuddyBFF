namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A pending password-reset for an existing <see cref="User"/>. "Forgot password"
/// emails a 6-digit OTP whose SHA-256 hash is stored here; the password is changed
/// only when the matching code is submitted to <c>POST /api/auth/reset-password</c>.
/// Maps to <c>password_reset_codes</c>.
///
/// <para>A partial unique index keeps at most one ACTIVE (un-consumed) row per user;
/// requesting again supersedes the prior code. Mirrors <see cref="PendingRegistration"/>.</para>
/// </summary>
public class PasswordResetCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = default!;     // SHA-256 hex of the 6-digit OTP; the raw code only ever leaves via email
    public int AttemptCount { get; set; }                // wrong-code attempts; capped before the row is burned
    public int ResendCount { get; set; }                 // re-issues; capped to throttle abuse
    public DateTime ExpiresAt { get; set; }              // OTP validity window
    public DateTime? ConsumedAt { get; set; }            // set when the password is changed (or when superseded)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = default!;
}
