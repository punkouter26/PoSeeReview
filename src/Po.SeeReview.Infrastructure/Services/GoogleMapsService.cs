using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Google Maps API integration service for restaurant discovery
/// </summary>
public class GoogleMapsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleMapsService> _logger;
    private readonly string _apiKey;

    public GoogleMapsService(
        HttpClient httpClient,
        ILogger<GoogleMapsService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["GoogleMaps:ApiKey"] ?? throw new InvalidOperationException("GoogleMaps:ApiKey not configured");
    }

    /// <summary>
    /// Validates geographic coordinates
    /// </summary>
    public bool ValidateCoordinates(double latitude, double longitude)
    {
        if (latitude < -90 || latitude > 90)
        {
            _logger.LogWarning("Invalid latitude: {Latitude}", latitude);
            return false;
        }

        if (longitude < -180 || longitude > 180)
        {
            _logger.LogWarning("Invalid longitude: {Longitude}", longitude);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Searches for nearby restaurants using Google Places API (New)
    /// </summary>
    /// <param name="latitude">Search center latitude</param>
    /// <param name="longitude">Search center longitude</param>
    /// <param name="radiusMeters">Search radius in meters (default 5000m = 5km)</param>
    /// <returns>List of restaurants with basic metadata</returns>
    public async Task<List<Restaurant>> SearchNearbyAsync(
        double latitude,
        double longitude,
        int radiusMeters = 5000)
    {
        if (!ValidateCoordinates(latitude, longitude))
        {
            throw new ArgumentException("Invalid coordinates");
        }

        _logger.LogInformation(
            "Searching nearby restaurants at ({Latitude}, {Longitude}) within {Radius}m",
            latitude, longitude, radiusMeters);

        // Google Places API (New) endpoint - requires API key in header, not query string
        var requestBody = new
        {
            includedTypes = new[] { "restaurant" },
            maxResultCount = 20,
            locationRestriction = new
            {
                circle = new
                {
                    center = new
                    {
                        latitude,
                        longitude
                    },
                    radius = radiusMeters
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchNearby")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("X-Goog-Api-Key", _apiKey);
        request.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress,places.location,places.rating,places.userRatingCount");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Google Maps API error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<GooglePlacesResponse>();

        return result?.Places?.Select(p => new Restaurant
        {
            PlaceId = p.Id ?? string.Empty,
            Name = p.DisplayName?.Text ?? string.Empty,
            Address = p.FormattedAddress ?? string.Empty,
            Latitude = p.Location?.Latitude ?? 0,
            Longitude = p.Location?.Longitude ?? 0,
            AverageRating = p.Rating ?? 0,
            TotalReviews = p.UserRatingCount ?? 0,
            Region = DetermineRegion(latitude, longitude),
            CachedAt = DateTimeOffset.UtcNow
        }).ToList() ?? new List<Restaurant>();
    }

    /// <summary>
    /// Determines region code from coordinates (simplified implementation)
    /// Returns ISO 3166-1 alpha-2 country codes for leaderboard compatibility
    /// </summary>
    private string DetermineRegion(double latitude, double longitude)
    {
        // Simplified region determination based on latitude/longitude
        // United States
        if (latitude >= 24.0 && latitude <= 50.0 && longitude >= -125.0 && longitude <= -66.0)
        {
            return "US";
        }
        
        // Canada
        if (latitude >= 41.0 && latitude <= 84.0 && longitude >= -141.0 && longitude <= -52.0)
        {
            return "CA";
        }
        
        // United Kingdom (rough bounds)
        if (latitude >= 49.0 && latitude <= 61.0 && longitude >= -8.0 && longitude <= 2.0)
        {
            return "GB";
        }
        
        // Australia
        if (latitude >= -44.0 && latitude <= -10.0 && longitude >= 112.0 && longitude <= 154.0)
        {
            return "AU";
        }

        // Default to US if unknown
        return "US";
    }

    // Google Places API (New) response models
    private class GooglePlacesResponse
    {
        [JsonPropertyName("places")]
        public List<Place>? Places { get; set; }
    }

    private class Place
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public DisplayName? DisplayName { get; set; }

        [JsonPropertyName("formattedAddress")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("location")]
        public Location? Location { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("userRatingCount")]
        public int? UserRatingCount { get; set; }
    }

    private class DisplayName
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class Location
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }

    /// <summary>
    /// Gets detailed place information including reviews
    /// </summary>
    public async Task<Restaurant?> GetPlaceDetailsAsync(string placeId)
    {
        _logger.LogInformation("Fetching place details for {PlaceId}", placeId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://places.googleapis.com/v1/places/{placeId}");
        request.Headers.Add("X-Goog-Api-Key", _apiKey);
        request.Headers.Add("X-Goog-FieldMask", "id,displayName,formattedAddress,location,rating,userRatingCount,reviews");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Google Maps API error fetching details for {PlaceId}: {StatusCode} - {ErrorContent}",
                placeId, response.StatusCode, errorContent);

            if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                                    or System.Net.HttpStatusCode.BadRequest)
            {
                // Invalid or expired place ID — treat as not found
                return null;
            }

            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<PlaceDetailsResponse>();

        if (result == null)
        {
            return null;
        }

        // The API returns place details directly (not nested in a "place" property)
        var place = result;
        var restaurant = new Restaurant
        {
            PlaceId = place.Id ?? placeId,
            Name = place.DisplayName?.Text ?? string.Empty,
            Address = place.FormattedAddress ?? string.Empty,
            Latitude = place.Location?.Latitude ?? 0,
            Longitude = place.Location?.Longitude ?? 0,
            AverageRating = place.Rating ?? 0,
            TotalReviews = place.UserRatingCount ?? 0,
            Region = DetermineRegion(place.Location?.Latitude ?? 0, place.Location?.Longitude ?? 0),
            CachedAt = DateTimeOffset.UtcNow,
            Reviews = place.Reviews?.Select(r => new Review
            {
                AuthorName = r.AuthorAttribution?.DisplayName ?? "Anonymous",
                Rating = r.Rating ?? 0,
                Text = r.Text?.Text ?? r.OriginalText?.Text ?? string.Empty,
                Time = !string.IsNullOrEmpty(r.PublishTime) && DateTimeOffset.TryParse(r.PublishTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var publishTime)
                    ? publishTime
                    : DateTimeOffset.UtcNow,
                StrangenessScore = 0 // Will be calculated later by AI
            }).ToList() ?? new List<Review>()
        };

        _logger.LogInformation("Fetched {ReviewCount} reviews for place {PlaceId}", restaurant.Reviews.Count, placeId);

        return restaurant;
    }

    // Place Details API response models
    private class PlaceDetailsResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public DisplayName? DisplayName { get; set; }

        [JsonPropertyName("formattedAddress")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("location")]
        public Location? Location { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("userRatingCount")]
        public int? UserRatingCount { get; set; }

        [JsonPropertyName("reviews")]
        public List<PlaceReview>? Reviews { get; set; }

        // Support both wrapper and direct access patterns
        [JsonPropertyName("place")]
        public PlaceDetailsResponse? Place { get; set; }
    }

    private class PlaceReview
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("relativePublishTimeDescription")]
        public string? RelativePublishTimeDescription { get; set; }

        [JsonPropertyName("rating")]
        public int? Rating { get; set; }

        [JsonPropertyName("text")]
        public TextContent? Text { get; set; }

        [JsonPropertyName("originalText")]
        public TextContent? OriginalText { get; set; }

        [JsonPropertyName("authorAttribution")]
        public AuthorAttribution? AuthorAttribution { get; set; }

        [JsonPropertyName("publishTime")]
        public string? PublishTime { get; set; }
    }

    private class TextContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("languageCode")]
        public string? LanguageCode { get; set; }
    }

    private class AuthorAttribution
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [JsonPropertyName("photoUri")]
        public string? PhotoUri { get; set; }
    }
}
