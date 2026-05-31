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
            _ => throw new NotSupportedException($"OAuth provider '{provider}' is not supported.")
        };
    }

    public Task<OAuthUserInfo> ExchangeCodeAsync(string provider, string code, CancellationToken ct = default)
    {
        return provider.ToLower() switch
        {
            "google" => ExchangeGoogleAsync(code, ct),
            "github" => ExchangeGitHubAsync(code, ct),
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
