using System.Text.Json.Serialization;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Response from GET /api/restaurants/nearby and GET /api/restaurants/search
/// </summary>
public class NearbyRestaurantsResponse
{
    public List<RestaurantDto> Restaurants { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTimeOffset CachedAt { get; set; }

    /// <summary>
    /// True when the response was synthesized (e.g. dev short-circuit, stale cache,
    /// upstream unavailable). Clients should display a "configure your API key" or
    /// "data may be stale" hint instead of an empty-results error.
    /// </summary>
    [JsonPropertyName("stale")]
    public bool Stale { get; set; }

    /// <summary>
    /// Optional human-readable hint explaining why the response is <see cref="Stale"/>.
    /// </summary>
    [JsonPropertyName("staleReason")]
    public string? StaleReason { get; set; }
}
