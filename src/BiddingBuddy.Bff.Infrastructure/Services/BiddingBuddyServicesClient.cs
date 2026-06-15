using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Typed HTTP client for BiddingBuddyServices (internal MongoDB API).
/// Auth flow: POST /auth/token → Bearer JWT (1 h). Token is cached in-memory
/// and refreshed automatically when it expires.
/// Config: BiddingBuddyServices:BaseUrl | :Username | :Password
/// </summary>
public class BiddingBuddyServicesClient : IBiddingBuddyServicesClient
{
    private readonly HttpClient _http;
    private readonly string     _username;
    private readonly string     _password;
    private readonly ILogger<BiddingBuddyServicesClient> _log;

    // ── token cache ───────────────────────────────────────────────────────────
    private string?       _cachedToken;
    private DateTime      _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive  = true,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy         = JsonNamingPolicy.CamelCase,
        // Ignore unknown properties such as ai.embedding (large float array)
        UnknownTypeHandling          = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
    };

    public BiddingBuddyServicesClient(
        HttpClient   http,
        IConfiguration config,
        ILogger<BiddingBuddyServicesClient> log)
    {
        _log      = log;
        _http     = http;
        _username = config["BiddingBuddyServices:Username"] ?? "admin";
        _password = config["BiddingBuddyServices:Password"] ?? "admin123";

        var baseUrl = config["BiddingBuddyServices:BaseUrl"]
            ?? throw new InvalidOperationException("BiddingBuddyServices:BaseUrl is not configured.");

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout     = TimeSpan.FromSeconds(30);
    }

    // ── public API ────────────────────────────────────────────────────────────

    public async Task<List<TenderListItemDto>> SearchTendersAsync(
        TenderSearchQueryDto query, CancellationToken ct = default)
    {
        var url = BuildSearchUrl(query);
        _log.LogDebug("BiddingBuddyServices → GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(ct));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Network error reaching BiddingBuddyServices at {Base}", _http.BaseAddress);
            throw new InvalidOperationException(
                $"Could not connect to BiddingBuddyServices: {ex.Message}", ex);
        }

        // Token may have been invalidated externally — refresh once and retry
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _log.LogWarning("BiddingBuddyServices returned 401 — refreshing token and retrying.");
            InvalidateToken();
            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await GetTokenAsync(ct));
            response = await _http.SendAsync(request, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("BiddingBuddyServices {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"BiddingBuddyServices returned {(int)response.StatusCode}: {body}");
        }
        
        var stream     = await response.Content.ReadAsStreamAsync(ct);
        var rawResult  = await JsonSerializer.DeserializeAsync<TenderSearchResultDto>(stream, _json, ct)
            ?? throw new InvalidOperationException("BiddingBuddyServices returned an empty response.");

        // Map the raw MongoDB items to the BFF's TenderListItemDto using the shared translator
        return rawResult.Items.ToListDto();
    }

    public async Task<PagedTenderListDto> SearchTendersPagedAsync(
        TenderSearchQueryDto query, CancellationToken ct = default)
    {
        var url = BuildSearchUrl(query);
        _log.LogDebug("BiddingBuddyServices → GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(ct));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Network error reaching BiddingBuddyServices at {Base}", _http.BaseAddress);
            throw new InvalidOperationException(
                $"Could not connect to BiddingBuddyServices: {ex.Message}", ex);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _log.LogWarning("BiddingBuddyServices returned 401 — refreshing token and retrying.");
            InvalidateToken();
            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await GetTokenAsync(ct));
            response = await _http.SendAsync(request, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("BiddingBuddyServices {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"BiddingBuddyServices returned {(int)response.StatusCode}: {body}");
        }

        var stream    = await response.Content.ReadAsStreamAsync(ct);
        var rawResult = await JsonSerializer.DeserializeAsync<TenderSearchResultDto>(stream, _json, ct)
            ?? throw new InvalidOperationException("BiddingBuddyServices returned an empty response.");

        return new PagedTenderListDto(
            Items:           rawResult.Items.ToListDto(),
            TotalCount:      rawResult.TotalCount,
            Page:            rawResult.Page,
            PageSize:        rawResult.PageSize,
            TotalPages:      rawResult.TotalPages,
            HasNextPage:     rawResult.HasNextPage,
            HasPreviousPage: rawResult.HasPreviousPage);
    }

    public async Task<TenderDetailDto> GetTenderAsync(
        string tenderId, CancellationToken ct = default)
    {
        var searchItem = await GetRawTenderAsync(tenderId, ct)
            ?? throw new InvalidOperationException("BiddingBuddyServices returned an empty response.");
        return searchItem.ToDetailsDto();
    }

    public async Task<TenderSearchItemDto?> GetRawTenderAsync(
        string tenderId, CancellationToken ct = default)
    {
        var url = $"api/tenders/{tenderId}";
        _log.LogDebug("BiddingBuddyServices → GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(ct));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Network error reaching BiddingBuddyServices at {Base}", _http.BaseAddress);
            throw new InvalidOperationException(
                $"Could not connect to BiddingBuddyServices: {ex.Message}", ex);
        }

        // Token may have been invalidated externally — refresh once and retry
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _log.LogWarning("BiddingBuddyServices returned 401 — refreshing token and retrying.");
            InvalidateToken();
            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await GetTokenAsync(ct));
            response = await _http.SendAsync(request, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("BiddingBuddyServices {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"BiddingBuddyServices returned {(int)response.StatusCode}: {body}");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<TenderSearchItemDto>(stream, _json, ct)
            ?? throw new InvalidOperationException("BiddingBuddyServices returned an empty response.");
    }

    // ── token management ──────────────────────────────────────────────────────

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Fast path — token still valid (with 60 s buffer)
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt - TimeSpan.FromSeconds(60))
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt - TimeSpan.FromSeconds(60))
                return _cachedToken;

            _log.LogInformation("Obtaining new token from BiddingBuddyServices /auth/token");

            var payload = JsonSerializer.Serialize(new { username = _username, password = _password });
            var tokenResp = await _http.PostAsync(
                "auth/token",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct);

            if (!tokenResp.IsSuccessStatusCode)
            {
                var err = await tokenResp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"BiddingBuddyServices auth failed ({(int)tokenResp.StatusCode}): {err}");
            }

            var tokenJson = await JsonSerializer.DeserializeAsync<TokenResponse>(
                await tokenResp.Content.ReadAsStreamAsync(ct), _json, ct);

            _cachedToken    = tokenJson!.AccessToken;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenJson.ExpiresIn);

            _log.LogInformation(
                "BiddingBuddyServices token obtained, expires in {ExpiresIn}s", tokenJson.ExpiresIn);

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _cachedToken    = null;
        _tokenExpiresAt = DateTime.MinValue;
    }

    // ── query string builder ──────────────────────────────────────────────────

    private static string BuildSearchUrl(TenderSearchQueryDto q)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrWhiteSpace(q.NameContains))
        {
            qs["NameContains"] = q.NameContains;
            //qs["CategoryPrimary"] = q.NameContains;
            qs["Tag"] = q.NameContains;
            qs["SourceTenderId"] = q.NameContains;
        }
        if (!string.IsNullOrWhiteSpace(q.Status))             qs["Status"]             = q.Status;
        if (!string.IsNullOrWhiteSpace(q.CategoryPrimary))    qs["CategoryPrimary"]    = q.CategoryPrimary;
        if (!string.IsNullOrWhiteSpace(q.CategorySecondary))  qs["CategorySecondary"]  = q.CategorySecondary;
        if (!string.IsNullOrWhiteSpace(q.Tag))                qs["Tag"]                = q.Tag;
        if (!string.IsNullOrWhiteSpace(q.Organization))       qs["Organization"]       = q.Organization;
        if (!string.IsNullOrWhiteSpace(q.Ministry))           qs["Ministry"]           = q.Ministry;
        if (!string.IsNullOrWhiteSpace(q.State))              qs["State"]              = q.State;
        if (!string.IsNullOrWhiteSpace(q.City))               qs["City"]               = q.City;
        if (q.BidEndFrom.HasValue)  qs["BidEndFrom"]  = q.BidEndFrom.Value.ToString("O"); // ISO 8601
        if (q.BidEndTo.HasValue)    qs["BidEndTo"]    = q.BidEndTo.Value.ToString("O");
        if (q.MinValue.HasValue)    qs["MinValue"]    = q.MinValue.Value.ToString("F2");
        if (q.MaxValue.HasValue)    qs["MaxValue"]    = q.MaxValue.Value.ToString("F2");
        if (!string.IsNullOrWhiteSpace(q.SortBy))             qs["SortBy"]             = q.SortBy;
        if (!string.IsNullOrWhiteSpace(q.SortOrder))          qs["SortOrder"]          = q.SortOrder;

        qs["Page"]     = Math.Max(1, q.Page).ToString();
        qs["PageSize"] = Math.Clamp(q.PageSize, 1, 100).ToString();

        var queryString = qs.ToString();
        return string.IsNullOrEmpty(queryString)
            ? "api/tenders/search"
            : $"api/tenders/search?{queryString}";
    }

    // ── inner types ───────────────────────────────────────────────────────────

    private sealed record TokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("tokenType")]   string TokenType,
        [property: JsonPropertyName("expiresIn")]   int    ExpiresIn);
}
