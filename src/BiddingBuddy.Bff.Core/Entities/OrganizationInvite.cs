namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A pending invite — membership is never granted until the invitee confirms.
/// Existing users receive a link to the SPA accept page (consumed via
/// <c>POST /api/invites/accept</c>); unregistered emails receive a registration
/// link (consumed at register/verify time, creating the user + membership together).
/// Maps to <c>organization_invites</c>.
/// </summary>
public class OrganizationInvite
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Email { get; set; } = default!;       // stored lowercased
    public string Role { get; set; } = default!;
    public string? Department { get; set; }
    public Guid InvitedBy { get; set; }                  // user id of the admin/owner who invited
    public string TokenHash { get; set; } = default!;    // SHA-256 hex of the raw token; raw token only ever leaves via email
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }            // set when the recipient registers via the invite
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User Inviter { get; set; } = default!;
}
