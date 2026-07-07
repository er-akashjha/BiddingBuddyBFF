namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Single-use authorization code minted by the OAuth callback for mobile clients.
/// Tokens never ride the redirect URL — the app redeems this code together with its
/// PKCE verifier at <c>POST /api/auth/oauth/exchange</c> for a normal token pair.
/// </summary>
public class OAuthExchangeCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = default!;
    /// <summary>PKCE S256 challenge pinned at initiation; verified at redeem time.</summary>
    public string CodeChallenge { get; set; } = default!;
    public bool IsNewUser { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = default!;
}
