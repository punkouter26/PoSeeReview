using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// The last discovery search and what it found, so coming Back from a comic restores the grid
/// instead of re-running the search (or re-asking for geolocation).
/// </summary>
public sealed class DiscoveryState
{
    /// <summary>The text searched, or null for a location search.</summary>
    public string? Query { get; private set; }

    public List<RestaurantDto> Restaurants { get; private set; } = [];

    public void Set(string? query, List<RestaurantDto> restaurants) => (Query, Restaurants) = (query, restaurants);

    public void Clear() => Set(null, []);
}
