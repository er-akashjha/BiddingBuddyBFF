using System.Security.Cryptography;
using System.Text;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class AuthService(
    IUserRepository userRepo,
    IOAuthAccountRepository oauthRepo,
    IRefreshTokenRepository refreshRepo,
    IOrganizationRepository orgRepo,
    IOAuthProviderService oauthProvider,
    TokenService tokenService) : IAuthService
{
    public async Task<TokenResponseDto> HandleOAuthCallbackAsync(
        string provider, string code, CancellationToken ct = default)
    {
        // 1. Exchange code for user info from provider
        var info = await oauthProvider.ExchangeCodeAsync(provider, code, ct);

        // 2. Find or create the linked OAuthAccount
        var oauthAccount = await oauthRepo.FindAsync(provider, info.ProviderUserId, ct);

        User user;
        if (oauthAccount is not null)
        {
            // Known provider identity — load user, update tokens
            user = (await userRepo.FindByIdAsync(oauthAccount.UserId, ct))!;
            oauthAccount.AccessToken = info.AccessToken;
            oauthAccount.RefreshToken = info.ProviderRefreshToken;
            oauthAccount.TokenExpiresAt = info.TokenExpiresAt;
            await oauthRepo.UpdateAsync(oauthAccount, ct);
        }
        else
        {
            // New provider identity — look up by email or create user
            user = await userRepo.FindByEmailAsync(info.Email, ct)
                ?? await userRepo.CreateAsync(new User
                {
                    Email = info.Email,
                    Name = info.Name,
                    AvatarUrl = info.AvatarUrl,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }, ct);

            // Link this provider to the user
            await oauthRepo.CreateAsync(new OAuthAccount
            {
                UserId = user.Id,
                Provider = provider,
                ProviderUserId = info.ProviderUserId,
                Email = info.Email,
                AccessToken = info.AccessToken,
                RefreshToken = info.ProviderRefreshToken,
                TokenExpiresAt = info.TokenExpiresAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }, ct);
        }

        // 3. Update last login
        user.LastLoginAt = DateTime.UtcNow;
        if (user.AvatarUrl is null && info.AvatarUrl is not null)
            user.AvatarUrl = info.AvatarUrl;
        await userRepo.UpdateAsync(user, ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<TokenResponseDto> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        var stored = await refreshRepo.FindByHashAsync(hash, ct)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (!stored.IsActive)
            throw new UnauthorizedAccessException("Refresh token has expired or been revoked.");

        var user = await userRepo.FindByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        // Rotate: revoke old, issue new
        await refreshRepo.RevokeAsync(hash, ct);
        return await IssueTokensAsync(user, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = HashToken(refreshToken);
        await refreshRepo.RevokeAsync(hash, ct);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        var orgs = await orgRepo.FindByUserIdAsync(userId, ct);
        var oauthAccounts = await oauthRepo.FindByUserIdAsync(userId, ct);

        var orgDtos = await BuildOrgDtosAsync(orgs, userId, ct);
        var providers = oauthAccounts.Select(a => a.Provider).ToList();

        return new UserDto(user.Id, user.Email, user.Name, user.AvatarUrl, user.Phone,
            orgDtos, providers);
    }

    public async Task UnlinkProviderAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        var accounts = await oauthRepo.FindByUserIdAsync(userId, ct);
        if (accounts.Count <= 1)
            throw new InvalidOperationException("Cannot unlink the only connected provider.");
        await oauthRepo.DeleteAsync(userId, provider, ct);
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (dto.Name is not null) user.Name = dto.Name;
        if (dto.Phone is not null) user.Phone = dto.Phone;
        user.UpdatedAt = DateTime.UtcNow;
        await userRepo.UpdateAsync(user, ct);

        return await GetCurrentUserAsync(userId, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<TokenResponseDto> IssueTokensAsync(User user, CancellationToken ct)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var (rawRefresh, hash) = tokenService.GenerateRefreshToken();

        await refreshRepo.CreateAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.Add(tokenService.RefreshTokenLifetime),
            CreatedAt = DateTime.UtcNow,
        }, ct);

        return new TokenResponseDto(accessToken, rawRefresh, ExpiresIn: 15 * 60);
    }

    private async Task<IReadOnlyList<UserOrgDto>> BuildOrgDtosAsync(
        IReadOnlyList<Organization> orgs, Guid userId, CancellationToken ct)
    {
        var result = new List<UserOrgDto>();
        foreach (var org in orgs)
        {
            var role = await orgRepo.GetUserRoleAsync(org.Id, userId, ct) ?? "viewer";
            result.Add(new UserOrgDto(org.Id, org.Name, org.Slug, role, org.LogoUrl, org.IsActive));
        }
        return result;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
