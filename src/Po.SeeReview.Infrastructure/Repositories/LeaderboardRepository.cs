using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Entities;

namespace Po.SeeReview.Infrastructure.Repositories;

/// <summary>
/// Repository for leaderboard persistence with inverted RowKey for descending sort
/// Table Name: PoSeeReviewLeaderboard
/// PartitionKey: LEADERBOARD_{Region}
/// RowKey: {InvertedScore}_{PlaceId}
/// </summary>
public class LeaderboardRepository : ILeaderboardRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<LeaderboardRepository> _logger;
    private const string PartitionKeyPrefix = "LEADERBOARD";

    public LeaderboardRepository(
        IConfiguration configuration,
        ILogger<LeaderboardRepository> logger)
    {
        _logger = logger;

        var connectionString = configuration["AzureStorage:ConnectionString"]
            ?? throw new InvalidOperationException("Azure Storage connection string not configured");

        var tableName = configuration["AzureStorage:LeaderboardTableName"] ?? "PoSeeReviewLeaderboard";

        var tableServiceClient = new TableServiceClient(connectionString);
        _tableClient = tableServiceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();
    }

    /// <summary>
    /// Gets top N entries for a region, sorted by strangeness score descending
    /// Leverages inverted RowKey for efficient sorting
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetTopEntriesAsync(string region, int limit)
    {
        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region cannot be empty", nameof(region));

        if (limit < 1 || limit > 50)
            throw new ArgumentException("Limit must be between 1 and 50", nameof(limit));

        var partitionKey = $"{PartitionKeyPrefix}_{region}";

        _logger.LogInformation("Fetching top {Limit} entries for region {Region}", limit, region);

        try
        {
            var query = _tableClient.QueryAsync<LeaderboardEntity>(
                filter: $"PartitionKey eq '{partitionKey}'",
                maxPerPage: limit
            );

            var entries = new List<LeaderboardEntry>();
            var rank = 1;

            await foreach (var entity in query)
            {
                entries.Add(entity.ToDomain(rank++));

                if (entries.Count >= limit)
                    break;
            }

            _logger.LogInformation("Retrieved {Count} entries for region {Region}", entries.Count, region);
            return entries;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("No leaderboard entries found for region {Region}", region);
            return new List<LeaderboardEntry>();
        }
    }

    /// <summary>
    /// Gets a specific entry by placeId and region
    /// </summary>
    public async Task<LeaderboardEntry?> GetByPlaceIdAsync(string placeId, string region)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentException("PlaceId cannot be empty", nameof(placeId));

        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region cannot be empty", nameof(region));

        var partitionKey = $"{PartitionKeyPrefix}_{region}";

        try
        {
            // Query by PlaceId property (secondary filter)
            var query = _tableClient.QueryAsync<LeaderboardEntity>(
                filter: $"PartitionKey eq '{partitionKey}' and PlaceId eq '{placeId}'"
            );

            await foreach (var entity in query)
            {
                return entity.ToDomain(0); // Rank will be recalculated when needed
            }

            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Upserts a leaderboard entry
    /// If entry exists with different score, old entry is deleted and new one created
    /// (RowKey includes score, so score changes require delete+insert)
    /// </summary>
    public async Task UpsertAsync(LeaderboardEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.PlaceId))
            throw new ArgumentException("PlaceId is required", nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Region))
            throw new ArgumentException("Region is required", nameof(entry));

        // Check if entry exists with different score
        var existing = await GetByPlaceIdAsync(entry.PlaceId, entry.Region);

        if (existing != null && Math.Abs(existing.StrangenessScore - entry.StrangenessScore) > 0.01)
        {
            // Score changed - need to delete old entry (different RowKey)
            await DeleteAsync(entry.PlaceId, entry.Region);
        }

        var entity = LeaderboardEntity.FromDomain(entry);

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        _logger.LogInformation(
            "Upserted leaderboard entry for {PlaceId} in {Region} with score {Score}",
            entry.PlaceId, entry.Region, entry.StrangenessScore);
    }

    /// <summary>
    /// Deletes a leaderboard entry
    /// Must query first to find the RowKey (which includes score)
    /// </summary>
    public async Task DeleteAsync(string placeId, string region)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentException("PlaceId cannot be empty", nameof(placeId));

        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region cannot be empty", nameof(region));

        var partitionKey = $"{PartitionKeyPrefix}_{region}";

        try
        {
            // Find the entity by PlaceId to get its RowKey
            var query = _tableClient.QueryAsync<LeaderboardEntity>(
                filter: $"PartitionKey eq '{partitionKey}' and PlaceId eq '{placeId}'"
            );

            await foreach (var entity in query)
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                _logger.LogInformation("Deleted leaderboard entry {PlaceId} from {Region}", placeId, region);
                return;
            }

            _logger.LogWarning("Leaderboard entry {PlaceId} not found in {Region}", placeId, region);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted, ignore
            _logger.LogDebug("Entry {PlaceId} already deleted from {Region}", placeId, region);
        }
    }

    /// <summary>
    /// Deletes all leaderboard entries for a specific place ID across all regions
    /// Used for takedown requests
    /// </summary>
    public async Task DeleteByPlaceIdAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentException("PlaceId cannot be empty", nameof(placeId));

        _logger.LogInformation("Deleting all leaderboard entries for PlaceId {PlaceId}", placeId);

        try
        {
            // Query all partitions for this PlaceId
            var query = _tableClient.QueryAsync<LeaderboardEntity>(
                filter: $"PlaceId eq '{placeId}'"
            );

            var deleteCount = 0;
            await foreach (var entity in query)
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                deleteCount++;
                _logger.LogDebug(
                    "Deleted leaderboard entry for {PlaceId} from region partition {Partition}",
                    placeId,
                    entity.PartitionKey);
            }

            _logger.LogInformation(
                "Deleted {Count} leaderboard entry/entries for PlaceId {PlaceId}",
                deleteCount,
                placeId);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Error deleting leaderboard entries for PlaceId {PlaceId}",
                placeId);
            throw;
        }
    }
}
