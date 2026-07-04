using System.Net.Http.Headers;
using System.Text.Json;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class OAuthProviderService(IHttpClientFactory httpClientFactory, IConfiguration config)
    : IOAuthProviderService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string GetAuthorizationUrl(string provider, string state)
    {
        return provider.ToLower() switch
        {
            "google" => BuildGoogleUrl(state),
            "github" => BuildGitHubUrl(state),
            "facebook" => BuildFacebookUrl(state),
            _ => throw new NotSupportedException($"OAuth provider '{provider}' is not supported.")
        };
    }

    public Task<OAuthUserInfo> ExchangeCodeAsync(string provider, string code, CancellationToken ct = default)
    {
        return provider.ToLower() switch
        {
            "google" => ExchangeGoogleAsync(code, ct),
            "github" => ExchangeGitHubAsync(code, ct),
            "facebook" => ExchangeFacebookAsync(code, ct),
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
            TokenExpiresAt: DateTime.UtcNow.AddSeconds(expiresIn)
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

        // GitHub may not expose email in /user — fetch from /user/emails
        var email = profile.TryGetProperty("email", out var em) && em.ValueKind != JsonValueKind.Null
            ? em.GetString()
            : await GetGitHubPrimaryEmailAsync(http, ct);

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
            TokenExpiresAt: null
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
            TokenExpiresAt: DateTime.UtcNow.AddSeconds(expiresIn)
        );
    }

    public Task UnlinkAsync(Guid userId, string provider, CancellationToken ct = default)
        => Task.CompletedTask;  // DB-only operation; actual delete is in AuthService via IOAuthAccountRepository

    private static async Task<string?> GetGitHubPrimaryEmailAsync(HttpClient http, CancellationToken ct)
    {
        var resp = await http.GetAsync("https://api.github.com/user/emails", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        var emails = JsonSerializer.Deserialize<JsonElement[]>(json, JsonOpts);
        if (emails is null) return null;
        var primary = emails.FirstOrDefault(e =>
            e.TryGetProperty("primary", out var p) && p.GetBoolean() &&
            e.TryGetProperty("verified", out var v) && v.GetBoolean());
        return primary.ValueKind != JsonValueKind.Undefined
            ? primary.GetProperty("email").GetString()
            : null;
    }
}
