using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace Po.SeeReview.IntegrationTests.Api;

/// <summary>
/// Integration tests for GET /api/leaderboard endpoint
/// Tests regional leaderboard retrieval with real Azure Table Storage (Azurite)
/// </summary>
public class LeaderboardEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly WebApplicationFactory<Program> _factory;

    public LeaderboardEndpointTests(
        WebApplicationFactory<Program> factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithValidRegion_ReturnsTop10Entries()
    {
        // Arrange
        var region = "US-WA-TEST";
        await SeedLeaderboardEntries(region, 15);

        // Act
        var response = await _client.GetAsync($"/api/leaderboard?region={region}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(content);
        Assert.NotNull(content.Entries);
        Assert.Equal(10, content.Entries.Count); // Default limit is 10
        Assert.Equal(region, content.Region);
        Assert.True(content.LastUpdated > DateTimeOffset.MinValue);

        // Verify descending order by score
        for (int i = 0; i < content.Entries.Count - 1; i++)
        {
            Assert.True(content.Entries[i].StrangenessScore >= content.Entries[i + 1].StrangenessScore,
                $"Entry {i} score should be >= entry {i + 1} score");
        }

        // Verify ranks are sequential
        for (int i = 0; i < content.Entries.Count; i++)
        {
            Assert.Equal(i + 1, content.Entries[i].Rank);
        }

        _output.WriteLine($"Retrieved {content.Entries.Count} leaderboard entries for {region}");
        _output.WriteLine($"Top score: {content.Entries[0].StrangenessScore:F2}");
        _output.WriteLine($"Top restaurant: {content.Entries[0].RestaurantName}");
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithCustomLimit_ReturnsRequestedCount()
    {
        // Arrange
        var region = "US-CA-TEST";
        var limit = 5;
        await SeedLeaderboardEntries(region, 10);

        // Act
        var response = await _client.GetAsync($"/api/leaderboard?region={region}&limit={limit}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(content);
        Assert.Equal(limit, content.Entries.Count);
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithFewerEntriesThanLimit_ReturnsAvailableEntries()
    {
        // Arrange
        var region = "US-MT-TEST";
        await SeedLeaderboardEntries(region, 3);

        // Act
        var response = await _client.GetAsync($"/api/leaderboard?region={region}&limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(content);
        Assert.Equal(3, content.Entries.Count);
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithEmptyRegion_UsesDefaultRegion()
    {
        // Act - Empty region parameter should use default "US"
        var response = await _client.GetAsync("/api/leaderboard?region=");

        // Assert - Should succeed with default region
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(content);
        Assert.Equal("US", content.Region); // Default region is US
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithInvalidRegionFormat_ReturnsBadRequest()
    {
        // Act - Invalid format (should be XX-XX-XXX)
        var response = await _client.GetAsync("/api/leaderboard?region=INVALID");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]

    [Trait("Category", "Integration")]
    public async Task GetLeaderboard_WithExcessiveLimit_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/leaderboard?region=US-WA-TEST&limit=100");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("US", 10)]
    [InlineData("CA", 25)]
    [InlineData("GB", 50)]
    public async Task GET_Leaderboard_Respects_Limit_Parameter_For_Each_Region(string region, int limit)
    {
        // Arrange
        await SeedLeaderboardEntries(region, limit + 10); // Create more than limit

        // Act
        var response = await _client.GetAsync($"/api/leaderboard?region={region}&limit={limit}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(result);
        Assert.True(result.Entries.Count <= limit, $"Expected max {limit} entries, got {result.Entries.Count}");
    }

    [Fact]
    public async Task GET_Leaderboard_Filters_By_Region_Only()
    {
        // Arrange - Create entries for multiple regions
        await SeedLeaderboardEntries("US", 5);
        await SeedLeaderboardEntries("CA", 5);

        // Act
        var response = await _client.GetAsync("/api/leaderboard?region=CA&limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        Assert.NotNull(result);
        Assert.Equal("CA", result.Region);
        
        // Verify all entries are from CA region only
        Assert.All(result.Entries, entry => Assert.Equal("CA", entry.Region));
    }

    private async Task SeedLeaderboardEntries(string region, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILeaderboardRepository>();

        for (int i = 0; i < count; i++)
        {
            var entry = new LeaderboardEntry
            {
                PlaceId = $"ChIJTestSeed{i:D3}",
                RestaurantName = $"Test Restaurant {i + 1}",
                Address = $"{i + 1} Test Street, Test City",
                Region = region,
                StrangenessScore = 100 - (i * 2), // Descending scores
                ComicBlobUrl = $"https://test.blob.core.windows.net/comic{i}.png",
                LastUpdated = DateTimeOffset.UtcNow
            };

            await repository.UpsertAsync(entry);
        }

        _output.WriteLine($"Seeded {count} entries for region {region}");
    }

    // Response DTO for deserialization
    private class LeaderboardResponse
    {
        public List<LeaderboardEntryDto> Entries { get; set; } = new();
        public string Region { get; set; } = string.Empty;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.MinValue;
    }
}
