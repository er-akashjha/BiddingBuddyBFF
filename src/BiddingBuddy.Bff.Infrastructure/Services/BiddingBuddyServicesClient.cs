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

    public Task<List<TenderEnumerationDto>> EnumerateTendersAsync(
        string? afterId, int limit, CancellationToken ct = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(afterId)) qs["afterId"] = afterId;
        qs["limit"] = limit.ToString();
        return GetJsonAsync<List<TenderEnumerationDto>>($"api/tenders/enumerate?{qs}", ct);
    }

    public Task<TenderFacetsDto> GetTenderFacetsAsync(int limit = 15, CancellationToken ct = default)
        => GetJsonAsync<TenderFacetsDto>($"api/tenders/facets?limit={limit}", ct);

    public Task<List<string>> GetTenderFacetOptionsAsync(
        string field, string? search, int limit, CancellationToken ct = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["field"] = field;
        if (!string.IsNullOrWhiteSpace(search)) qs["search"] = search;
        qs["limit"] = limit.ToString();
        return GetJsonAsync<List<string>>($"api/tenders/facet-options?{qs}", ct);
    }

    public Task<List<StateTenderCountDto>> GetStateTenderCountsAsync(CancellationToken ct = default)
        => GetJsonAsync<List<StateTenderCountDto>>("api/tenders/state-counts", ct);

    public Task<TenderResultDto?> GetTenderResultAsync(
        string platform, string platformTenderId, CancellationToken ct = default)
    {
        // platformTenderId (GeM bid number) carries slashes → pass as a query param, not a path segment.
        var qs = new Dictionary<string, string?>
        {
            ["platform"] = platform,
            ["platformTenderId"] = platformTenderId,
        };
        return GetJsonOrNullAsync<TenderResultDto>(
            "api/tender-results/by-tender?" + QueryString(qs), ct);
    }

    public Task<MarketPricingStatsDto> GetMarketPricingAsync(
        MarketFilterDto filter, CancellationToken ct = default)
        => GetJsonAsync<MarketPricingStatsDto>(Url("api/tender-results/market/pricing", filter.ToQuery()), ct);

    public Task<List<MarketGroupBucketDto>> GetMarketGroupedAsync(
        MarketFilterDto filter, string groupBy, int limit, CancellationToken ct = default)
    {
        var qs = filter.ToQuery();
        qs["groupBy"] = groupBy;
        qs["limit"] = limit.ToString();
        return GetJsonAsync<List<MarketGroupBucketDto>>(Url("api/tender-results/market/grouped", qs), ct);
    }

    public Task<List<SellerStatsDto>> GetTopSellersAsync(
        MarketFilterDto filter, int limit, CancellationToken ct = default)
    {
        var qs = filter.ToQuery();
        qs["limit"] = limit.ToString();
        return GetJsonAsync<List<SellerStatsDto>>(Url("api/tender-results/market/sellers", qs), ct);
    }

    public Task<SellerStatsDto?> GetSellerStatsAsync(string seller, CancellationToken ct = default)
        => GetJsonOrNullAsync<SellerStatsDto>(
            Url("api/tender-results/market/sellers/profile", new Dictionary<string, string?> { ["seller"] = seller }), ct);

    public Task<List<HeadToHeadRecordDto>> GetHeadToHeadAsync(
        string seller, MarketFilterDto filter, int limit, CancellationToken ct = default)
    {
        var qs = filter.ToQuery();
        qs["seller"] = seller;
        qs["limit"] = limit.ToString();
        return GetJsonAsync<List<HeadToHeadRecordDto>>(Url("api/tender-results/market/head-to-head", qs), ct);
    }

    public Task<BuyerProfileDto?> GetBuyerProfileAsync(string buyer, CancellationToken ct = default)
        => GetJsonOrNullAsync<BuyerProfileDto>(
            Url("api/tender-results/market/buyer", new Dictionary<string, string?> { ["buyer"] = buyer }), ct);

    public Task<List<TenderResultDto>> GetComparableAwardsAsync(
        string? category, string? state, decimal? estimatedValue, int limit, CancellationToken ct = default)
    {
        var qs = new Dictionary<string, string?> { ["limit"] = limit.ToString() };
        if (!string.IsNullOrWhiteSpace(category)) qs["category"] = category;
        if (!string.IsNullOrWhiteSpace(state)) qs["state"] = state;
        if (estimatedValue.HasValue)
            qs["estimatedValue"] = estimatedValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return GetJsonAsync<List<TenderResultDto>>(Url("api/tender-results/market/comparables", qs), ct);
    }

    private static string Url(string path, IDictionary<string, string?> qs) =>
        qs.Count > 0 ? $"{path}?{QueryString(qs)}" : path;

    private static string QueryString(IDictionary<string, string?> qs) =>
        string.Join("&", qs.Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

    /// <summary>
    /// Shared authenticated GET + deserialize with one-shot 401 token refresh.
    /// Mirrors the inline pattern used by the older methods in this class.
    /// </summary>
    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
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
        return await JsonSerializer.DeserializeAsync<T>(stream, _json, ct)
            ?? throw new InvalidOperationException("BiddingBuddyServices returned an empty response.");
    }

    /// <summary>Like <see cref="GetJsonAsync{T}"/> but returns <c>default</c> (null) on a 404 instead
    /// of throwing — used for optional resources (e.g. a tender that isn't awarded yet).</summary>
    private async Task<T?> GetJsonOrNullAsync<T>(string url, CancellationToken ct)
    {
        _log.LogDebug("BiddingBuddyServices → GET {Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));

        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
            response = await _http.SendAsync(request, ct);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("BiddingBuddyServices {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"BiddingBuddyServices returned {(int)response.StatusCode}: {body}");
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, _json, ct);
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

        // Multi-select facets — repeated query params (Categories=a&Categories=b)
        // bind to the List<string> properties on the Services-side TenderSearchQuery.
        if (q.Categories is not null)
            foreach (var c in q.Categories)
                if (!string.IsNullOrWhiteSpace(c)) qs.Add("Categories", c);
        if (q.States is not null)
            foreach (var s in q.States)
                if (!string.IsNullOrWhiteSpace(s)) qs.Add("States", s);
        if (q.Platforms is not null)
            foreach (var p in q.Platforms)
                if (!string.IsNullOrWhiteSpace(p)) qs.Add("Platforms", p);
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
