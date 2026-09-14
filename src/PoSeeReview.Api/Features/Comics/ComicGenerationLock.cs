using System.Collections.Concurrent;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// In-process, per-place exclusive lock — the "single flight" that stops duplicate paid work.
/// <para>
/// <b>What it is for.</b> The generation pipeline reads the cache, spends ten seconds and a
/// paid image call, then writes the cache. Two requests for one restaurant that arrive inside
/// that window both miss, and both pay: the second comic is discarded by the upsert. That is
/// pure waste, and it is the easiest waste to hit, because a double-tap on a phone or two
/// tabs is all it takes. Holding a per-place gate makes the second caller wait for the first,
/// re-read the cache, and find it.
/// </para>
/// <para>
/// <b>Deliberately in-process.</b> This is not a distributed lock and does not pretend to be
/// one: across App Service instances two callers can still both generate. Making it distributed
/// would mean a lease row per place, an ETag protocol, and a failure mode where a crashed
/// instance blocks a restaurant until the lease expires — a lot of machinery to remove a rare
/// duplicate, on an app that already caps the daily spend globally and per user. The in-process
/// gate removes the common case (one instance, one double-tap) at the cost of a dictionary.
/// </para>
/// </summary>
public sealed class ComicGenerationLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable> AcquireAsync(PlaceId placeId, CancellationToken cancellationToken)
    {
        var key = placeId.Value;
        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);

        return new Release(gate, key, _gates);
    }

    /// <summary>
    /// Releases the gate and drops it from the map, so an unbounded set of place ids does not
    /// become an unbounded dictionary.
    /// </summary>
    private sealed class Release(
        SemaphoreSlim gate,
        string key,
        ConcurrentDictionary<string, SemaphoreSlim> gates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();

            // Only drop the entry once nobody holds or awaits it. Removing it while a waiter is
            // still queued would let a later caller reach a *different* semaphore and run
            // alongside work this one is guarding — a duplicate is far cheaper than that, so
            // the check is on the count rather than on a flag.
            if (gate.CurrentCount == 1)
            {
                gates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
            }

            return ValueTask.CompletedTask;
        }
    }
}
