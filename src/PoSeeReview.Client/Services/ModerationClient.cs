using System.Net.Http.Json;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Typed wrapper over <c>/api/moderation</c>.
/// <para>
/// Every call here surfaces its failure rather than swallowing it. Elsewhere in this app a
/// failed read degrades quietly because the thing being read is decoration; a moderator told
/// "done" when nothing was written would leave content up believing they had taken it down.
/// </para>
/// </summary>
public sealed class ModerationClient(HttpClient httpClient, DevSessionClient devSessionClient)
{
    public async Task<ModerationQueueDto> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/api/moderation/queue");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ModerationQueueDto, cancellationToken)
            ?? new ModerationQueueDto();
    }

    public Task HideAsync(string placeId, string reason, CancellationToken cancellationToken = default) =>
        ActAsync(HttpMethod.Post, $"/api/moderation/{placeId}/hide", reason, cancellationToken);

    public Task RestoreAsync(string placeId, string reason, CancellationToken cancellationToken = default) =>
        ActAsync(HttpMethod.Post, $"/api/moderation/{placeId}/restore", reason, cancellationToken);

    public Task SuppressAsync(string placeId, string reason, CancellationToken cancellationToken = default) =>
        ActAsync(HttpMethod.Post, $"/api/moderation/{placeId}/suppress", reason, cancellationToken);

    /// <summary>Erases the comic everywhere it survives and suppresses the place. Not reversible.</summary>
    public Task RemoveAsync(string placeId, string reason, CancellationToken cancellationToken = default) =>
        ActAsync(HttpMethod.Delete, $"/api/moderation/{placeId}", reason, cancellationToken);

    private async Task ActAsync(HttpMethod method, string url, string reason, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, url);
        request.Content = JsonContent.Create(
            new ModerationActionDto { Reason = reason }, AppJsonContext.Default.ModerationActionDto);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        await devSessionClient.AttachStoredHeaderAsync(request);
        return request;
    }
}
