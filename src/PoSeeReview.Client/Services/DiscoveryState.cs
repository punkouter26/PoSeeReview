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

    /// <summary>Raised by <see cref="Clear"/>, so a discovery page already on screen drops its results too.</summary>
    public event Action? Cleared;

    public void Set(string? query, List<RestaurantDto> restaurants) => (Query, Restaurants) = (query, restaurants);

    /// <summary>Back to the landing — what the brand link does.</summary>
    public void Clear()
    {
        Set(null, []);
        Cleared?.Invoke();
    }
}
