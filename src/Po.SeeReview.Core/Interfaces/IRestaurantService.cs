using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for discovering and managing restaurant data with 24-hour caching
/// </summary>
public interface IRestaurantService
{
    /// <summary>
    /// Finds nearby restaurants within 5km radius using Google Maps API
    /// Returns cached results if available (24-hour TTL)
    /// </summary>
    /// <param name="latitude">User latitude (-90 to 90)</param>
    /// <param name="longitude">User longitude (-180 to 180)</param>
    /// <param name="limit">Maximum results to return (default 10)</param>
    /// <returns>List of restaurants with distance from user</returns>
    /// <exception cref="ArgumentException">Invalid coordinates</exception>
    /// <exception cref="HttpRequestException">Google Maps API failure</exception>
    Task<List<Restaurant>> GetNearbyRestaurantsAsync(double latitude, double longitude, int limit = 10);

    /// <summary>
    /// Gets detailed restaurant information by Google place ID
    /// Returns cached data if available (24-hour TTL), otherwise fetches from Google Maps
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Restaurant with reviews and strangeness scores</returns>
    /// <exception cref="ArgumentNullException">placeId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Restaurant not found</exception>
    Task<Restaurant> GetRestaurantByPlaceIdAsync(string placeId);

    /// <summary>
    /// Gets detailed restaurant information including reviews (alias for GetRestaurantByPlaceIdAsync)
    /// Used by comic generation service to fetch reviews for analysis
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Restaurant with full review details, or null if not found</returns>
    Task<Restaurant?> GetRestaurantDetailsAsync(string placeId);
}
