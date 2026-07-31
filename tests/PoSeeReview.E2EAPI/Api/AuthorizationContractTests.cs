using System.Net;
using PoSeeReview.Api;
using Xunit;

namespace PoSeeReview.E2EAPI.Api;

/// <summary>
/// Contract tests for the deny-by-default authorization policy (NET_RULES 4.1/4.5): every
/// endpoint requires a session unless it explicitly opts out with AllowAnonymous.
/// </summary>
public class AuthorizationContractTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/comics/ChIJTest123")]
    [InlineData("/api/restaurants/nearby?latitude=47.6&longitude=-122.3")]
    [InlineData("/api/leaderboard")]
    public async Task BusinessEndpoints_WithoutSession_Are401(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/auth/me")]
    [InlineData("/api/devsession")]
    [InlineData("/diag")]
    [InlineData("/health")]
    public async Task AnonymousEndpoints_WithoutSession_AreNot401(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SpaFallback_IsReachableAnonymously()
    {
        // Regression guard: the fallback must stay a real endpoint carrying AllowAnonymous,
        // otherwise the deny-by-default policy 401s "/" and the login flow is unreachable.
        var response = await factory.CreateClient().GetAsync("/some/client/route");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_RatherThanSpaShell()
    {
        var response = await factory.CreateClient().GetAsync("/api/definitely-not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FakeAuthHeader_GrantsAccessToDeniedEndpoint()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fake-User", "authz-contract-user");

        var response = await client.GetAsync("/api/leaderboard");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
