using System.Net;
using System.Net.Http.Json;
using PoSeeReview.Api;
using PoSeeReview.Shared.Dtos;
using Xunit;

namespace PoSeeReview.E2EAPI.Api;

/// <summary>
/// Contract tests for the ops surface (NET_RULES 3.2): <c>/health</c> (+ live/ready) and
/// <c>/diag</c>. Both are anonymous so platform probes can reach them, which makes the
/// masking guarantee on <c>/diag</c> a security boundary rather than a nicety.
/// </summary>
public class DiagnosticsContractTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    /// <summary>Keys whose VALUES must never be readable in a /diag response.</summary>
    private static readonly string[] SecretKeyFragments =
        ["apikey", "secret", "password", "connectionstring", "token"];

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_AreAnonymous_AndRespond(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        // Deny-by-default must not apply: an unauthenticated probe never sees 401/403.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsJsonPayloadWithStatus()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diag_IsAnonymous_AndReportsEnvironment()
    {
        var snapshot = await factory.CreateClient().GetFromJsonAsync<DiagnosticsSnapshotDto>("/diag");

        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Environment));
        Assert.NotEqual(default, snapshot.Timestamp);
    }

    [Fact]
    public async Task Diag_MasksSecretValues()
    {
        var snapshot = await factory.CreateClient().GetFromJsonAsync<DiagnosticsSnapshotDto>("/diag");
        Assert.NotNull(snapshot);

        var secretEntries = snapshot.Config
            .Where(c => SecretKeyFragments.Any(f => c.Key.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .ToList();

        // A masked value always carries the *** marker; a raw one would not.
        Assert.All(secretEntries, entry =>
            Assert.Contains("***", entry.Value!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diag_NeverEchoesTheConfiguredApiKeyVerbatim()
    {
        var body = await factory.CreateClient().GetStringAsync("/diag");

        // The factory injects this value; if masking regressed it would appear in full.
        Assert.DoesNotContain("test-key-12345", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-google-maps-key", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagMockStatus_ReportsActiveMocks()
    {
        var status = await factory.CreateClient().GetFromJsonAsync<MockStatusDto>("/diag/mock-status");

        Assert.NotNull(status);
        // The E2E factory registers fake comic + restaurant services as IMockable.
        Assert.True(status.IsMockActive);
        Assert.NotEmpty(status.ActiveMocks);
    }
}
