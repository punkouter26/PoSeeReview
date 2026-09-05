using System.Diagnostics;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Reports the funnel steps that only the browser can see.
/// <para>
/// The server can count comics it generated. It cannot count a user who denied location, ran a
/// search and gave up, or closed the tab eight seconds into a generation — and those are exactly
/// the numbers the PRD's success metrics are made of. This service is the browser-side half.
/// </para>
/// <para>
/// Every method is fire-and-forget and swallows everything. A funnel ping that can delay a tap,
/// or surface an error to a user, is worse than no measurement at all.
/// </para>
/// </summary>
public sealed class AnalyticsService(ApiClient apiClient)
{
    /// <summary>
    /// Timestamp of the generation currently in flight, so completion can be reported with a
    /// real client-observed duration rather than the server's view of its own pipeline.
    /// </summary>
    private long? _generationStartedAt;

    /// <summary>Reports a step with no duration.</summary>
    public void Track(string step) => FireAndForget(step, null);

    /// <summary>Marks the start of a generation so the next completion can be timed.</summary>
    public void TrackGenerationStarted()
    {
        _generationStartedAt = Stopwatch.GetTimestamp();
        FireAndForget(FunnelSteps.GenerationStarted, null);
    }

    /// <summary>
    /// Reports a finished generation with the wall-clock time the user actually waited.
    /// <para>
    /// A cache hit is reported as its own step and deliberately carries no duration: folding a
    /// sub-second cache read into the same average as a ten-second pipeline would make
    /// time-to-first-comic look good by measuring the wrong thing.
    /// </para>
    /// </summary>
    public void TrackGenerationCompleted(bool fromCache)
    {
        var elapsed = TakeElapsedMs();

        if (fromCache)
        {
            FireAndForget(FunnelSteps.CacheHit, null);
            return;
        }

        FireAndForget(FunnelSteps.GenerationCompleted, elapsed);
    }

    /// <summary>Reports a generation that failed after starting.</summary>
    public void TrackGenerationFailed()
    {
        TakeElapsedMs();
        FireAndForget(FunnelSteps.GenerationFailed, null);
    }

    /// <summary>
    /// Reports a generation the user walked away from. Called when the comic page is disposed
    /// with a generation still running — the abandon rate is the single clearest signal that
    /// the wait is too long.
    /// </summary>
    public void TrackGenerationAbandoned()
    {
        if (_generationStartedAt is null)
        {
            return;
        }

        TakeElapsedMs();
        FireAndForget(FunnelSteps.GenerationAbandoned, null);
    }

    /// <summary>Consumes the pending start timestamp and returns the elapsed milliseconds.</summary>
    private int? TakeElapsedMs()
    {
        if (_generationStartedAt is not { } start)
        {
            return null;
        }

        _generationStartedAt = null;
        return (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>
    /// Posts without awaiting. The discard is the point: callers are UI event handlers, and a
    /// telemetry round trip must not sit between a tap and what it does.
    /// </summary>
    private void FireAndForget(string step, int? durationMs) =>
        _ = apiClient.RecordFunnelEventAsync(step, durationMs);
}
