using Po.SeeReview.Core.Entities;
using Xunit;

namespace Po.SeeReview.UnitTests.Repositories;

public sealed class LeaderboardRepositoryTests
{
    [Fact]
    public void InvertedRowKey_WithScore98_ReturnsCorrectFormat()
    {
        var score = 98.0;
        var placeId = "ChIJTest123";

        var invertedScore = 9999999999 - (long)Math.Floor(score * 100000000.0);
        var rowKey = $"{invertedScore:D10}_{placeId}";

        Assert.Contains(placeId, rowKey);
        Assert.True(rowKey.Length > 10);
    }

    [Fact]
    public void InvertedRowKey_HigherScores_SortBeforeLowerScores()
    {
        var score98 = CreateRowKey(98.0, "ChIJA");
        var score85 = CreateRowKey(85.0, "ChIJB");
        var score50 = CreateRowKey(50.0, "ChIJC");

        Assert.True(string.CompareOrdinal(score98, score85) < 0);
        Assert.True(string.CompareOrdinal(score85, score50) < 0);
        Assert.True(string.CompareOrdinal(score98, score50) < 0);
    }

    [Fact]
    public void PartitionKey_WithRegion_ReturnsCorrectFormat()
    {
        var region = "US-WA-Seattle";
        var expected = "LEADERBOARD_US-WA-Seattle";

        var partitionKey = $"LEADERBOARD_{region}";

        Assert.Equal(expected, partitionKey);
    }

    [Theory]
    [InlineData("US-CA-SF")]
    [InlineData("US-NY-NYC")]
    [InlineData("UK-EN-London")]
    public void PartitionKey_WithVariousRegions_FormatsCorrectly(string region)
    {
        var partitionKey = $"LEADERBOARD_{region}";

        Assert.StartsWith("LEADERBOARD_", partitionKey);
        Assert.Contains(region, partitionKey);
    }

    [Fact]
    public void LeaderboardEntry_WithRequiredFields_CreatesValidEntity()
    {
        var entry = new LeaderboardEntry
        {
            Rank = 1,
            PlaceId = "ChIJTest",
            RestaurantName = "Test Place",
            Address = "123 Main St",
            Region = "US-WA-SEA",
            StrangenessScore = 95.5,
            ComicBlobUrl = "https://blob.core.windows.net/test.png",
            LastUpdated = DateTimeOffset.UtcNow
        };

        Assert.Equal(1, entry.Rank);
        Assert.Equal("ChIJTest", entry.PlaceId);
        Assert.InRange(entry.StrangenessScore, 0, 100);
    }

    [Fact]
    public void RowKey_WithSameScoreDifferentPlaceIds_CreatesUniqueKeys()
    {
        var score = 95.0;
        var placeId1 = "ChIJAbc123";
        var placeId2 = "ChIJXyz789";

        var rowKey1 = CreateRowKey(score, placeId1);
        var rowKey2 = CreateRowKey(score, placeId2);

        Assert.NotEqual(rowKey1, rowKey2);
        Assert.StartsWith(CreateInvertedScore(score), rowKey1);
        Assert.StartsWith(CreateInvertedScore(score), rowKey2);
    }

    private static string CreateRowKey(double score, string placeId)
    {
        var invertedScore = CreateInvertedScore(score);
        return $"{invertedScore}_{placeId}";
    }

    private static string CreateInvertedScore(double score)
    {
        var scaledScore = (long)Math.Floor(score * 100000000.0);
        var inverted = 9999999999 - scaledScore;
        return inverted.ToString("D10");
    }
}
