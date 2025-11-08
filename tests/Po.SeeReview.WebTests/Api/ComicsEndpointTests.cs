using System.Net;
using System.Net.Http.Json;
using Po.SeeReview.Shared.Dtos;
using Xunit;

namespace Po.SeeReview.WebTests.Api;

/// <summary>
/// Web integration tests for /api/comics endpoints
/// Using CustomWebApplicationFactory to avoid Serilog frozen logger issues
/// </summary>
public class ComicsEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ComicsEndpointTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostComic_WithInvalidPlaceId_Returns404()
    {
        // Arrange
        var invalidPlaceId = "invalid-place-id-123";

        // Act
        var response = await _client.PostAsync($"/api/comics/{invalidPlaceId}", null);

        // Assert
        // API returns 404 when restaurant is not found (which is correct behavior)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetComic_WithNonExistentComic_Returns404()
    {
        // Arrange
        var nonExistentPlaceId = "ChIJNonExistentPlace123456789";

        // Act
        var response = await _client.GetAsync($"/api/comics/{nonExistentPlaceId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
