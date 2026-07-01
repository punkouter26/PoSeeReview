using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PoSeeReview.Shared.Dtos;
using Xunit;

namespace PoSeeReview.E2EAPI.Api;

/// <summary>
/// Contract tests for the BFF auth slice (NET_RULES 4.4): /auth/me state, FakeAuth
/// header mapping, guest cookie login, and open-redirect sanitization.
/// </summary>
public class AuthEndpointsTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private HttpClient CreateHttpsClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task AuthMe_Unauthenticated_ReturnsAnonymousState()
    {
        var state = await CreateHttpsClient().GetFromJsonAsync<AuthStateDto>("/auth/me");

        Assert.NotNull(state);
        Assert.False(state.IsAuthenticated);
        Assert.Null(state.UserId);
    }

    [Fact]
    public async Task AuthMe_WithFakeHeaders_MapsClaimsPrincipal()
    {
        var client = CreateHttpsClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Add("X-Fake-User", "test-user-42");
        request.Headers.Add("X-Fake-Roles", "Admin, Tester");

        var response = await client.SendAsync(request);
        var state = await response.Content.ReadFromJsonAsync<AuthStateDto>();

        Assert.NotNull(state);
        Assert.True(state.IsAuthenticated);
        Assert.Equal("test-user-42", state.UserId);
        Assert.Contains("Admin", state.Roles);
        Assert.Contains("Tester", state.Roles);
    }

    [Fact]
    public async Task FakeLogin_SetsBffCookie_AndRedirectsToLocalReturnUrl()
    {
        var response = await CreateHttpsClient().GetAsync("/auth/login/fake?returnUrl=/leaderboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/leaderboard", response.Headers.Location?.ToString());
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".PoSeeReview.Auth"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public async Task FakeLogin_NonLocalReturnUrl_CollapsesToRoot(string returnUrl)
    {
        var response = await CreateHttpsClient().GetAsync($"/auth/login/fake?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Logout_RedirectsToLogin()
    {
        var response = await CreateHttpsClient().GetAsync("/auth/logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.ToString());
    }
}
