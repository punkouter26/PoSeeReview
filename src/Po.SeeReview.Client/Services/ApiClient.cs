using System.Net.Http.Json;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// HTTP client for calling the PoSeeReview API backend.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets nearby restaurants based on coordinates.
    /// </summary>
    public async Task<NearbyRestaurantsResponse?> GetNearbyRestaurantsAsync(
        double latitude,
        double longitude,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<NearbyRestaurantsResponse>(
                $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}",
                cancellationToken);

            return response;
        }
        catch (HttpRequestException)
        {
            // Log error - for now just return null
            return null;
        }
    }

    /// <summary>
    /// Gets restaurant details by place ID.
    /// </summary>
    public async Task<RestaurantDetailsDto?> GetRestaurantDetailsAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RestaurantDetailsDto>(
                $"/api/restaurants/{placeId}",
                cancellationToken);

            return response;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a comic for the given restaurant place ID.
    /// This may take 8-10 seconds for a new comic generation.
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

        var response = await _httpClient.PostAsync(url, null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var comic = await response.Content.ReadFromJsonAsync<ComicDto>(cancellationToken: cancellationToken);
        return comic ?? throw new InvalidOperationException("Comic response was null");
    }

    /// <summary>
    /// Gets a cached comic for the given restaurant place ID.
    /// </summary>
    public async Task<ComicDto> GetCachedComicAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/comics/{placeId}",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var comic = await response.Content.ReadFromJsonAsync<ComicDto>(cancellationToken);
        return comic ?? throw new InvalidOperationException("Comic response was null");
    }
}

/// <summary>
/// Response from nearby restaurants endpoint.
/// </summary>
public class NearbyRestaurantsResponse
{
    public List<RestaurantDto> Restaurants { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
