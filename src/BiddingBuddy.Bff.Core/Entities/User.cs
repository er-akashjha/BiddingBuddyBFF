namespace BiddingBuddy.Bff.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? AvatarUrl { get; set; }
    public string? Phone { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// A platform operator (one of ours), permitted to work the JWT-gated <c>/api/admin/*</c>
    /// surface — currently the buyer-access review queue. Not an org role: it is orthogonal to
    /// every <c>org_members.role</c> and is never granted through any client-facing route.
    /// Defaults false; set directly in the database. Migration <c>0034</c>.
    /// </summary>
    public bool IsPlatformAdmin { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<OAuthAccount> OAuthAccounts { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<OrgMember> OrgMemberships { get; set; } = [];
    public ICollection<Organization> OwnedOrganizations { get; set; } = [];
}
