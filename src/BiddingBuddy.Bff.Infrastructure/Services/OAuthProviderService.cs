using System.Net.Http.Headers;
using System.Text.Json;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class OAuthProviderService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IMicrosoftTokenVerifier microsoftVerifier)
    : IOAuthProviderService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string GetAuthorizationUrl(string provider, string state, string? nonce = null, string? loginHint = null)
    {
        return provider.ToLower() switch
        {
            "google" => BuildGoogleUrl(state),
            "github" => BuildGitHubUrl(state),
            "facebook" => BuildFacebookUrl(state),
            "microsoft" => BuildMicrosoftUrl(state, nonce, loginHint),
            _ => throw new NotSupportedException($"OAuth provider '{provider}' is not supported.")
        };
    }

    public Task<OAuthUserInfo> ExchangeCodeAsync(
        string provider, string code, string? expectedNonce = null, CancellationToken ct = default)
    {
        return provider.ToLower() switch
        {
            "google" => ExchangeGoogleAsync(code, ct),
            "github" => ExchangeGitHubAsync(code, ct),
            "facebook" => ExchangeFacebookAsync(code, ct),
            "microsoft" => ExchangeMicrosoftAsync(code, expectedNonce, ct),
            _ => throw new NotSupportedException($"OAuth provider '{provider}' is not supported.")
        };
    }

    // ── Google ────────────────────────────────────────────────────────────────

    private string BuildGoogleUrl(string state)
    {
        var clientId = config["OAuth:Google:ClientId"]!;
        var redirectUri = config["OAuth:Google:RedirectUri"]!;
        var scope = Uri.EscapeDataString("openid email profile");
        var encodedState = Uri.EscapeDataString(state);
        return $"https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope={scope}" +
               $"&state={encodedState}" +
               $"&access_type=offline" +
               $"&prompt=consent";
    }

    private async Task<OAuthUserInfo> ExchangeGoogleAsync(string code, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();

        // Exchange code for tokens
        var tokenResp = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = config["OAuth:Google:ClientId"]!,
                ["client_secret"] = config["OAuth:Google:ClientSecret"]!,
                ["redirect_uri"] = config["OAuth:Google:RedirectUri"]!,
                ["grant_type"] = "authorization_code",
            }), ct);

        tokenResp.EnsureSuccessStatusCode();
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson, JsonOpts);
        var accessToken = tokenData.GetProperty("access_token").GetString()!;
        var refreshToken = tokenData.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = tokenData.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        // Fetch user profile
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileResp = await http.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo", ct);
        profileResp.EnsureSuccessStatusCode();
        var profileJson = await profileResp.Content.ReadAsStringAsync(ct);
        var profile = JsonSerializer.Deserialize<JsonElement>(profileJson, JsonOpts);

        return new OAuthUserInfo(
            ProviderUserId: profile.GetProperty("id").GetString()!,
            Email: profile.GetProperty("email").GetString()!,
            Name: profile.TryGetProperty("name", out var name) ? name.GetString()! : "Unknown",
            AvatarUrl: profile.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
            AccessToken: accessToken,
            ProviderRefreshToken: refreshToken,
            TokenExpiresAt: DateTime.UtcNow.AddSeconds(expiresIn),
            // Google has always told us this; we simply never read it. A Google account can hold an
            // unverified address, and adopting an existing account on one is the takeover this guards.
            EmailVerified: profile.TryGetProperty("verified_email", out var ve)
                           && ve.ValueKind == JsonValueKind.True
        );
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    private string BuildGitHubUrl(string state)
    {
        var clientId = config["OAuth:GitHub:ClientId"]!;
        var redirectUri = config["OAuth:GitHub:RedirectUri"]!;
        var encodedState = Uri.EscapeDataString(state);
        return $"https://github.com/login/oauth/authorize" +
               $"?client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope=read:user+user:email" +
               $"&state={encodedState}";
    }

    private async Task<OAuthUserInfo> ExchangeGitHubAsync(string code, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BiddingBuddyBFF/1.0");

        // Exchange code for token
        var tokenResp = await http.PostAsync("https://github.com/login/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = config["OAuth:GitHub:ClientId"]!,
                ["client_secret"] = config["OAuth:GitHub:ClientSecret"]!,
                ["redirect_uri"] = config["OAuth:GitHub:RedirectUri"]!,
            }), ct);

        tokenResp.EnsureSuccessStatusCode();
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson, JsonOpts);
        var accessToken = tokenData.GetProperty("access_token").GetString()!;

        // Fetch user profile
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileResp = await http.GetAsync("https://api.github.com/user", ct);
        profileResp.EnsureSuccessStatusCode();
        var profileJson = await profileResp.Content.ReadAsStringAsync(ct);
        var profile = JsonSerializer.Deserialize<JsonElement>(profileJson, JsonOpts);

        // GitHub may not expose an email on /user, so /user/emails is the fallback it always was.
        // It is now also the evidence: /user/emails is the only surface that states whether an address
        // is verified, so we consult it even when the public profile already gave us one. The public
        // email is normally verified too, and marking it unverified would needlessly block linking.
        var verified = await GetGitHubVerifiedEmailsAsync(http, ct);

        var profileEmail = profile.TryGetProperty("email", out var em) && em.ValueKind != JsonValueKind.Null
            ? em.GetString()
            : null;
        var email = profileEmail ?? verified.FirstOrDefault();
        var emailVerified = email is not null
            && verified.Contains(email, StringComparer.OrdinalIgnoreCase);

        var name = profile.TryGetProperty("name", out var nm) && nm.ValueKind != JsonValueKind.Null
            ? nm.GetString()!
            : profile.TryGetProperty("login", out var lg) ? lg.GetString()! : "Unknown";

        return new OAuthUserInfo(
            ProviderUserId: profile.GetProperty("id").GetInt64().ToString(),
            Email: email ?? throw new InvalidOperationException("GitHub did not provide an email address."),
            Name: name,
            AvatarUrl: profile.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
            AccessToken: accessToken,
            ProviderRefreshToken: null,
            TokenExpiresAt: null,
            EmailVerified: emailVerified
        );
    }

    // ── Facebook ──────────────────────────────────────────────────────────────

    private string BuildFacebookUrl(string state)
    {
        var clientId = config["OAuth:Facebook:ClientId"]!;
        var redirectUri = config["OAuth:Facebook:RedirectUri"]!;
        var encodedState = Uri.EscapeDataString(state);
        return $"https://www.facebook.com/v19.0/dialog/oauth" +
               $"?client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope={Uri.EscapeDataString("email,public_profile")}" +
               $"&state={encodedState}";
    }

    private async Task<OAuthUserInfo> ExchangeFacebookAsync(string code, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();

        // Facebook exchanges the code via GET with query params (not a form POST)
        var tokenUrl = $"https://graph.facebook.com/v19.0/oauth/access_token" +
                       $"?client_id={config["OAuth:Facebook:ClientId"]}" +
                       $"&client_secret={config["OAuth:Facebook:ClientSecret"]}" +
                       $"&redirect_uri={Uri.EscapeDataString(config["OAuth:Facebook:RedirectUri"]!)}" +
                       $"&code={Uri.EscapeDataString(code)}";
        var tokenResp = await http.GetAsync(tokenUrl, ct);
        tokenResp.EnsureSuccessStatusCode();
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson, JsonOpts);
        var accessToken = tokenData.GetProperty("access_token").GetString()!;
        var expiresIn = tokenData.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        // Fetch user profile. Phone-only Facebook accounts have no email — the field
        // is absent from the response even when the email scope was granted.
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profileResp = await http.GetAsync(
            "https://graph.facebook.com/v19.0/me?fields=id,name,email,picture.width(200)", ct);
        profileResp.EnsureSuccessStatusCode();
        var profileJson = await profileResp.Content.ReadAsStringAsync(ct);
        var profile = JsonSerializer.Deserialize<JsonElement>(profileJson, JsonOpts);

        var email = profile.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String
            ? em.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException(
                "Your Facebook account has no email address. Please sign in with Google or email instead.");

        string? avatar = null;
        if (profile.TryGetProperty("picture", out var pic) &&
            pic.TryGetProperty("data", out var picData) &&
            picData.TryGetProperty("url", out var picUrl))
            avatar = picUrl.GetString();

        return new OAuthUserInfo(
            ProviderUserId: profile.GetProperty("id").GetString()!,
            Email: email!,
            Name: profile.TryGetProperty("name", out var nm) ? nm.GetString()! : "Unknown",
            AvatarUrl: avatar,
            AccessToken: accessToken,
            ProviderRefreshToken: null,   // Facebook issues no refresh token for this flow
            TokenExpiresAt: DateTime.UtcNow.AddSeconds(expiresIn),
            // Graph exposes no verification state for the email at all, so we cannot claim one. The
            // practical effect: a Facebook sign-in can create a new account, but can never silently
            // adopt an existing one that happens to share the address.
            EmailVerified: false
        );
    }

    // ── Microsoft (Entra ID) ──────────────────────────────────────────────────

    private const string DefaultMicrosoftAuthority = "https://login.microsoftonline.com/organizations";

    /// <summary>
    /// The <c>/organizations</c> authority is the "work or school accounts only" gate — Microsoft turns
    /// a personal Outlook/Hotmail/Xbox account away at its own sign-in page, before it ever reaches our
    /// callback. That is a product decision expressed as one path segment, so it is worth naming.
    /// </summary>
    private string MicrosoftAuthority =>
        (config["OAuth:Microsoft:Authority"] ?? DefaultMicrosoftAuthority).TrimEnd('/');

    private string BuildMicrosoftUrl(string state, string? nonce, string? loginHint)
    {
        var clientId = config["OAuth:Microsoft:ClientId"]!;
        var redirectUri = config["OAuth:Microsoft:RedirectUri"]!;
        // offline_access is what makes Entra return a refresh token; the rest are the standard OIDC
        // trio. All four are user-consentable, so a customer's staff can sign in without their IT
        // department having to grant tenant-wide admin consent first.
        var scope = Uri.EscapeDataString("openid email profile offline_access");

        var url = $"{MicrosoftAuthority}/oauth2/v2.0/authorize" +
                  $"?client_id={clientId}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&response_type=code" +
                  $"&response_mode=query" +
                  $"&scope={scope}" +
                  $"&state={Uri.EscapeDataString(state)}";

        if (!string.IsNullOrWhiteSpace(nonce))
            url += $"&nonce={Uri.EscapeDataString(nonce)}";
        // Cosmetic only — it pre-selects the account in Microsoft's picker. Identity comes from the
        // id_token, so a tampered hint changes what the user sees and nothing else.
        if (!string.IsNullOrWhiteSpace(loginHint))
            url += $"&login_hint={Uri.EscapeDataString(loginHint)}";

        return url;
    }

    /// <summary>
    /// Exchanges the code, then reads identity from the returned <c>id_token</c> rather than calling
    /// Microsoft Graph <c>/me</c>. The id_token is signature-verified, carries <c>tid</c> (which Graph's
    /// <c>/me</c> does not return without extra permissions), and costs no second network hop.
    /// </summary>
    private async Task<OAuthUserInfo> ExchangeMicrosoftAsync(
        string code, string? expectedNonce, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();

        var tokenResp = await http.PostAsync($"{MicrosoftAuthority}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = config["OAuth:Microsoft:ClientId"]!,
                ["client_secret"] = config["OAuth:Microsoft:ClientSecret"]!,
                ["redirect_uri"] = config["OAuth:Microsoft:RedirectUri"]!,
                ["grant_type"] = "authorization_code",
                ["scope"] = "openid email profile offline_access",
            }), ct);

        tokenResp.EnsureSuccessStatusCode();
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson, JsonOpts);

        var idToken = tokenData.TryGetProperty("id_token", out var it) ? it.GetString() : null;
        if (string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Microsoft returned no id_token.");

        var identity = await microsoftVerifier.VerifyAsync(idToken!, expectedNonce, ct);

        var accessToken = tokenData.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var refreshToken = tokenData.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = tokenData.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        return new OAuthUserInfo(
            ProviderUserId: identity.ObjectId,
            Email: identity.Email,
            Name: string.IsNullOrWhiteSpace(identity.Name)
                ? identity.Email.Split('@')[0]
                : identity.Name!,
            AvatarUrl: null,          // the photo lives behind Graph /me/photo — not worth a permission
            AccessToken: accessToken,
            ProviderRefreshToken: refreshToken,
            TokenExpiresAt: DateTime.UtcNow.AddSeconds(expiresIn),
            // Constant true, and it is the /organizations authority that earns it: the account belongs
            // to a directory whose custom domains Microsoft made someone prove by DNS.
            EmailVerified: true,
            TenantId: identity.TenantId
        );
    }

    public Task UnlinkAsync(Guid userId, string provider, CancellationToken ct = default)
        => Task.CompletedTask;  // DB-only operation; actual delete is in AuthService via IOAuthAccountRepository

    /// <summary>
    /// Every verified address on the account, primary first — so <c>.FirstOrDefault()</c> keeps the old
    /// "primary verified email" behaviour, while the full set lets the caller ask whether some *other*
    /// address (the public profile one) is verified too.
    /// </summary>
    /// <remarks>A failed call yields an empty set, not an exception: losing the verification signal
    /// should degrade linking to the safe answer, never break sign-in outright.</remarks>
    private static async Task<IReadOnlyList<string>> GetGitHubVerifiedEmailsAsync(
        HttpClient http, CancellationToken ct)
    {
        var resp = await http.GetAsync("https://api.github.com/user/emails", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync(ct);
        var emails = JsonSerializer.Deserialize<JsonElement[]>(json, JsonOpts);
        if (emails is null) return [];

        return emails
            .Where(e => e.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True)
            .OrderByDescending(e => e.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True)
            .Select(e => e.TryGetProperty("email", out var m) ? m.GetString() : null)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList();
    }
}
