using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Analytics;

/// <summary>
/// Daily funnel counters. Owned by the Analytics slice (NET_RULES 2.2).
/// </summary>
public sealed class FunnelRepository(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<FunnelRepository> logger)
{
    /// <summary>Optimistic-concurrency attempts before an event is dropped.</summary>
    private const int MaxConcurrencyRetries = 4;

    private readonly TableClient _table =
        tableServiceClient.GetTableClient(options.Value.AnalyticsTableName);

    /// <summary>
    /// Adds one to a step's daily count, optionally folding in a duration sample.
    /// <para>
    /// Dropping an event on sustained contention is deliberate. These are approximate product
    /// counters, and blocking a user's page on a telemetry write would be a far worse bug than
    /// a slightly low number on an internal dashboard.
    /// </para>
    /// </summary>
    public async Task RecordAsync(
        DateOnly day,
        string step,
        int? durationMs,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = FunnelCounterEntity.PartitionKeyFor(day);

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            try
            {
                var existing = await TryGetAsync(partitionKey, step, cancellationToken);

                if (existing is null)
                {
                    var created = new FunnelCounterEntity
                    {
                        PartitionKey = partitionKey,
                        RowKey = step,
                        Count = 1,
                        DurationSumMs = durationMs ?? 0,
                        DurationSamples = durationMs is null ? 0 : 1
                    };

                    await _table.AddEntityAsync(created, cancellationToken);
                    return;
                }

                existing.Count++;
                if (durationMs is { } ms)
                {
                    existing.DurationSumMs += ms;
                    existing.DurationSamples++;
                }

                await _table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, cancellationToken);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Concurrent reporter; re-read and retry.
            }
        }

        logger.LogDebug("Dropped a contended funnel event for step {Step}", step);
    }

    /// <summary>Reads a day's counters and derives the rates the PRD actually set targets for.</summary>
    public async Task<FunnelSnapshotDto> GetSnapshotAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var partitionKey = FunnelCounterEntity.PartitionKeyFor(day);
        var filter = TableClient.CreateQueryFilter<FunnelCounterEntity>(e => e.PartitionKey == partitionKey);

        var snapshot = new FunnelSnapshotDto { Date = day };
        long durationSum = 0;
        var durationSamples = 0;

        await foreach (var entity in _table.QueryAsync<FunnelCounterEntity>(filter, cancellationToken: cancellationToken))
        {
            snapshot.Counts[entity.RowKey] = entity.Count;

            if (entity.RowKey == FunnelSteps.GenerationCompleted)
            {
                durationSum = entity.DurationSumMs;
                durationSamples = entity.DurationSamples;
            }
        }

        var granted = Count(snapshot, FunnelSteps.LocationGranted);
        var denied = Count(snapshot, FunnelSteps.LocationDenied);
        var tapped = Count(snapshot, FunnelSteps.RestaurantTapped);
        var started = Count(snapshot, FunnelSteps.GenerationStarted);
        var completed = Count(snapshot, FunnelSteps.GenerationCompleted);
        var failed = Count(snapshot, FunnelSteps.GenerationFailed);
        var abandoned = Count(snapshot, FunnelSteps.GenerationAbandoned);
        var cacheHits = Count(snapshot, FunnelSteps.CacheHit);
        var shared = Count(snapshot, FunnelSteps.ComicShared);

        snapshot.LocationGrantRate = Rate(granted, granted + denied);

        // Both sides are tapped-flow-only events. Dividing all delivered comics by taps — what
        // this did first — reports over 100%, because a comic opened from a shared link or the
        // Hall of Fame never had a tap in front of it.
        snapshot.TapThroughRate = Rate(started, tapped);

        // Denominator is the outcomes actually observed rather than `started`: a generation still
        // running when the day is read has no outcome yet, and counting it as a failure would
        // make the rate sag every time someone is mid-wait.
        snapshot.GenerationCompletionRate = Rate(completed, completed + failed + abandoned);

        // Cache hits are counted against every comic the user actually received, which is the
        // denominator the PRD's ">40% cache hit rate" target means.
        snapshot.CacheHitRate = Rate(cacheHits, completed + cacheHits);
        snapshot.ShareRate = Rate(shared, completed + cacheHits);

        snapshot.AverageGenerationMs = durationSamples > 0
            ? Math.Round(durationSum / (double)durationSamples, 1)
            : null;

        return snapshot;
    }

    private static int Count(FunnelSnapshotDto snapshot, string step) =>
        snapshot.Counts.TryGetValue(step, out var value) ? value : 0;

    /// <summary>Percentage, or null when the denominator is zero — an absent rate is honest, 0% is not.</summary>
    private static double? Rate(int numerator, int denominator) =>
        denominator <= 0 ? null : Math.Round(numerator * 100.0 / denominator, 1);

    private async Task<FunnelCounterEntity?> TryGetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _table.GetEntityAsync<FunnelCounterEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
