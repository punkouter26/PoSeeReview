using System.Net;
using System.Net.Http.Json;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.IntegrationTests.Api;

[Trait("Tier", "Integration")]
[Trait("Domain", "Api")]
[Trait("Suite", "DevSession")]
public class DevSessionEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DevSessionEndpointTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task PostAnonSession_InDevelopmentEnvironment_Returns200WithAnonUserId()
    {
        var response = await _client.PostAsync("/api/devsession/anon", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DevSessionDto>();
        Assert.NotNull(dto);
        Assert.StartsWith("ANON", dto.UserId, StringComparison.Ordinal);
        Assert.True(dto.IsAnonymous);
        Assert.True(dto.IsDevelopmentBypass);
    }

    [Fact]
    public async Task PostAnonSession_TwoCalls_ProduceDifferentUserIds()
    {
        var r1 = await _client.PostAsync("/api/devsession/anon", null);
        var r2 = await _client.PostAsync("/api/devsession/anon", null);

        var dto1 = await r1.Content.ReadFromJsonAsync<DevSessionDto>();
        var dto2 = await r2.Content.ReadFromJsonAsync<DevSessionDto>();

        Assert.NotEqual(dto1!.UserId, dto2!.UserId);
    }

    [Fact]
    public async Task GetCurrentSession_WithDevUserIdHeader_ReturnsMatchingUserId()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/devsession");
        request.Headers.Add("X-Dev-User-Id", "ANON999999");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DevSessionDto>();
        Assert.NotNull(dto);
        Assert.Equal("ANON999999", dto.UserId);
    }

    [Fact]
    public async Task GetCurrentSession_WithoutDevUserIdHeader_Returns200WithAnonymousOrEmpty()
    {
        var response = await _client.GetAsync("/api/devsession");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DevSessionDto>();
        Assert.NotNull(dto);
        // With no header and no auth, userId should be "anonymous" or similar
        Assert.False(string.IsNullOrWhiteSpace(dto.UserId));
    }

    [Fact]
    public async Task PostAnonSession_ResponseHasCreatedAtUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var response = await _client.PostAsync("/api/devsession/anon", null);
        var after = DateTime.UtcNow.AddSeconds(1);

        var dto = await response.Content.ReadFromJsonAsync<DevSessionDto>();
        Assert.NotNull(dto);
        Assert.InRange(dto.CreatedAtUtc, before, after);
    }
}
