using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Infrastructure.Entities;

namespace Po.SeeReview.Infrastructure.Repositories;

/// <summary>
/// Repository for restaurant data with 24-hour cache in Azure Table Storage
/// Table: PoSeeReviewRestaurants
/// Partition Key: RESTAURANT_{Region}, Row Key: {PlaceId}
/// </summary>
public class RestaurantRepository : TableStorageRepository<RestaurantEntity>
{
    private new readonly ILogger<RestaurantRepository> _logger;
    private const int CacheExpirationHours = 24;

    public RestaurantRepository(
        TableServiceClient tableServiceClient,
        ILogger<RestaurantRepository> logger)
        : base(tableServiceClient, "PoSeeReviewRestaurants", logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets restaurant by Google place ID
    /// Returns null if not cached or cache expired (> 24 hours)
    /// </summary>
    public async Task<Restaurant?> GetByPlaceIdAsync(string placeId, string region)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentNullException(nameof(placeId));

        var partitionKey = RestaurantEntity.CreatePartitionKey(region);
        var rowKey = RestaurantEntity.CreateRowKey(placeId);

        var entity = await GetByIdAsync(partitionKey, rowKey);

        if (entity == null)
            return null;

        // Check cache expiration
        if (!IsCacheValid(entity.CachedAt))
        {
            _logger.LogInformation("Cache expired for restaurant {PlaceId}", placeId);
            return null;
        }

        return FromEntity(entity);
    }

    /// <summary>
    /// Gets all valid (non-expired) restaurants in a region
    /// </summary>
    public async Task<List<Restaurant>> GetByRegionAsync(string region)
    {
        var partitionKey = RestaurantEntity.CreatePartitionKey(region);
        var filter = $"PartitionKey eq '{partitionKey}'";

        var entities = await QueryAsync(filter);

        // Filter out expired cache entries and convert to domain models
        return entities
            .Where(e => IsCacheValid(e.CachedAt))
            .Select(FromEntity)
            .ToList();
    }

    /// <summary>
    /// Stores/updates restaurant with current timestamp
    /// </summary>
    public async Task UpsertRestaurantAsync(Restaurant restaurant)
    {
        if (restaurant == null)
            throw new ArgumentNullException(nameof(restaurant));

        restaurant.CachedAt = DateTimeOffset.UtcNow;

        var entity = ToEntity(restaurant);
        await UpsertAsync(entity);

        _logger.LogInformation(
            "Cached restaurant {PlaceId} in region {Region}",
            restaurant.PlaceId,
            restaurant.Region);
    }

    /// <summary>
    /// Maps domain Restaurant to Azure Table Storage entity
    /// </summary>
    private RestaurantEntity ToEntity(Restaurant restaurant)
    {
        return new RestaurantEntity
        {
            PartitionKey = RestaurantEntity.CreatePartitionKey(restaurant.Region),
            RowKey = RestaurantEntity.CreateRowKey(restaurant.PlaceId),
            PlaceId = restaurant.PlaceId,
            Name = restaurant.Name,
            Address = restaurant.Address,
            Latitude = restaurant.Latitude,
            Longitude = restaurant.Longitude,
            Region = restaurant.Region,
            AverageRating = restaurant.AverageRating,
            TotalReviews = restaurant.TotalReviews,
            ReviewsJson = JsonSerializer.Serialize(restaurant.Reviews),
            CachedAt = restaurant.CachedAt
        };
    }

    /// <summary>
    /// Maps Azure Table Storage entity to domain Restaurant
    /// </summary>
    private Restaurant FromEntity(RestaurantEntity entity)
    {
        return new Restaurant
        {
            PlaceId = entity.PlaceId,
            Name = entity.Name,
            Address = entity.Address,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            Region = entity.Region,
            AverageRating = entity.AverageRating,
            TotalReviews = entity.TotalReviews,
            Reviews = string.IsNullOrWhiteSpace(entity.ReviewsJson)
                ? new List<Review>()
                : JsonSerializer.Deserialize<List<Review>>(entity.ReviewsJson) ?? new List<Review>(),
            CachedAt = entity.CachedAt
        };
    }

    /// <summary>
    /// Checks if cached data is still valid (within 24 hours)
    /// </summary>
    private bool IsCacheValid(DateTimeOffset cachedAt)
    {
        return DateTimeOffset.UtcNow - cachedAt < TimeSpan.FromHours(CacheExpirationHours);
    }
}
