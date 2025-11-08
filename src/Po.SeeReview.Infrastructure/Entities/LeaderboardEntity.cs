using Azure;
using Azure.Data.Tables;
using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Infrastructure.Entities;

/// <summary>
/// Azure Table Storage entity for leaderboard with inverted RowKey for descending sort
/// PartitionKey: LEADERBOARD_{Region}
/// RowKey: {InvertedScore}_{PlaceId} (inverted for descending sort by score)
/// </summary>
public class LeaderboardEntity : ITableEntity
{
    /// <summary>
    /// Partition key format: LEADERBOARD_{Region}
    /// Example: LEADERBOARD_US-WA-Seattle
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key format: {InvertedScore}_{PlaceId}
    /// InvertedScore = 9999999999 - (score * 100000000) for descending sort
    /// Example: 0000000500_ChIJabc123 (for score 95.00)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// Azure Table Storage timestamp
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Azure Table Storage ETag for optimistic concurrency
    /// </summary>
    public ETag ETag { get; set; }

    // Business properties

    /// <summary>
    /// Google Maps Place ID
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name
    /// </summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// Full address
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Geographic region code (e.g., US-WA-Seattle)
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Strangeness score (0-100)
    /// </summary>
    public double StrangenessScore { get; set; }

    /// <summary>
    /// URL to comic image in Azure Blob Storage
    /// </summary>
    public string ComicBlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Converts domain LeaderboardEntry to Table Storage entity
    /// </summary>
    public static LeaderboardEntity FromDomain(LeaderboardEntry entry)
    {
        var invertedScore = CalculateInvertedScore(entry.StrangenessScore);

        return new LeaderboardEntity
        {
            PartitionKey = $"LEADERBOARD_{entry.Region}",
            RowKey = $"{invertedScore:D10}_{entry.PlaceId}",
            PlaceId = entry.PlaceId,
            RestaurantName = entry.RestaurantName,
            Address = entry.Address,
            Region = entry.Region,
            StrangenessScore = entry.StrangenessScore,
            ComicBlobUrl = entry.ComicBlobUrl,
            LastUpdated = entry.LastUpdated
        };
    }

    /// <summary>
    /// Converts Table Storage entity to domain LeaderboardEntry
    /// </summary>
    public LeaderboardEntry ToDomain(int rank)
    {
        return new LeaderboardEntry
        {
            Rank = rank,
            PlaceId = PlaceId,
            RestaurantName = RestaurantName,
            Address = Address,
            Region = Region,
            StrangenessScore = StrangenessScore,
            ComicBlobUrl = ComicBlobUrl,
            LastUpdated = LastUpdated
        };
    }

    /// <summary>
    /// Calculates inverted score for RowKey to enable descending sort
    /// Score is scaled by 100000000 to handle decimal precision
    /// Returns: 9999999999 - (score * 100000000)
    /// </summary>
    private static long CalculateInvertedScore(double score)
    {
        var scaledScore = (long)Math.Floor(score * 100000000.0);
        return 9999999999 - scaledScore;
    }
}
