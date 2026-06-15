using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class AuthService(
    IUserRepository userRepo,
    IOAuthAccountRepository oauthRepo,
    IRefreshTokenRepository refreshRepo,
    IOrganizationRepository orgRepo,
    IOAuthProviderService oauthProvider,
    TokenService tokenService,
    INotificationPublisher notifications,
    BffDbContext db,
    ILogger<AuthService> log) : IAuthService
{
    public async Task<TokenResponseDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        if (dto.Password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        if (await userRepo.FindByEmailAsync(dto.Email, ct) is not null)
            throw new InvalidOperationException("EMAIL_EXISTS");

        // If an invite token is supplied, the new user JOINS the inviting org
        // (with the role recorded on the invite) and we DO NOT auto-create a
        // personal org. The token must hash to a pending, unexpired invite whose
        // email matches the registration email — we don't want a leaked token to
        // grant access to a different email.
        OrganizationInvite? invite = null;
        if (!string.IsNullOrWhiteSpace(dto.InviteToken))
        {
            invite = await ValidateInviteAsync(dto.InviteToken!, dto.Email, ct);
        }

        var user = await userRepo.CreateAsync(new User
        {
            Email = dto.Email,
            Name = dto.Name,
            Phone = dto.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            LastLoginAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, ct);

        string orgNameForWelcome;
        if (invite is not null)
        {
            // Join the inviting org with the invite's role; skip personal-org creation.
            await orgRepo.AddMemberAsync(new OrgMember
            {
                OrgId      = invite.OrgId,
                UserId     = user.Id,
                Role       = invite.Role,
                Department = invite.Department,
                Status     = "active",
                InvitedBy  = invite.InvitedBy,
                JoinedAt   = DateTime.UtcNow,
                CreatedAt  = DateTime.UtcNow,
            }, ct);

            invite.AcceptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            orgNameForWelcome = await db.Organizations
                .Where(o => o.Id == invite.OrgId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        }
        else
        {
            var org = await orgRepo.CreateAsync(new Organization
            {
                OwnedBy = user.Id,
                Name = dto.OrgName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }, ct);

            await orgRepo.AddMemberAsync(new OrgMember
            {
                OrgId = org.Id,
                UserId = user.Id,
                Role = "owner",
                Status = "active",
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            }, ct);

            orgNameForWelcome = org.Name;
        }

        var tokens = await IssueTokensAsync(user, ct);
        await SendWelcomeAsync(user, orgNameForWelcome, ct);
        return tokens;
    }

    /// <summary>
    /// Resolves a raw invite token to a usable invite row, or throws
    /// <see cref="ArgumentException"/> with a non-revealing message on any failure
    /// (unknown token / expired / already accepted / email mismatch).
    /// </summary>
    private async Task<OrganizationInvite> ValidateInviteAsync(string rawToken, string email, CancellationToken ct)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var invite = await db.OrganizationInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        if (invite is null
            || invite.AcceptedAt is not null
            || invite.ExpiresAt < DateTime.UtcNow
            || !string.Equals(invite.Email, email.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("INVITE_INVALID");
        }
        return invite;
    }

    public async Task<TokenResponseDto> LoginWithPasswordAsync(LoginWithPasswordDto dto, CancellationToken ct = default)
    {
        var user = await userRepo.FindByEmailAsync(dto.Email, ct);
        if (user is null || user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("INVALID_CREDENTIALS");

        user.LastLoginAt = DateTime.UtcNow;
        await userRepo.UpdateAsync(user, ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<TokenResponseDto> HandleOAuthCallbackAsync(
        string provider, string code, CancellationToken ct = default)
    {
        // 1. Exchange code for user info from provider
        var info = await oauthProvider.ExchangeCodeAsync(provider, code, ct);

        // 2. Find or create the linked OAuthAccount
        var oauthAccount = await oauthRepo.FindAsync(provider, info.ProviderUserId, ct);

        User user;
        var isNewUser = false;
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
            // New provider identity — look up by email; create user if first time on this system
            var existing = await userRepo.FindByEmailAsync(info.Email, ct);
            if (existing is null)
            {
                user = await userRepo.CreateAsync(new User
                {
                    Email = info.Email,
                    Name = info.Name,
                    AvatarUrl = info.AvatarUrl,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }, ct);
                isNewUser = true;
            }
            else
            {
                user = existing;
            }

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

        var tokens = await IssueTokensAsync(user, ct);

        // First-time OAuth signup → fire WELCOME. The user has no org yet (orgs are
        // created/joined later), so OrganizationName is left blank in the payload.
        if (isNewUser)
            await SendWelcomeAsync(user, organizationName: null, ct);

        return tokens;
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
            result.Add(new UserOrgDto(org.Id, org.Name, org.Slug, role, org.LogoUrl, org.IsActive, org.PrimaryCategory));
        }
        return result;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Fire-and-forget WELCOME notification on first signup. Notification failures
    /// must not break the signup response — the user is already created and tokens
    /// are already minted by the time we're called. We log and move on; the
    /// processor's pending-grace poller handles any RabbitMQ hiccup.
    /// </summary>
    private async Task SendWelcomeAsync(User user, string? organizationName, CancellationToken ct)
    {
        try
        {
            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "WELCOME",
                UserId:       user.Id,
                Payload:      new Dictionary<string, object>
                {
                    ["FirstName"]        = FirstName(user.Name),
                    ["OrganizationName"] = organizationName ?? string.Empty,
                },
                Recipients: new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, user.Email),
                    new NotificationRecipientDto(NotificationChannel.InApp, user.Id.ToString()),
                }), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WELCOME notification failed for user {UserId}; signup itself succeeded.", user.Id);
        }
    }

    private static string FirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var space = fullName.IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName[..space].Trim();
    }
}
