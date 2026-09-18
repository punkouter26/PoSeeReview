using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Client-side telemetry tracker for generation lifecycle.
/// </summary>
public sealed class AnalyticsService(ILogger<AnalyticsService> logger)
{
    private long? _generationStartedAt;

    public void Track(string step) => logger.LogDebug("Analytics track: {Step}", step);

    public void TrackGenerationStarted()
    {
        _generationStartedAt = Stopwatch.GetTimestamp();
        logger.LogDebug("Analytics: GenerationStarted");
    }

    public void TrackGenerationCompleted(bool fromCache)
    {
        if (_generationStartedAt is { } start)
        {
            var elapsed = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            logger.LogDebug("Analytics: GenerationCompleted (fromCache={FromCache}, elapsed={Elapsed}ms)", fromCache, elapsed);
            _generationStartedAt = null;
        }
    }

    public void TrackGenerationFailed()
    {
        _generationStartedAt = null;
        logger.LogDebug("Analytics: GenerationFailed");
    }

    public void TrackGenerationAbandoned()
    {
        if (_generationStartedAt is not null)
        {
            logger.LogDebug("Analytics: GenerationAbandoned");
            _generationStartedAt = null;
        }
    }
}

public static class FunnelSteps
{
    public const string AppOpened = "AppOpened";
    public const string LocationDenied = "LocationDenied";
    public const string LocationGranted = "LocationGranted";
    public const string SearchPerformed = "SearchPerformed";
    public const string ResultsShown = "ResultsShown";
    public const string RestaurantTapped = "RestaurantTapped";
    public const string ComicShared = "ComicShared";
    public const string ComicSaved = "ComicSaved";
}
