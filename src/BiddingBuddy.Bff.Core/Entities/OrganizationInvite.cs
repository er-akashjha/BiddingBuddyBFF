namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A pending invite for an email that does not yet have a <see cref="User"/> row.
/// The recipient receives a registration link with a single-use token; on register,
/// the token is validated and a fresh user + <see cref="OrgMember"/> in the inviting
/// org are created. Maps to <c>organization_invites</c>.
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
