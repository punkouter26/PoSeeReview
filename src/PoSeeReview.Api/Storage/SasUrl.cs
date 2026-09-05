namespace PoSeeReview.Api.Storage;

/// <summary>
/// Read-side helpers for the SAS URLs minted by <see cref="BlobStorageService"/>.
/// <para>
/// This lives in <c>Storage/</c> — the sanctioned cross-slice home — because both the Comics and
/// Leaderboard slices need it and slices may not reference each other. Each used to carry its own
/// private copy of the <c>se=</c> parsing, the 2-hour margin and the host grammar, and the copies
/// had silently drifted on one point: whether a URL with no SAS at all counts as stale. That
/// difference is a real policy choice, so it is now an explicit parameter rather than an accident
/// of which file you happened to read.
/// </para>
/// </summary>
internal static class SasUrl
{
    /// <summary>How close to expiry a SAS may get before it is worth re-minting.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromHours(2);

    /// <summary>
    /// Returns true when a URL's SAS <c>se</c> (signed expiry) is already past or falls within
    /// <see cref="RefreshMargin"/>.
    /// </summary>
    /// <param name="url">The blob URL to inspect; may or may not carry a SAS query string.</param>
    /// <param name="treatUnsignedAzureUrlAsStale">
    /// When true, a URL with no <c>se=</c> at all is reported stale if it points at a real Azure
    /// Blob endpoint. This self-heals URLs persisted unsigned — e.g. a transient User Delegation
    /// SAS failure at write time left a bare "https://{account}.blob.core.windows.net/..." that a
    /// private container rejects (broken image). Azurite and any other host sign differently, so
    /// they are never caught by this branch. Callers that also serve URLs they did not upload
    /// (seeded or external links) leave this false so those are passed through untouched.
    /// </param>
    public static bool IsExpiringSoon(string? url, bool treatUnsignedAzureUrlAsStale = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            var query = uri.Query;
            var seIdx = query.IndexOf("se=", StringComparison.OrdinalIgnoreCase);
            if (seIdx < 0)
            {
                return treatUnsignedAzureUrlAsStale
                       && uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase);
            }

            var seStart = seIdx + 3;
            var seEnd = query.IndexOf('&', seStart);
            var seValue = Uri.UnescapeDataString(seEnd >= 0 ? query[seStart..seEnd] : query[seStart..]);
            return DateTimeOffset.TryParse(seValue, out var expiry)
                   && expiry < DateTimeOffset.UtcNow.Add(RefreshMargin);
        }
        catch
        {
            return false; // Malformed URL: leave it to the caller rather than churn refreshes
        }
    }
}
