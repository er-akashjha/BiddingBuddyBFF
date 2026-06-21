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
    public async Task<RegistrationPendingDto> StartRegistrationAsync(RegisterDto dto, CancellationToken ct = default)
    {
        if (dto.Password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        var email = NormalizeEmail(dto.Email);

        // The account is NOT created here. We stash the (hashed) credentials in
        // pending_registrations and email a 6-digit OTP; the real user/org are only
        // created in VerifyEmailAsync once the code is confirmed.
        if (await userRepo.FindByEmailAsync(email, ct) is not null)
            throw new InvalidOperationException("EMAIL_EXISTS");

        // Validate any invite token now for fast feedback. It is re-validated AND
        // consumed at verify time (CreateVerifiedAccountAsync), so a token that
        // expires between now and verification is still caught.
        if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            await ValidateInviteAsync(dto.InviteToken!, email, ct);

        var (rawCode, codeHash) = GenerateOtp();

        // Supersede any prior active pending registration for this email so the
        // partial unique index (one active row per email) doesn't block the insert.
        // Committed in its own round-trip BEFORE the insert so the old row is already
        // consumed when the new one lands (mirrors the org-invite supersede).
        var prior = await db.PendingRegistrations
            .Where(p => p.Email == email && p.ConsumedAt == null)
            .ToListAsync(ct);
        if (prior.Count > 0)
        {
            foreach (var p in prior) { p.ConsumedAt = DateTime.UtcNow; p.UpdatedAt = DateTime.UtcNow; }
            await db.SaveChangesAsync(ct);
        }

        var pending = new PendingRegistration
        {
            Id           = Guid.NewGuid(),
            Email        = email,
            Name         = dto.Name,
            // Hash the password NOW so plaintext never lands in the table.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            // OrgName is ignored when joining via an invite (mirrors the old flow).
            OrgName      = string.IsNullOrWhiteSpace(dto.InviteToken) ? dto.OrgName : null,
            Phone        = dto.Phone,
            InviteToken  = dto.InviteToken,
            CodeHash     = codeHash,
            ExpiresAt    = DateTime.UtcNow.Add(OtpLifetime),
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
        };
        db.PendingRegistrations.Add(pending);
        await db.SaveChangesAsync(ct);

        await SendVerificationEmailAsync(pending, rawCode, ct);

        return new RegistrationPendingDto(
            Status:           "verification_pending",
            Email:            email,
            ExpiresInSeconds: (int)OtpLifetime.TotalSeconds,
            // Dev-only: surface the code so the flow is testable without a mailbox.
            DevCode:          IsDevelopment ? rawCode : null);
    }

    public async Task<TokenResponseDto> VerifyEmailAsync(VerifyEmailDto dto, CancellationToken ct = default)
    {
        var email = NormalizeEmail(dto.Email);

        var pending = await db.PendingRegistrations
            .Where(p => p.Email == email && p.ConsumedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (pending is null || pending.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("CODE_INVALID");

        if (pending.AttemptCount >= MaxOtpAttempts)
            throw new InvalidOperationException("TOO_MANY_ATTEMPTS");

        var providedHash = HashOtp((dto.Code ?? string.Empty).Trim());
        var matches = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(providedHash),
            Convert.FromHexString(pending.CodeHash));

        if (!matches)
        {
            pending.AttemptCount++;
            pending.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("CODE_INVALID");
        }

        // Correct code → materialise the real account, then burn the pending row.
        var tokens = await CreateVerifiedAccountAsync(pending, ct);
        pending.ConsumedAt = DateTime.UtcNow;
        pending.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return tokens;
    }

    public async Task ResendVerificationAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);

        var pending = await db.PendingRegistrations
            .Where(p => p.Email == email && p.ConsumedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // No active pending registration, or the resend cap is hit → do nothing and
        // return normally. The caller always replies 204, so an attacker can't probe
        // which emails have a signup in flight.
        if (pending is null || pending.ResendCount >= MaxResends)
            return;

        var (rawCode, codeHash) = GenerateOtp();
        pending.CodeHash     = codeHash;
        pending.AttemptCount = 0;                       // fresh code → fresh attempt budget
        pending.ResendCount++;
        pending.ExpiresAt    = DateTime.UtcNow.Add(OtpLifetime);
        pending.UpdatedAt    = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await SendVerificationEmailAsync(pending, rawCode, ct);
    }

    /// <summary>
    /// Turns a verified <see cref="PendingRegistration"/> into a real account:
    /// creates the <see cref="User"/> (with the already-hashed password), joins the
    /// inviting org or auto-creates a personal one, issues tokens, and fires WELCOME.
    /// This is the body that used to run inline in the immediate-create register.
    /// </summary>
    private async Task<TokenResponseDto> CreateVerifiedAccountAsync(PendingRegistration reg, CancellationToken ct)
    {
        // Re-check the email is still free — it could have been claimed (e.g. via
        // OAuth) between StartRegistration and verification.
        if (await userRepo.FindByEmailAsync(reg.Email, ct) is not null)
            throw new InvalidOperationException("EMAIL_EXISTS");

        // Re-validate the invite at consume time (it may have expired/been accepted
        // since signup). A leaked token still can't target a different email.
        OrganizationInvite? invite = null;
        if (!string.IsNullOrWhiteSpace(reg.InviteToken))
            invite = await ValidateInviteAsync(reg.InviteToken!, reg.Email, ct);

        var user = await userRepo.CreateAsync(new User
        {
            Email = reg.Email,
            Name = reg.Name,
            Phone = reg.Phone,
            PasswordHash = reg.PasswordHash,    // already BCrypt-hashed at request time
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
                Name = reg.OrgName ?? string.Empty,
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

    public async Task<PasswordResetRequestedDto> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);

        var user = await userRepo.FindByEmailAsync(email, ct);

        // Only accounts that have a password can reset one. For an unknown email or an
        // OAuth-only user we return the SAME response and send nothing — the caller can't
        // tell whether an account exists (no enumeration).
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return new PasswordResetRequestedDto("reset_code_sent", (int)OtpLifetime.TotalSeconds);

        var (rawCode, codeHash) = GenerateOtp();

        // Supersede any prior active reset code for this user (partial unique guard).
        var prior = await db.PasswordResetCodes
            .Where(p => p.UserId == user.Id && p.ConsumedAt == null)
            .ToListAsync(ct);
        if (prior.Count > 0)
        {
            foreach (var p in prior) { p.ConsumedAt = DateTime.UtcNow; p.UpdatedAt = DateTime.UtcNow; }
            await db.SaveChangesAsync(ct);
        }

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            CodeHash  = codeHash,
            ExpiresAt = DateTime.UtcNow.Add(OtpLifetime),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await SendPasswordResetEmailAsync(user, rawCode, ct);

        return new PasswordResetRequestedDto(
            "reset_code_sent",
            (int)OtpLifetime.TotalSeconds,
            IsDevelopment ? rawCode : null);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default)
    {
        if ((dto.NewPassword ?? string.Empty).Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        var email = NormalizeEmail(dto.Email);
        var user = await userRepo.FindByEmailAsync(email, ct);
        if (user is null)
            throw new InvalidOperationException("CODE_INVALID");

        var reset = await db.PasswordResetCodes
            .Where(p => p.UserId == user.Id && p.ConsumedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (reset is null || reset.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("CODE_INVALID");

        if (reset.AttemptCount >= MaxOtpAttempts)
            throw new InvalidOperationException("TOO_MANY_ATTEMPTS");

        var providedHash = HashOtp((dto.Code ?? string.Empty).Trim());
        var matches = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(providedHash),
            Convert.FromHexString(reset.CodeHash));

        if (!matches)
        {
            reset.AttemptCount++;
            reset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("CODE_INVALID");
        }

        // Correct code → set the new password, burn the code, and revoke every existing
        // session so an old/stolen refresh token can't outlive the reset.
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword!);
        user.UpdatedAt = DateTime.UtcNow;
        await userRepo.UpdateAsync(user, ct);

        reset.ConsumedAt = DateTime.UtcNow;
        reset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTime?)DateTime.UtcNow), ct);
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

    // ── Email-verification (OTP) helpers ───────────────────────────────────────

    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(15);
    private const int MaxOtpAttempts = 5;   // wrong guesses before the code is dead
    private const int MaxResends     = 5;   // OTP re-issues per pending registration

    // Dev-only escape hatch so the OTP flow is testable without a working mailbox.
    // Defaults to false anywhere ASPNETCORE_ENVIRONMENT isn't "Development" (incl. prod).
    private static bool IsDevelopment =>
        string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Returns (rawCode, sha256-hex hash). 6 digits, uniform, crypto-random.</summary>
    private static (string Raw, string Hash) GenerateOtp()
    {
        var raw = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return (raw, HashOtp(raw));
    }

    private static string HashOtp(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Best-effort EMAIL_VERIFICATION send (Email channel only — no user/in-app inbox
    /// exists yet). Failures are logged, not thrown: the pending row still exists, the
    /// code can be resent, and the processor's pending-grace poller re-drives any
    /// RabbitMQ hiccup.
    /// </summary>
    private async Task SendVerificationEmailAsync(PendingRegistration reg, string rawCode, CancellationToken ct)
    {
        try
        {
            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "EMAIL_VERIFICATION",
                UserId:       null,
                Payload:      new Dictionary<string, object>
                {
                    ["FirstName"]     = FirstName(reg.Name),
                    ["Code"]          = rawCode,
                    ["ExpiryMinutes"] = (int)OtpLifetime.TotalMinutes,
                },
                Recipients: new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, reg.Email),
                }), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "EMAIL_VERIFICATION send failed for {Email}; pending row kept, code can be resent.", reg.Email);
        }
    }

    /// <summary>
    /// Best-effort PASSWORD_RESET send (Email channel). Failures are logged, not thrown:
    /// the reset-code row is kept and the user can request another, and the processor's
    /// pending-grace poller re-drives any RabbitMQ hiccup.
    /// </summary>
    private async Task SendPasswordResetEmailAsync(User user, string rawCode, CancellationToken ct)
    {
        try
        {
            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "PASSWORD_RESET",
                UserId:       user.Id,
                Payload:      new Dictionary<string, object>
                {
                    ["FirstName"]     = FirstName(user.Name),
                    ["Code"]          = rawCode,
                    ["ExpiryMinutes"] = (int)OtpLifetime.TotalMinutes,
                },
                Recipients: new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, user.Email),
                }), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PASSWORD_RESET send failed for user {UserId}; the code row is kept and can be re-requested.", user.Id);
        }
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
