using System.Net;
using PoSeeReview.Api;
using Xunit;

namespace PoSeeReview.E2EAPI.Api;

/// <summary>
/// Fast in-memory API contract tests that intentionally avoid real Azure and Google dependencies.
/// </summary>
public class ApiContractTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiContractTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        // Business endpoints deny by default (NET_RULES 4.1/4.5); authenticate via the
        // Test-only FakeAuth scheme so these contract calls exercise the real handlers.
        _client.DefaultRequestHeaders.Add("X-Fake-User", "contract-test-user");
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

}
