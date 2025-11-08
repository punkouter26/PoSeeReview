using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Service for managing the global strangeness leaderboard
/// Business logic layer wrapping the repository
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly ILeaderboardRepository _repository;
    private readonly ILogger<LeaderboardService> _logger;

    // Minimum score threshold to appear on leaderboard
    private const int MinimumStrangenessScore = 20;

    public LeaderboardService(
        ILeaderboardRepository repository,
        ILogger<LeaderboardService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Gets top N comics for a region, with rank numbers assigned
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetTopComicsAsync(string region, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            _logger.LogWarning("GetTopComicsAsync called with empty region");
            throw new ArgumentException("Region cannot be empty", nameof(region));
        }

        if (limit < 1 || limit > 50)
        {
            _logger.LogWarning("GetTopComicsAsync called with invalid limit: {Limit}", limit);
            throw new ArgumentException("Limit must be between 1 and 50", nameof(limit));
        }

        _logger.LogInformation("Getting top {Limit} comics for region {Region}", limit, region);

        try
        {
            var entries = await _repository.GetTopEntriesAsync(region, limit);

            // Ranks are already assigned by repository during query
            _logger.LogInformation("Retrieved {Count} entries for region {Region}", entries.Count, region);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving leaderboard for region {Region}", region);
            throw;
        }
    }

    /// <summary>
    /// Upserts a leaderboard entry (only if score meets threshold)
    /// Automatically manages deletion of old entries when score changes
    /// </summary>
    public async Task UpsertEntryAsync(LeaderboardEntry entry)
    {
        if (entry == null)
        {
            _logger.LogWarning("UpsertEntryAsync called with null entry");
            throw new ArgumentNullException(nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.PlaceId))
        {
            _logger.LogWarning("UpsertEntryAsync called with empty PlaceId");
            throw new ArgumentException("PlaceId is required", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.Region))
        {
            _logger.LogWarning("UpsertEntryAsync called with empty Region");
            throw new ArgumentException("Region is required", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.RestaurantName))
        {
            _logger.LogWarning("UpsertEntryAsync called with empty RestaurantName");
            throw new ArgumentException("RestaurantName is required", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.ComicBlobUrl))
        {
            _logger.LogWarning("UpsertEntryAsync called with empty ComicBlobUrl");
            throw new ArgumentException("ComicBlobUrl is required", nameof(entry));
        }

        if (entry.StrangenessScore < 0 || entry.StrangenessScore > 100)
        {
            _logger.LogWarning("UpsertEntryAsync called with invalid score: {Score}", entry.StrangenessScore);
            throw new ArgumentException("StrangenessScore must be between 0 and 100", nameof(entry));
        }

        // Only add to leaderboard if score meets threshold
        if (entry.StrangenessScore < MinimumStrangenessScore)
        {
            _logger.LogInformation(
                "Skipping leaderboard entry for {PlaceId} - score {Score} below threshold {Threshold}",
                entry.PlaceId, entry.StrangenessScore, MinimumStrangenessScore);
            return;
        }

        try
        {
            // Set LastUpdated timestamp
            entry.LastUpdated = DateTime.UtcNow;

            await _repository.UpsertAsync(entry);

            _logger.LogInformation(
                "Upserted leaderboard entry: {PlaceId} ({RestaurantName}) in {Region} with score {Score}",
                entry.PlaceId, entry.RestaurantName, entry.Region, entry.StrangenessScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting leaderboard entry for {PlaceId}", entry.PlaceId);
            throw;
        }
    }
}
