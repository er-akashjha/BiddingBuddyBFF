namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A signup that has NOT yet produced a <see cref="User"/>. When someone registers
/// with email + password we don't create the account immediately — we stash the
/// (already BCrypt-hashed) credentials here, email a 6-digit OTP, and only on
/// successful verification do we materialise the real user + organization.
/// Maps to <c>pending_registrations</c>.
///
/// <para>A partial unique index keeps at most one ACTIVE (un-consumed) row per
/// email; re-signing-up supersedes the prior pending row. Mirrors the
/// <see cref="OrganizationInvite"/> token pattern.</para>
/// </summary>
public class PendingRegistration
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;        // stored lowercased
    public string Name { get; set; } = default!;
    public string PasswordHash { get; set; } = default!; // BCrypt hash, computed at request time — plaintext never stored
    public string? OrgName { get; set; }                 // null when joining via an invite
    public string? Phone { get; set; }
    public string? InviteToken { get; set; }             // raw org-invite token, carried through and consumed at verify time
    public string CodeHash { get; set; } = default!;     // SHA-256 hex of the 6-digit OTP; the raw code only ever leaves via email
    public int AttemptCount { get; set; }                // wrong-code attempts; capped before the row is burned
    public int ResendCount { get; set; }                 // OTP re-issues; capped to throttle abuse
    public DateTime ExpiresAt { get; set; }              // OTP validity window
    public DateTime? ConsumedAt { get; set; }            // set when the account is created (or when superseded)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
