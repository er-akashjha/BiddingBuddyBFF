namespace BiddingBuddy.Bff.Core.Entities;

public class OAuthAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = default!;         // google | github | microsoft | linkedin
    public string ProviderUserId { get; set; } = default!;
    public string? Email { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    /// <summary>The Entra directory this identity belongs to (<c>tid</c>). Microsoft only; null for
    /// every other provider. Doubles as the proof that lets its owner bind an org to that tenant.</summary>
    public Guid? TenantId { get; set; }
    public string? RawProfile { get; set; }                  // JSON blob
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = default!;
}
