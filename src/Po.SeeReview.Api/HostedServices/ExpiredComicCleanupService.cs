using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Api.HostedServices;

/// <summary>
/// Periodically purges expired comics and their associated blobs
/// to control storage costs and respect 24-hour cache policy.
/// </summary>
public class ExpiredComicCleanupService : BackgroundService
{
    private const int DefaultBatchSize = 25;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredComicCleanupService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    public ExpiredComicCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredComicCleanupService> logger,
        TelemetryClient telemetryClient,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        var intervalMinutes = configuration.GetValue("Cleanup:ExpiredComicIntervalMinutes", (int)DefaultInterval.TotalMinutes);
        _interval = intervalMinutes <= 0
            ? DefaultInterval
            : TimeSpan.FromMinutes(Math.Min(intervalMinutes, 720));

        _batchSize = Math.Clamp(configuration.GetValue("Cleanup:ExpiredComicBatchSize", DefaultBatchSize), 5, 200);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay gives the application time to finish bootstrapping
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredComicsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Ignore cancellation requests during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure during expired comic cleanup");
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    ["Component"] = nameof(ExpiredComicCleanupService)
                });
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CleanupExpiredComicsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var comicRepository = scope.ServiceProvider.GetRequiredService<IComicRepository>();
        var blobStorageService = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        var totalDeleted = 0;
        var iterationStopwatch = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested)
        {
            var expiredComics = await comicRepository.GetExpiredComicsAsync(DateTimeOffset.UtcNow, _batchSize, cancellationToken);

            if (expiredComics.Count == 0)
            {
                break;
            }

            foreach (var comic in expiredComics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await comicRepository.DeleteAsync(comic.PlaceId);

                    if (!string.IsNullOrWhiteSpace(comic.Id))
                    {
                        await blobStorageService.DeleteComicImageAsync(comic.Id);
                    }

                    totalDeleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete expired comic {ComicId} for {PlaceId}", comic.Id, comic.PlaceId);
                    _telemetryClient.TrackException(ex, new Dictionary<string, string>
                    {
                        ["ComicId"] = comic.Id,
                        ["PlaceId"] = comic.PlaceId
                    });
                }
            }

            if (expiredComics.Count < _batchSize)
            {
                break;
            }
        }

        iterationStopwatch.Stop();

        if (totalDeleted > 0)
        {
            _logger.LogInformation("Expired comic cleanup removed {Count} records in {Duration}ms", totalDeleted, iterationStopwatch.Elapsed.TotalMilliseconds);
        }

        _telemetryClient.GetMetric("Comics.Expired.Purged").TrackValue(totalDeleted);
        _telemetryClient.GetMetric("Comics.Expired.CleanupDurationMs").TrackValue(iterationStopwatch.Elapsed.TotalMilliseconds);
    }
}
