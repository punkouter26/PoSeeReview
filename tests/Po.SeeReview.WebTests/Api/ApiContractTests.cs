using System.Net;
using Xunit;

namespace Po.SeeReview.WebTests.Api;

/// <summary>
/// Fast in-memory API contract tests that intentionally avoid real Azure and Google dependencies.
/// </summary>
public class ApiContractTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiContractTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostComic_WithInvalidPlaceId_Returns404()
    {
        var response = await _client.PostAsync("/api/comics/invalid-place-id-123", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetComic_WithNonExistentComic_Returns404()
    {
        var response = await _client.GetAsync("/api/comics/ChIJNonExistentPlace123456789");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetNearbyRestaurants_ValidCoordinates_Returns200()
    {
        var response = await _client.GetAsync("/api/restaurants/nearby?latitude=47.6062&longitude=-122.3321");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
