using Azure;

namespace PoSeeReview.Api.Storage;

/// <summary>
/// Retry for Table Storage optimistic concurrency: a 409 (a concurrent create) or 412 (a stale
/// ETag) means another writer got there first, so the operation re-reads and re-decides.
/// </summary>
internal static class TableConcurrency
{
    public static bool IsConflict(RequestFailedException ex) => ex.Status is 409 or 412;

    /// <summary>Retries on conflict up to <paramref name="maxAttempts"/> times; the last conflict propagates.</summary>
    public static async Task RetryAsync(int maxAttempts, Func<Task> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (RequestFailedException ex) when (IsConflict(ex) && attempt < maxAttempts)
            {
            }
        }
    }

    /// <summary>
    /// Retries on conflict up to <paramref name="maxAttempts"/> times, then returns
    /// <paramref name="onExhausted"/> instead of throwing.
    /// </summary>
    public static async Task<T> RetryAsync<T>(int maxAttempts, Func<Task<T>> operation, Func<T> onExhausted)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (RequestFailedException ex) when (IsConflict(ex))
            {
            }
        }

        return onExhausted();
    }
}
