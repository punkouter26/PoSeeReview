using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Core.Utilities;
using Po.SeeReview.Infrastructure.Repositories;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Service for discovering and managing restaurant data with 24-hour caching
/// </summary>
public class RestaurantService : IRestaurantService
{
    private readonly RestaurantRepository _repository;
    private readonly GoogleMapsService _googleMapsService;
    private readonly ILogger<RestaurantService> _logger;

    public RestaurantService(
        RestaurantRepository repository,
        GoogleMapsService googleMapsService,
        ILogger<RestaurantService> logger)
    {
        _repository = repository;
        _googleMapsService = googleMapsService;
        _logger = logger;
    }

    /// <summary>
    /// Finds nearby restaurants within 5km radius using Google Maps API
    /// Returns cached results if available (24-hour TTL)
    /// </summary>
    public async Task<List<Restaurant>> GetNearbyRestaurantsAsync(
        double latitude,
        double longitude,
        int limit = 10)
    {
        if (!_googleMapsService.ValidateCoordinates(latitude, longitude))
        {
            throw new ArgumentException("Invalid coordinates: latitude must be -90 to 90, longitude must be -180 to 180");
        }

        if (limit < 1 || limit > 50)
        {
            throw new ArgumentException("Limit must be between 1 and 50");
        }

        _logger.LogInformation(
            "Getting nearby restaurants at ({Latitude}, {Longitude}), limit {Limit}",
            latitude, longitude, limit);

        // Search via Google Maps API
        var restaurants = await _googleMapsService.SearchNearbyAsync(latitude, longitude);

        // Calculate distance from user location for each restaurant
        foreach (var restaurant in restaurants)
        {
            restaurant.Reviews = new List<Review>(); // Don't load reviews for nearby search (performance)
        }

        // Cache each restaurant (upsert will set CachedAt)
        foreach (var restaurant in restaurants)
        {
            await _repository.UpsertRestaurantAsync(restaurant);
        }

        // Return top N by distance
        return restaurants
            .OrderBy(r => GeoUtils.CalculateDistance(latitude, longitude, r.Latitude, r.Longitude))
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets detailed restaurant information by Google place ID
    /// Returns cached data if available (24-hour TTL), otherwise fetches from Google Maps
    /// </summary>
    public async Task<Restaurant> GetRestaurantByPlaceIdAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentNullException(nameof(placeId));
        }

        _logger.LogInformation("Getting restaurant details for {PlaceId}", placeId);

        // Try cache first (requires region - simplified, use wildcard query)
        var cachedRestaurant = await TryGetFromCacheAsync(placeId);

        if (cachedRestaurant != null && cachedRestaurant.Reviews?.Count > 0)
        {
            _logger.LogInformation("Cache hit for restaurant {PlaceId} with {ReviewCount} reviews", placeId, cachedRestaurant.Reviews.Count);
            return cachedRestaurant;
        }

        _logger.LogInformation("Cache miss or no reviews for restaurant {PlaceId}, fetching from Google Maps", placeId);

        // Fetch from Google Maps with reviews
        var restaurant = await _googleMapsService.GetPlaceDetailsAsync(placeId);

        if (restaurant == null)
        {
            throw new KeyNotFoundException($"Restaurant with placeId '{placeId}' not found");
        }

        // Cache the restaurant with reviews
        await _repository.UpsertRestaurantAsync(restaurant);
        _logger.LogInformation("Cached restaurant {PlaceId} with {ReviewCount} reviews", placeId, restaurant.Reviews?.Count ?? 0);

        return restaurant;
    }

    /// <summary>
    /// Gets detailed restaurant information including reviews (for comic generation)
    /// </summary>
    public async Task<Restaurant?> GetRestaurantDetailsAsync(string placeId)
    {
        try
        {
            return await GetRestaurantByPlaceIdAsync(placeId);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to get restaurant from cache across all regions
    /// (Simplified implementation - in production, use secondary index or cosmos DB)
    /// </summary>
    private async Task<Restaurant?> TryGetFromCacheAsync(string placeId)
    {
        // Simplified: Try common regions (in production, use global secondary index)
        var commonRegions = new[] { "US-WA-Seattle", "US-CA-SF", "US-NY-NYC", "US-Unknown-Unknown" };

        foreach (var region in commonRegions)
        {
            var restaurant = await _repository.GetByPlaceIdAsync(placeId, region);
            if (restaurant != null)
            {
                return restaurant;
            }
        }

        return null;
    }
}
