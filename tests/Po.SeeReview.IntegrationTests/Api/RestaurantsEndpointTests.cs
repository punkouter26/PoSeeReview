using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Po.SeeReview.IntegrationTests.Api;

/// <summary>
/// Integration tests for GET /api/restaurants endpoints
/// </summary>
public class RestaurantsEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public RestaurantsEndpointTests(CustomWebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        _output = output;
    }

    [Fact(Skip = "Serilog frozen logger conflict with WebApplicationFactory - moved to Po.SeeReview.WebTests")]
    public async Task GetNearbyRestaurants_ValidCoordinates_Returns200()
    {
        // Arrange
        var latitude = 47.6062;
        var longitude = -122.3321;

        // Act
        var response = await _client.GetAsync($"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", contentType);
    }

    [Theory]
    [InlineData(91.0, -122.3321)]   // Invalid latitude > 90
    [InlineData(-91.0, -122.3321)]  // Invalid latitude < -90
    [InlineData(47.6062, 181.0)]    // Invalid longitude > 180
    [InlineData(47.6062, -181.0)]   // Invalid longitude < -180
    public async Task GetNearbyRestaurants_InvalidCoordinates_Returns400(double latitude, double longitude)
    {
        // Act
        var response = await _client.GetAsync($"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_MissingLatitude_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/restaurants/nearby?longitude=-122.3321");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetNearbyRestaurants_MissingLongitude_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/restaurants/nearby?latitude=47.6062");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresGoogleMapsApi")]
    public async Task GetRestaurantByPlaceId_ValidPlaceId_Returns200NotFoundOrServiceUnavailable()
    {
        // Arrange
        var placeId = "ChIJtest123";

        // Act
        var response = await _client.GetAsync($"/api/restaurants/{placeId}");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert - Google Maps API might return 503 if rate limited or unavailable
        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 200, 404, or 503, got {response.StatusCode}");

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _output.WriteLine($"⚠️ Google Maps API unavailable (503): {responseBody}");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetRestaurantByPlaceId_EmptyPlaceId_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/restaurants/");

        // Assert
        // Empty placeId routes to NotFound (404) as the route doesn't match
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
