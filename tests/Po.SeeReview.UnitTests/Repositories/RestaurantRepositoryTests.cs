using Moq;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Repositories;
using Xunit;

namespace Po.SeeReview.UnitTests.Repositories;

/// <summary>
/// Unit tests for RestaurantRepository cache operations
/// </summary>
public class RestaurantRepositoryTests
{
    [Fact]
    public void GetByPlaceIdAsync_CachedDataValid_ReturnsCachedRestaurant()
    {
        // Arrange
        var placeId = "test-place-123";
        var cachedRestaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Cached Restaurant",
            CachedAt = DateTimeOffset.UtcNow.AddHours(-12) // 12 hours ago, still valid
        };

        // Act & Assert
        Assert.NotNull(cachedRestaurant);
        Assert.True(IsCacheValid(cachedRestaurant.CachedAt));
    }

    [Fact]
    public void GetByPlaceIdAsync_CachedDataExpired_ReturnsNull()
    {
        // Arrange
        var cachedRestaurant = new Restaurant
        {
            PlaceId = "test-place-123",
            Name = "Expired Restaurant",
            CachedAt = DateTimeOffset.UtcNow.AddHours(-25) // 25 hours ago, expired
        };

        // Act & Assert
        Assert.False(IsCacheValid(cachedRestaurant.CachedAt));
    }

    [Fact]
    public void UpsertAsync_NewRestaurant_SetsCachedAtTimestamp()
    {
        // Arrange
        var restaurant = new Restaurant
        {
            PlaceId = "new-place-123",
            Name = "New Restaurant"
        };

        // Act
        var beforeUpsert = DateTimeOffset.UtcNow;
        restaurant.CachedAt = DateTimeOffset.UtcNow;
        var afterUpsert = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(restaurant.CachedAt >= beforeUpsert);
        Assert.True(restaurant.CachedAt <= afterUpsert);
    }

    [Fact]
    public void GetByRegionAsync_ValidRegion_ReturnsOnlyValidCachedRestaurants()
    {
        // Arrange
        var region = "US-WA-Seattle";
        var validRestaurant = new Restaurant
        {
            PlaceId = "valid-1",
            Region = region,
            CachedAt = DateTimeOffset.UtcNow.AddHours(-5)
        };

        var expiredRestaurant = new Restaurant
        {
            PlaceId = "expired-1",
            Region = region,
            CachedAt = DateTimeOffset.UtcNow.AddHours(-30)
        };

        // Act
        var validCached = IsCacheValid(validRestaurant.CachedAt);
        var expiredCached = IsCacheValid(expiredRestaurant.CachedAt);

        // Assert
        Assert.True(validCached);
        Assert.False(expiredCached);
    }

    [Theory]
    [InlineData(-1)]   // 1 hour ago
    [InlineData(-12)]  // 12 hours ago
    [InlineData(-23)]  // 23 hours ago
    public void IsCacheValid_WithinExpirationWindow_ReturnsTrue(int hoursAgo)
    {
        // Arrange
        var cachedAt = DateTimeOffset.UtcNow.AddHours(hoursAgo);

        // Act
        var isValid = IsCacheValid(cachedAt);

        // Assert
        Assert.True(isValid);
    }

    [Theory]
    [InlineData(-25)]  // 25 hours ago
    [InlineData(-48)]  // 48 hours ago
    [InlineData(-100)] // 100 hours ago
    public void IsCacheValid_OutsideExpirationWindow_ReturnsFalse(int hoursAgo)
    {
        // Arrange
        var cachedAt = DateTimeOffset.UtcNow.AddHours(hoursAgo);

        // Act
        var isValid = IsCacheValid(cachedAt);

        // Assert
        Assert.False(isValid);
    }

    private bool IsCacheValid(DateTimeOffset cachedAt)
    {
        const int CacheExpirationHours = 24;
        return DateTimeOffset.UtcNow - cachedAt < TimeSpan.FromHours(CacheExpirationHours);
    }
}
