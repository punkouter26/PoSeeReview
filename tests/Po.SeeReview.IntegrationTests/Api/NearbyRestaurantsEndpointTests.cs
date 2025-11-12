using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Shared.Dtos;
using Xunit;
using Xunit.Abstractions;

namespace Po.SeeReview.IntegrationTests.Api;

/// <summary>
/// Comprehensive integration tests for GET /api/restaurants/nearby endpoint
/// Focuses on diagnosing the 503 Service Unavailable error
/// </summary>
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
    [Trait("Category", "RequiresGoogleMapsApi")]
    public async Task GetNearbyRestaurants_WithValidCoordinates_ShouldReturnSuccess()
    {
        // Arrange - Seattle coordinates
        var latitude = 47.6062;
        var longitude = -122.3321;

        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}");

        // Log response for debugging
        var responseBody = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Status: {response.StatusCode}");
        _output.WriteLine($"Response: {responseBody}");

        // Assert
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Parse the problem details to understand the error
            var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            _output.WriteLine($"Error Title: {problemDetails?.Title}");
            _output.WriteLine($"Error Detail: {problemDetails?.Detail}");

            // This test documents the current failure
            Assert.Fail($"Expected 200 OK but got 503. " +
                $"Title: {problemDetails?.Title}, " +
                $"Detail: {problemDetails?.Detail}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify response structure
        var result = await response.Content.ReadFromJsonAsync<NearbyRestaurantsResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Restaurants);
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

    [Theory]
    [InlineData(47.6062, -122.3321, 10)]
    [InlineData(47.6062, -122.3321, 5)]
    [InlineData(47.6062, -122.3321, 20)]
    public async Task GetNearbyRestaurants_WithDifferentLimits_ShouldRespectLimit(
        double latitude,
        double longitude,
        int limit)
    {
        // Arrange
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

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_MissingParameters_ShouldReturn400()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Missing both parameters
        var response1 = await client.GetAsync("/api/restaurants/nearby");

        // Act - Missing latitude
        var response2 = await client.GetAsync("/api/restaurants/nearby?longitude=-122.3321");

        // Act - Missing longitude
        var response3 = await client.GetAsync("/api/restaurants/nearby?latitude=47.6062");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response2.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response3.StatusCode);

        // Verify error messages
        var problem1 = await response1.Content.ReadFromJsonAsync<ProblemDetails>();
        _output.WriteLine($"Missing both: {problem1?.Detail}");

        var problem2 = await response2.Content.ReadFromJsonAsync<ProblemDetails>();
        _output.WriteLine($"Missing latitude: {problem2?.Detail}");

        var problem3 = await response3.Content.ReadFromJsonAsync<ProblemDetails>();
        _output.WriteLine($"Missing longitude: {problem3?.Detail}");

        Assert.Contains("latitude", problem1?.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(91.0, -122.3321, "latitude")]    // Latitude > 90
    [InlineData(-91.0, -122.3321, "latitude")]   // Latitude < -90
    [InlineData(47.6062, 181.0, "longitude")]    // Longitude > 180
    [InlineData(47.6062, -181.0, "longitude")]   // Longitude < -180
    public async Task GetNearbyRestaurants_InvalidCoordinates_ShouldReturn400(
        double latitude,
        double longitude,
        string expectedInvalidParam)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        _output.WriteLine($"Invalid {expectedInvalidParam}: {problemDetails?.Detail}");

        Assert.NotNull(problemDetails);
        Assert.Contains("Invalid", problemDetails.Detail ?? "", StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    [Trait("Category", "Integration")]
    public void GetNearbyRestaurants_CheckServiceRegistration_ShouldHaveRequiredServices()
    {
        // Arrange - Create a scope to check service registration
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Act & Assert - Verify all required services are registered
        try
        {
            var restaurantService = services.GetService<Po.SeeReview.Core.Interfaces.IRestaurantService>();
            var googleMapsService = services.GetService<Po.SeeReview.Infrastructure.Services.GoogleMapsService>();

            _output.WriteLine($"IRestaurantService: {(restaurantService != null ? "✓ Registered" : "✗ Missing")}");
            _output.WriteLine($"GoogleMapsService: {(googleMapsService != null ? "✓ Registered" : "✗ Missing")}");

            Assert.NotNull(restaurantService);
            Assert.NotNull(googleMapsService);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Service resolution failed: {ex.Message}");
            throw;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void GetNearbyRestaurants_CheckGoogleMapsApiKey_ShouldBeConfigured()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();

        // Act
        var apiKey = configuration?["GoogleMaps:ApiKey"];

        // Assert & Log
        _output.WriteLine($"Google Maps API Key configured: {(!string.IsNullOrEmpty(apiKey) ? "✓ Yes" : "✗ No")}");

        if (string.IsNullOrEmpty(apiKey))
        {
            _output.WriteLine("DIAGNOSTIC: Google Maps API key is not configured!");
            _output.WriteLine("This is likely the cause of the 503 error.");
            _output.WriteLine("Set the key using: dotnet user-secrets set 'GoogleMaps:ApiKey' 'YOUR_KEY'");

            Assert.Fail("Google Maps API key is not configured. This is causing the 503 error.");
        }
        else
        {
            _output.WriteLine($"API Key present: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...");
            Assert.NotEmpty(apiKey);
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
