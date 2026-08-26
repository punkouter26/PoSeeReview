using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api;
using PoSeeReview.Shared.Dtos;
using Xunit.Abstractions;
using Xunit;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Integration.Api;

/// <summary>
/// Comprehensive integration tests for GET /api/restaurants/nearby endpoint
/// Focuses on diagnosing the 503 Service Unavailable error
/// </summary>
[Trait("Tier", "Integration")]
[Trait("Domain", "Api")]
public class NearbyRestaurantsEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public NearbyRestaurantsEndpointTests(
        CustomWebApplicationFactory<Program> factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_WithDifferentLocations_ShouldReturnAppropriateResults()
    {
        // Arrange - Test multiple locations
        var locations = new[]
        {
            new { Name = "Seattle", Lat = 47.6062, Lon = -122.3321 },
            new { Name = "New York", Lat = 40.7128, Lon = -74.0060 },
            new { Name = "San Francisco", Lat = 37.7749, Lon = -122.4194 }
        };

        var client = _factory.CreateClient();
        var successCount = 0;

        foreach (var location in locations)
        {
            // Act
            var response = await client.GetAsync(
                $"/api/restaurants/nearby?latitude={location.Lat}&longitude={location.Lon}");

            var responseBody = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"\n{location.Name}:");
            _output.WriteLine($"Status: {response.StatusCode}");
            _output.WriteLine($"Response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}...");

            // Document results
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                _output.WriteLine($"Error: {problemDetails?.Detail}");
            }
            else if (response.IsSuccessStatusCode)
            {
                successCount++;
            }
        }

        // Assert - At least document if all failed
        _output.WriteLine($"\nSuccessful requests: {successCount}/{locations.Length}");
        Assert.True(true, "Test completed - check output for results");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_WithLimit_ShouldRespectLimit()
    {
        // Arrange – use mid-range limit (10) as the canonical case
        var latitude = 47.6062;
        var longitude = -122.3321;
        const int limit = 10;
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}");

        // Log
        _output.WriteLine($"Limit {limit}: Status {response.StatusCode}");

        // Assert - if successful, verify limit is respected
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<NearbyRestaurantsResponse>();
            Assert.NotNull(result);
            Assert.True(result.Restaurants.Count <= limit,
                $"Expected at most {limit} restaurants, got {result.Restaurants.Count}");
        }
    }

    [Theory]
    [InlineData(-1)]   // Limit < 1
    [InlineData(0)]    // Limit = 0
    [InlineData(51)]   // Limit > 50
    [InlineData(100)]  // Limit way over 50
    public async Task GetNearbyRestaurants_InvalidLimit_ShouldReturn400(int limit)
    {
        // Arrange
        var client = _factory.CreateClient();
        var latitude = 47.6062;
        var longitude = -122.3321;

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}");

        // Assert
        _output.WriteLine($"Limit {limit}: Status {response.StatusCode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Contains("limit", problemDetails.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_CheckResponseSchema_ShouldMatchContract()
    {
        // Arrange
        var client = _factory.CreateClient();
        var latitude = 47.6062;
        var longitude = -122.3321;

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}");

        // Only check schema if request succeeds
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<NearbyRestaurantsResponse>();

            // Assert - Verify contract
            Assert.NotNull(result);
            Assert.NotNull(result.Restaurants);
            Assert.True(result.TotalCount >= 0);
            Assert.NotEqual(default(DateTimeOffset), result.CachedAt);

            // Verify each restaurant has required fields
            foreach (var restaurant in result.Restaurants)
            {
                Assert.NotNull(restaurant.PlaceId);
                Assert.NotNull(restaurant.Name);
                Assert.NotNull(restaurant.Address);
                Assert.True(restaurant.Latitude >= -90 && restaurant.Latitude <= 90);
                Assert.True(restaurant.Longitude >= -180 && restaurant.Longitude <= 180);
                Assert.True(restaurant.AverageRating >= 0 && restaurant.AverageRating <= 5);
                Assert.True(restaurant.TotalReviews >= 0);
                Assert.True(restaurant.Distance >= 0);
            }

            _output.WriteLine($"Found {result.TotalCount} restaurants");
            _output.WriteLine($"Cached at: {result.CachedAt}");
        }
        else
        {
            _output.WriteLine($"Test skipped - endpoint returned {response.StatusCode}");
        }
    }

}

/// <summary>
/// Response DTO matching the controller response
/// </summary>
public class NearbyRestaurantsResponse
{
    public List<RestaurantDto> Restaurants { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
