using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Leaderboard;

/// <summary>
/// Service for managing the global strangeness leaderboard
/// Business logic layer wrapping the repository
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly ILeaderboardRepository _repository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<LeaderboardService> _logger;
    private readonly LeaderboardOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly HallOfFameRepository? _hallOfFame;

    /// <summary>Widest page the repository will return; used so filtering empty comics can still fill <c>limit</c>.</summary>
    private const int MaxProbeCount = 50;

    public LeaderboardService(
        ILeaderboardRepository repository,
        IBlobStorageService blobStorageService,
        ILogger<LeaderboardService> logger,
        IOptions<LeaderboardOptions> options,
        TimeProvider? timeProvider = null,
        HallOfFameRepository? hallOfFame = null)
    {
        _repository = repository;
        _blobStorageService = blobStorageService;
        _logger = logger;
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Optional so the archive is additive: unit tests that construct this service directly
        // keep compiling, and a deployment without the table simply records no archive rather
        // than failing every generation.
        _hallOfFame = hallOfFame;
    }

    /// <summary>
    /// Gets top N comics for a region, with rank numbers assigned
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetTopComicsAsync(RegionCode region, int limit = 10)
    {
        if (region.IsEmpty)
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
            // Probe the full page size so expired-blob rows (empty ComicBlobUrl after the
            // existence check) do not occupy the slots the Hall of Fame actually paints.
            var entries = await _repository.GetTopEntriesAsync(region, MaxProbeCount);

            // Refresh any SAS tokens that are expired or within 2 hours of expiry — run in parallel
            var sasRefreshTasks = entries
                .Where(e => SasUrl.IsExpiringSoon(e.ComicBlobUrl))
                .Select(async entry =>
                {
                    _logger.LogInformation("Refreshing expired SAS for leaderboard entry {PlaceId}", entry.PlaceId);
                    entry.ComicBlobUrl = await _blobStorageService.RefreshSasUrlAsync(entry.ComicBlobUrl);
                    await _repository.UpsertAsync(entry);
                });
            await Task.WhenAll(sasRefreshTasks);

            // Verify blob existence for hosted comic URLs whose SAS is still valid — run in parallel.
            // Seeded test URLs (and any non-container link) are left as-is; only `/comics/` paths
            // are ones this app uploaded and can answer Exists for.
            var blobCheckTasks = entries
                .Where(e => IsHostedComicUrl(e.ComicBlobUrl) && !SasUrl.IsExpiringSoon(e.ComicBlobUrl))
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

            var visible = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.ComicBlobUrl))
                .Take(limit)
                .ToList();

            for (var i = 0; i < visible.Count; i++)
            {
                visible[i].Rank = i + 1;
            }

            _logger.LogInformation("Retrieved {Count} entries with comics for region {Region}", visible.Count, region);

            return visible;
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

        if (entry.PlaceId.IsEmpty)
        {
            _logger.LogWarning("UpsertEntryAsync called with empty PlaceId");
            throw new ArgumentException("PlaceId is required", nameof(entry));
        }

        if (entry.Region.IsEmpty)
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
            // Hall of Fame tracks the PEAK score ever recorded for a place. A regeneration that
            // scores lower must not demote or evict the entry — keep the higher score and only
            // refresh the artwork so the card keeps rendering after old blobs are cleaned up.
            var existing = await _repository.GetByPlaceIdAsync(entry.PlaceId, entry.Region);
            if (existing != null && existing.StrangenessScore > entry.StrangenessScore)
            {
                existing.ComicBlobUrl = entry.ComicBlobUrl;
                existing.LastUpdated = _timeProvider.GetUtcNow();
                await _repository.UpsertAsync(existing);

                _logger.LogInformation(
                    "Kept peak score {ExistingScore} for {PlaceId} (new comic scored {NewScore}); refreshed artwork only",
                    existing.StrangenessScore, entry.PlaceId, entry.StrangenessScore);

                // Archive the peak, not the weaker comic that just ran: the week's record is
                // what this place actually achieved.
                await ArchiveQuietlyAsync(existing);
                return;
            }

            // Set LastUpdated timestamp
            entry.LastUpdated = _timeProvider.GetUtcNow();

            await _repository.UpsertAsync(entry);

            _logger.LogInformation(
                "Upserted leaderboard entry: {PlaceId} ({RestaurantName}) in {Region} with score {Score}",
                entry.PlaceId, entry.RestaurantName, entry.Region, entry.StrangenessScore);

            await ArchiveQuietlyAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting leaderboard entry for {PlaceId}", entry.PlaceId);
            throw;
        }
    }

    /// <summary>
    /// Files an entry into the permanent weekly archive.
    /// <para>
    /// Swallows its own failures on purpose. This runs inside the comic generation pipeline,
    /// and a decorative archive must never be the reason a user who just waited ten seconds
    /// and spent a generation gets an error instead of their comic.
    /// </para>
    /// </summary>
    private async Task ArchiveQuietlyAsync(LeaderboardEntry entry)
    {
        if (_hallOfFame is null)
        {
            return;
        }

        try
        {
            await _hallOfFame.ArchiveAsync(entry, _timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not archive {PlaceId} into the hall of fame", entry.PlaceId);
        }
    }

    /// <summary>
    /// True when the URL points at a blob in this app's comics container, so <see cref="IBlobStorageService.BlobExistsAsync"/>
    /// can actually answer. Other hosts (including integration-test seeds) are not ours to probe.
    /// </summary>
    private static bool IsHostedComicUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            return new Uri(url).AbsolutePath.Contains("/comics/", StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

}

