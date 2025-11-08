using System.Net;
using Xunit;

namespace Po.SeeReview.WebTests.Api;

/// <summary>
/// Web integration tests for GET /api/restaurants endpoints
/// Using CustomWebApplicationFactory to avoid Serilog frozen logger issues
/// </summary>
public class RestaurantsEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RestaurantsEndpointTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "RequiresGoogleMapsApi")]
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
}
