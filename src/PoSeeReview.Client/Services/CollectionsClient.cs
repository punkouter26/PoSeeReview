using System.Net;
using System.Net.Http.Json;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// The outcome of trying to keep a comic. An enum rather than a bool because the three cases
/// need three different things said to the user, and "false" cannot tell them apart.
/// </summary>
public enum KeepOutcome
{
    Kept,

    /// <summary>The user is at their limit and has to forget one first.</summary>
    CollectionFull,

    /// <summary>There is no comic to keep, or the call failed.</summary>
    Failed
}

/// <summary>
/// Typed wrapper over <c>/api/collections</c> — the comics a signed-in user has chosen to keep.
/// <para>
/// Distinct from <see cref="ComicHistoryService"/>, which remembers what this browser has seen.
/// History is local, free and lossy; a collection is server-side, capped, and survives both the
/// 24-hour expiry and the device.
/// </para>
/// </summary>
public sealed class CollectionsClient(HttpClient httpClient, DevSessionClient devSessionClient)
{
    /// <summary>Reads the user collection. Returns null when it cannot be read.</summary>
    public async Task<KeptComicsResponse?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, "/api/collections");
            using var response = await httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.KeptComicsResponse, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Keeps a comic. Idempotent server-side, so a double tap is safe.</summary>
    public async Task<KeepOutcome> KeepAsync(string placeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, $"/api/collections/{placeId}");
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return KeepOutcome.Kept;
            }

            // The cap is the one failure the user can actually do something about, so it is the
            // one that gets its own outcome.
            return response.StatusCode == HttpStatusCode.Conflict
                ? KeepOutcome.CollectionFull
                : KeepOutcome.Failed;
        }
        catch (HttpRequestException)
        {
            return KeepOutcome.Failed;
        }
    }

    /// <summary>Removes a comic from the collection. Silent on failure — the list simply reloads.</summary>
    public async Task<bool> ForgetAsync(string placeId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Delete, $"/api/collections/{placeId}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        await devSessionClient.AttachStoredHeaderAsync(request);
        return request;
    }
}
