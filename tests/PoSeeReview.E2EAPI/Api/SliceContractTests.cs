using System.Net;
using System.Net.Http.Json;
using PoSeeReview.Api;
using PoSeeReview.Shared.Dtos;
using Xunit;

namespace PoSeeReview.E2EAPI.Api;

/// <summary>
/// Request/response contract for the Leaderboard, Restaurants and Takedowns slices.
/// Authenticated via the Test-only FakeAuth scheme (NET_RULES 4.4) so the real handlers run
/// without an interactive login.
/// </summary>
public class SliceContractTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fake-User", "slice-contract-user");
        return client;
    }

    // ── Leaderboard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLeaderboard_Default_Returns200WithRegion()
    {
        var response = await CreateAuthenticatedClient().GetAsync("/api/leaderboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(payload);
        Assert.Equal("US", payload.Region);
    }

    [Theory]
    [InlineData("not-a-region")]
    [InlineData("U")]
    [InlineData("123")]
    public async Task GetLeaderboard_InvalidRegion_Returns400(string region)
    {
        var response = await CreateAuthenticatedClient().GetAsync($"/api/leaderboard?region={region}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Restaurants ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNearby_MissingCoordinates_Returns400()
    {
        var response = await CreateAuthenticatedClient().GetAsync("/api/restaurants/nearby");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetNearby_ReturnsWellFormedEnvelope()
    {
        var payload = await CreateAuthenticatedClient()
            .GetFromJsonAsync<NearbyRestaurantsResponse>("/api/restaurants/nearby?latitude=47.6062&longitude=-122.3321");

        Assert.NotNull(payload);
        Assert.NotNull(payload.Restaurants);
        Assert.Equal(payload.Restaurants.Count, payload.TotalCount);
    }

    // ── Takedowns ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PostTakedown_WithoutApiKey_IsNeverAccepted()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/takedowns", new TakedownRequestDto
            {
                PlaceId = "ChIJTakedown123",
                Region = "US",
                ContactEmail = "owner@example.com",
                RequesterName = "Owner",
                Reason = "We do not consent to this content appearing on your platform."
            });

        // The key is deliberately absent from every appsettings file, so the filter fails
        // closed with 503; a configured-but-wrong key would yield 401. Never 2xx.
        Assert.True(
            response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Unauthorized,
            $"Expected 503 or 401, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task PostTakedown_WithWrongApiKey_IsNeverAccepted()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "definitely-not-the-key");

        var response = await client.PostAsJsonAsync("/api/takedowns", new TakedownRequestDto
        {
            PlaceId = "ChIJTakedown123",
            Region = "US",
            ContactEmail = "owner@example.com",
            RequesterName = "Owner",
            Reason = "We do not consent to this content appearing on your platform."
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Unauthorized,
            $"Expected 503 or 401, got {(int)response.StatusCode}");
    }

    // ── Dev sessions ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDevSession_IsAnonymous_AndReturnsSession()
    {
        var response = await factory.CreateClient().GetAsync("/api/devsession");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<DevSessionDto>();
        Assert.NotNull(session);
    }

    [Fact]
    public async Task PostAnonDevSession_MintsDistinctIdentities()
    {
        var client = factory.CreateClient();

        // Ids are drawn from a 900k-wide range, so a single pair could collide by chance.
        // Sampling five and requiring more than one distinct value makes the assertion
        // deterministic in practice without weakening what it checks.
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var session = await (await client.PostAsync("/api/devsession/anon", null))
                .Content.ReadFromJsonAsync<DevSessionDto>();
            Assert.NotNull(session);
            Assert.False(string.IsNullOrWhiteSpace(session.UserId));
            ids.Add(session.UserId);
        }

        Assert.True(ids.Distinct().Count() > 1, $"every minted id was identical: {ids[0]}");
    }
}
