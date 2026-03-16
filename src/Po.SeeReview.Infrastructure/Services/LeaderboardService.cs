using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Configuration;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Service for managing the global strangeness leaderboard
/// Business logic layer wrapping the repository
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly ILeaderboardRepository _repository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<LeaderboardService> _logger;
    private readonly ComicOptions _options;

    public LeaderboardService(
        ILeaderboardRepository repository,
        IBlobStorageService blobStorageService,
        ILogger<LeaderboardService> logger,
        IOptions<ComicOptions> options)
    {
        _repository = repository;
        _blobStorageService = blobStorageService;
        _logger = logger;
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
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

            // Refresh any SAS tokens that are expired or within 2 hours of expiry — run in parallel
            var sasRefreshTasks = entries
                .Where(e => IsSasExpiringSoon(e.ComicBlobUrl))
                .Select(async entry =>
                {
                    _logger.LogInformation("Refreshing expired SAS for leaderboard entry {PlaceId}", entry.PlaceId);
                    entry.ComicBlobUrl = await _blobStorageService.RefreshSasUrlAsync(entry.ComicBlobUrl);
                    await _repository.UpsertAsync(entry);
                });
            await Task.WhenAll(sasRefreshTasks);

            // Verify blob existence for entries whose SAS is still valid — run in parallel
            var blobCheckTasks = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.ComicBlobUrl) && !IsSasExpiringSoon(e.ComicBlobUrl))
                .Select(async entry =>
                {
                    if (!await _blobStorageService.BlobExistsAsync(entry.ComicBlobUrl))
                    {
                        _logger.LogWarning("LeaderboardEntry {PlaceId} references a deleted blob — clearing ComicBlobUrl", entry.PlaceId);
                        entry.ComicBlobUrl = string.Empty;
                        await _repository.UpsertAsync(entry);
                    }
                });
            await Task.WhenAll(blobCheckTasks);

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
        if (entry.StrangenessScore < _options.MinimumStrangenessScore)
        {
            _logger.LogInformation(
                "Skipping leaderboard entry for {PlaceId} - score {Score} below threshold {Threshold}",
                entry.PlaceId, entry.StrangenessScore, _options.MinimumStrangenessScore);
            return;
        }

        try
        {
            // Set LastUpdated timestamp
            entry.LastUpdated = DateTimeOffset.UtcNow;

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

    /// <summary>
    /// Returns true when a SAS URL's <c>se</c> (signed expiry) parameter is already past or within 2 hours.
    /// </summary>
    private static bool IsSasExpiringSoon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var query = new Uri(url).Query;
            var seIdx = query.IndexOf("se=", StringComparison.OrdinalIgnoreCase);
            if (seIdx < 0) return false;

            var seStart = seIdx + 3;
            var seEnd = query.IndexOf('&', seStart);
            var seValue = Uri.UnescapeDataString(seEnd >= 0 ? query[seStart..seEnd] : query[seStart..]);
            return DateTimeOffset.TryParse(seValue, out var expiry)
                   && expiry < DateTimeOffset.UtcNow.AddHours(2);
        }
        catch
        {
            return false;
        }
    }
}
