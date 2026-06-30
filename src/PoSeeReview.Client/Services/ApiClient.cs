using System.Net.Http.Json;
using System.Text.Json;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// HTTP client for calling the PoSeeReview API backend.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly DevSessionClient _devSessionClient;

    public ApiClient(HttpClient httpClient, DevSessionClient devSessionClient)
    {
        _httpClient = httpClient;
        _devSessionClient = devSessionClient;
    }

    /// <summary>
    /// Gets nearby restaurants based on coordinates.
    /// On non-success the response body is parsed for an RFC 7807 <c>ProblemDetails</c> payload
    /// so the user-facing message is actionable instead of a generic 503.
    /// Returns <c>null</c> only when the body cannot be parsed.
    /// </summary>
    public async Task<NearbyRestaurantsResponse?> GetNearbyRestaurantsAsync(
        double latitude,
        double longitude,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}");
        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            var detail = TryExtractProblemDetail(errorBody)
                ?? $"Nearby restaurant search failed (HTTP {(int)httpResponse.StatusCode}).";

            throw new HttpRequestException(detail, null, httpResponse.StatusCode);
        }

        return await httpResponse.Content.ReadFromJsonAsync(AppJsonContext.Default.NearbyRestaurantsResponse, cancellationToken);
    }

    /// <summary>
    /// Generates a comic for the given restaurant place ID.
    /// This may take 8-10 seconds for a new comic generation.
    /// On non-success the response body is parsed for an RFC 7807 <c>ProblemDetails</c> payload
    /// so the user-facing message is actionable instead of the opaque
    /// "net_http_message_not_success_statuscode_reason, 500, Internal Server Error" surfaced by
    /// <c>HttpRequestException</c>.
    /// </summary>
    public async Task<ComicDto> GenerateComicAsync(
        string placeId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/comics/{placeId}";
        if (forceRegenerate)
        {
            url += "?forceRegenerate=true";
        }

        using var request = await CreateRequestAsync(HttpMethod.Post, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractProblemDetail(errorBody)
                ?? $"Comic generation failed (HTTP {(int)response.StatusCode}). Please try again in a moment.";

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var comic = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ComicDto, cancellationToken);
        return comic ?? throw new InvalidOperationException("Comic response was null");
    }

    /// <summary>
    /// Searches for restaurants by location query (city name or ZIP code).
    /// </summary>
    public async Task<NearbyRestaurantsResponse?> SearchRestaurantsByLocationAsync(
        string locationQuery,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/restaurants/search?location={Uri.EscapeDataString(locationQuery)}&limit={limit}";
        using var request = await CreateRequestAsync(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractProblemDetail(errorBody)
                ?? $"Search request failed with status {(int)response.StatusCode}.";

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.NearbyRestaurantsResponse, cancellationToken);
        return payload;
    }

    private static string? TryExtractProblemDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("detail", out var detailElement))
            {
                var detail = detailElement.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return detail;
                }
            }

            if (root.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
        }
        catch (JsonException)
        {
            // Response may not be JSON; fall through to null.
        }

        return null;
    }

    /// <summary>
    /// Gets the leaderboard entries for a given region.
    /// </summary>
    public async Task<LeaderboardResponse?> GetLeaderboardAsync(
        string region = "US",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(
                HttpMethod.Get,
                $"/api/leaderboard?region={region}&limit={limit}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.LeaderboardResponse, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<HealthStatusDto?> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/health");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.HealthStatusDto, cancellationToken);
    }

    public async Task<DiagnosticsSnapshotDto?> GetDiagnosticsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/diag");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DiagnosticsSnapshotDto, cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        await _devSessionClient.AttachStoredHeaderAsync(request);
        return request;
    }
}
