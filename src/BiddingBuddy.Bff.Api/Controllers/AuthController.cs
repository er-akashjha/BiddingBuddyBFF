using System.Security.Claims;
using System.Text.RegularExpressions;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
    IOAuthProviderService oauthProvider,
    IConfiguration config,
    IWebHostEnvironment env) : ControllerBase
{
    // Order here is the canonical display order for GET /api/auth/providers.
    private static readonly string[] SupportedProviders = ["google", "facebook", "github"];

    /// <summary>A supported provider is enabled unless <c>OAuth:{Provider}:Enabled</c> is
    /// explicitly false (config keys are case-insensitive, so the lowercase name works).</summary>
    private bool IsProviderEnabled(string provider)
        => config.GetValue<bool?>($"OAuth:{provider}:Enabled") ?? true;

    /// <summary>
    /// Begin a password signup. Sends a 6-digit verification code to the email and
    /// returns 202 — the account is created only after the code is confirmed via
    /// <c>POST /api/auth/verify-email</c>.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegistrationPendingDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
    {
        try
        {
            var result = await authService.StartRegistrationAsync(dto, ct);
            return StatusCode(StatusCodes.Status202Accepted, result);
        }
        catch (ArgumentException ex)
        {
            // Covers password-too-short and INVITE_INVALID.
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_EXISTS")
        {
            return Conflict(new { error = "Email already registered." });
        }
    }

    /// <summary>Confirm a signup's 6-digit code → creates the account and returns tokens.</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.VerifyEmailAsync(dto, ct);
            return Ok(tokens);
        }
        catch (InvalidOperationException ex) when (ex.Message == "TOO_MANY_ATTEMPTS")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_EXISTS")
        {
            return Conflict(new { error = "Email already registered." });
        }
        catch (InvalidOperationException ex)   // CODE_INVALID (wrong/expired/unknown)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)            // INVITE_INVALID re-checked at consume time
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Re-send the verification code for a pending signup. Always 204 (no enumeration).</summary>
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto dto, CancellationToken ct)
    {
        await authService.ResendVerificationAsync(dto.Email, ct);
        return NoContent();
    }

    /// <summary>
    /// Request a 6-digit password-reset code by email. Always 200 with the same shape
    /// regardless of whether the email maps to a resettable account (no enumeration).
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(PasswordResetRequestedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto, CancellationToken ct)
    {
        var result = await authService.RequestPasswordResetAsync(dto.Email, ct);
        return Ok(result);
    }

    /// <summary>Reset the password using the emailed 6-digit code. Revokes all existing sessions.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        try
        {
            await authService.ResetPasswordAsync(dto, ct);
            return Ok(new { status = "password_reset" });
        }
        catch (InvalidOperationException ex) when (ex.Message == "TOO_MANY_ATTEMPTS")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)   // CODE_INVALID (wrong/expired/unknown)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)            // password too short
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Login with email and password. Returns access + refresh tokens.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginWithPasswordDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.LoginWithPasswordAsync(dto, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }
    }

    /// <summary>
    /// OAuth providers currently enabled for sign-in — drives which social buttons the
    /// SPA renders on login/signup. Anonymous by design (the pages are public).
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult ListEnabledProviders()
        => Ok(new { providers = SupportedProviders.Where(IsProviderEnabled).ToArray() });

    /// <summary>
    /// Redirect the browser to the OAuth provider consent page (Google, Facebook or GitHub).
    /// Native apps additionally pass <c>client=mobile</c>, a PKCE S256 <c>code_challenge</c>
    /// and an allowlisted <c>redirect_uri</c> — the callback then bounces a one-time code
    /// back into the app instead of redirecting tokens to the SPA.
    /// </summary>
    [HttpGet("oauth/{provider}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult InitiateOAuth(
        string provider,
        [FromQuery] string returnUrl = "/",
        [FromQuery] string? client = null,
        [FromQuery(Name = "code_challenge")] string? codeChallenge = null,
        [FromQuery(Name = "redirect_uri")] string? redirectUri = null)
    {
        provider = provider.ToLower();
        // Disabled providers are rejected here too — hiding the button isn't the gate.
        if (!SupportedProviders.Contains(provider) || !IsProviderEnabled(provider))
            return BadRequest(new { error = $"Provider '{provider}' is not supported." });

        OAuthStateData stateData;
        if (client == "mobile")
        {
            if (codeChallenge is null || !PkceChallengeRe.IsMatch(codeChallenge))
                return BadRequest(new { error = "A valid S256 code_challenge is required for mobile sign-in." });
            if (redirectUri is null || !IsMobileRedirectAllowed(redirectUri))
                return BadRequest(new { error = "redirect_uri is not on the mobile allowlist." });
            stateData = new OAuthStateData(returnUrl, "mobile", codeChallenge, redirectUri);
        }
        else
        {
            stateData = new OAuthStateData(returnUrl);
        }

        var state = tokenService.GenerateStateToken(stateData);
        var authUrl = oauthProvider.GetAuthorizationUrl(provider, state);
        return Redirect(authUrl);
    }

    // RFC 7636: base64url alphabet, 43–128 chars.
    private static readonly Regex PkceChallengeRe = new("^[A-Za-z0-9_-]{43,128}$", RegexOptions.Compiled);

    /// <summary>
    /// A mobile redirect must start with an allowlisted prefix (<c>OAuth:Mobile:RedirectAllowlist</c>).
    /// Development also accepts Expo Go / dev-client redirects (exp:// exps://) so the flow is
    /// testable without a store build.
    /// </summary>
    private bool IsMobileRedirectAllowed(string redirectUri)
    {
        var allowlist = config.GetSection("OAuth:Mobile:RedirectAllowlist").Get<string[]>()
                        ?? ["tendersagent://auth", "biddingbuddymobile://auth"];
        if (allowlist.Any(p => redirectUri.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;
        return env.IsDevelopment() &&
               (redirectUri.StartsWith("exp://") || redirectUri.StartsWith("exps://"));
    }

    /// <summary>OAuth callback — exchanges code for tokens and redirects to the frontend.</summary>
    [HttpGet("oauth/{provider}/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> OAuthCallback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        provider = provider.ToLower();
        if (!SupportedProviders.Contains(provider) || !IsProviderEnabled(provider))
            return BadRequest(new { error = "Unsupported provider." });

        if (!tokenService.TryValidateStateToken(state, out var stateData))
            return BadRequest(new { error = "Invalid or expired state token." });

        // Mobile flows (client=mobile pinned in the signed state at initiation) get a
        // one-time exchange code bounced into the app — never tokens in the URL.
        if (stateData.Client == "mobile" && stateData.RedirectUri is not null)
            return await CompleteMobileOAuthAsync(provider, code, stateData, ct);

        try
        {
            var tokens = await authService.HandleOAuthCallbackAsync(provider, code, ct);

            var frontendBase = config["Frontend:BaseUrl"] ?? "http://localhost:3000";
            var callbackPath = config["Frontend:AuthCallbackPath"] ?? "/auth/callback";
            // is_new is a cosmetic hint for the SPA (welcome copy) — onboarding routing
            // is gated on "authenticated but no org", not on this flag.
            var redirectUrl = $"{frontendBase}{callbackPath}" +
                              $"?access_token={Uri.EscapeDataString(tokens.AccessToken)}" +
                              $"&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}" +
                              $"&expires_in={tokens.ExpiresIn}" +
                              $"&is_new={(tokens.IsNewUser ? "1" : "0")}" +
                              $"&return_url={Uri.EscapeDataString(stateData.ReturnUrl)}";

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            var frontendBase = config["Frontend:BaseUrl"] ?? "http://localhost:3000";
            return Redirect($"{frontendBase}/auth/error?message={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>
    /// Mobile completion: mint a 60-second single-use exchange code and bounce back into
    /// the app via its allowlisted redirect. The app redeems the code (with its PKCE
    /// verifier) at <c>POST /api/auth/oauth/exchange</c>.
    /// </summary>
    private async Task<IActionResult> CompleteMobileOAuthAsync(
        string provider, string code, OAuthStateData state, CancellationToken ct)
    {
        var sep = state.RedirectUri!.Contains('?') ? '&' : '?';
        try
        {
            var result = await authService.HandleOAuthCallbackForMobileAsync(
                provider, code, state.CodeChallenge!, ct);
            return Redirect(
                $"{state.RedirectUri}{sep}code={Uri.EscapeDataString(result.Code)}&is_new={(result.IsNewUser ? "1" : "0")}");
        }
        catch (Exception ex)
        {
            return Redirect($"{state.RedirectUri}{sep}error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>Redeem a one-time mobile OAuth code + PKCE verifier for a normal token pair.</summary>
    [HttpPost("oauth/exchange")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExchangeOAuthCode([FromBody] OAuthExchangeRequestDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.ExchangeOAuthCodeAsync(dto.Code, dto.CodeVerifier, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid or expired code." });
        }
    }

    /// <summary>
    /// Native Sign in with Apple (iOS). The app posts the ASAuthorization identityToken
    /// (+ the full name, only present on first authorization). Returns the standard token pair.
    /// </summary>
    [HttpPost("apple")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Apple([FromBody] AppleSignInDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.SignInWithAppleAsync(dto, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Apple sign-in could not be verified." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "APPLE_NO_EMAIL")
        {
            return BadRequest(new { error = "Apple did not share an email. Please sign in with Google or email instead." });
        }
        catch (InvalidOperationException)   // Apple not configured
        {
            return BadRequest(new { error = "Apple sign-in is not available." });
        }
    }

    /// <summary>
    /// Permanently delete the current user's account (App Store 5.1.1(v) / Play policy).
    /// Password users re-enter their password; OAuth/Apple-only users pass confirm=true.
    /// Anonymizes + deactivates the account and revokes all sessions and devices.
    /// </summary>
    [HttpPost("delete-account")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto, CancellationToken ct)
    {
        try
        {
            await authService.DeleteAccountAsync(CurrentUserId, dto, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Password is incorrect." });
        }
        catch (ArgumentException ex) when (ex.Message == "CONFIRM_REQUIRED")
        {
            return BadRequest(new { error = "Confirmation is required to delete your account." });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SOLE_OWNER:"))
        {
            var org = ex.Message["SOLE_OWNER:".Length..];
            return Conflict(new
            {
                error = $"You are the only owner of \"{org}\" and it has other members. " +
                        "Transfer ownership or remove the members before deleting your account.",
            });
        }
    }

    /// <summary>Rotate refresh token — returns a new access token + refresh token pair.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.RefreshAsync(request.RefreshToken, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>Revoke the current refresh token (logout).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Get the current user's profile, organizations and connected OAuth providers.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(user);
    }

    /// <summary>Update the current user's name or phone number.</summary>
    [HttpPatch("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto, CancellationToken ct)
    {
        var user = await authService.UpdateProfileAsync(CurrentUserId, dto, ct);
        return Ok(user);
    }

    /// <summary>List OAuth providers (google, github) linked to the current user.</summary>
    [HttpGet("me/providers")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(new { providers = user.ConnectedProviders });
    }

    /// <summary>Unlink an OAuth provider from the current user (must have at least one provider remaining).</summary>
    [HttpDelete("me/providers/{provider}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnlinkProvider(string provider, CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        if (user.ConnectedProviders.Count <= 1)
            return BadRequest(new { error = "Cannot unlink the only connected provider." });

        await authService.UnlinkProviderAsync(CurrentUserId, provider.ToLower(), ct);
        return NoContent();
    }

    private Guid CurrentUserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? throw new UnauthorizedAccessException("User ID not found in token.");
            return Guid.Parse(sub);
        }
    }
}
