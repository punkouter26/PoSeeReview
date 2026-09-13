using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using PoSeeReview.Api.Identity;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Operator moderation. Maps <c>/api/moderation</c> (NET_RULES 3.3).
/// <para>
/// The gap this fills: <c>/api/reports</c> wrote rows nothing read, and the only way to act on
/// anything was <c>/api/takedowns</c> — a shared admin key that deletes the comic, the blob and
/// the leaderboard row on the spot, with no review step and nothing stopping the next visitor
/// from regenerating exactly the same comic about the same named business.
/// </para>
/// <para>
/// So the actions here are graded. <b>Hide</b> is reversible and is what an unreviewed report
/// gets. <b>Suppress</b> is what makes a removal stick. <b>Remove</b> is the destructive one and
/// suppresses as part of the same call, because a delete that can be undone by the next tap is
/// not a delete.
/// </para>
/// <para>
/// Gated by a role rather than a shared key. A key names nobody, and a moderation trail whose
/// actor column always reads "whoever had the key" cannot be audited.
/// </para>
/// </summary>
internal static class ModerationEndpoints
{
    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/moderation")
            .WithTags("Moderation")
            .RequireAuthorization(ModerationOptions.PolicyName);

        group.MapGet("/queue", GetQueueAsync);
        group.MapPost("/{placeId}/hide", HideAsync);
        group.MapPost("/{placeId}/restore", RestoreAsync);
        group.MapPost("/{placeId}/suppress", SuppressAsync);
        group.MapDelete("/{placeId}", RemoveAsync);

        return app;
    }

    private static async Task<IResult> GetQueueAsync(
        ModerationQueueReader reader,
        HttpContext http) =>
        Results.Ok(await reader.GetQueueAsync(http.RequestAborted));

    /// <summary>Withholds a comic pending review. Reversible, and does not block regeneration.</summary>
    private static async Task<IResult> HideAsync(
        string placeId,
        ModerationActionDto action,
        ModerationRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        await repository.SetStateAsync(
            PlaceId.From(placeId),
            hidden: true,
            suppressed: false,
            reason: Note(action, "Hidden by a moderator."),
            actor: Actor(identityAccessor),
            http.RequestAborted);

        return Results.NoContent();
    }

    /// <summary>
    /// Clears both flags — the "we looked, it is fine" verdict.
    /// <para>
    /// The row is kept and marked reviewed rather than deleted, which is what stops the next
    /// three reports from silently auto-hiding a comic a human has already cleared.
    /// </para>
    /// </summary>
    private static async Task<IResult> RestoreAsync(
        string placeId,
        ModerationActionDto action,
        ModerationRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        await repository.SetStateAsync(
            PlaceId.From(placeId),
            hidden: false,
            suppressed: false,
            reason: Note(action, "Reviewed and cleared."),
            actor: Actor(identityAccessor),
            http.RequestAborted);

        return Results.NoContent();
    }

    /// <summary>Blocks regeneration without deleting what already exists.</summary>
    private static async Task<IResult> SuppressAsync(
        string placeId,
        ModerationActionDto action,
        ModerationRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        await repository.SetStateAsync(
            PlaceId.From(placeId),
            hidden: true,
            suppressed: true,
            reason: Note(action, "Suppressed by a moderator."),
            actor: Actor(identityAccessor),
            http.RequestAborted);

        return Results.NoContent();
    }

    /// <summary>
    /// Erases the comic everywhere it survives, then suppresses the place.
    /// <para>
    /// Four stores, because four things outlive a comic in different ways: the cached row, the
    /// blob, the live leaderboard entry, the weekly archive that is built to outlive expiry, and
    /// every user's kept copy in the container the cleanup service never visits. Suppression is
    /// part of the same call — the removal is otherwise undone by whoever taps the restaurant
    /// next.
    /// </para>
    /// </summary>
    private static async Task<IResult> RemoveAsync(
        string placeId,
        // Explicit, because Minimal APIs do not infer a body on DELETE. The note travels with
        // the request rather than in a query string: it is operator free text and can be long.
        [FromBody] ModerationActionDto action,
        ModerationRepository repository,
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ILeaderboardRepository leaderboardRepository,
        IHallOfFameArchive hallOfFameArchive,
        IKeptComicArchive keptComicArchive,
        ICurrentRequestIdentityAccessor identityAccessor,
        TelemetryClient telemetryClient,
        ILogger<ModerationRepository> logger,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        var id = PlaceId.From(placeId);
        var actor = Actor(identityAccessor);
        var reason = Note(action, "Removed by a moderator.");

        // Suppressed FIRST. If any erase below fails the place is already un-regeneratable,
        // which is the safe half-state; the reverse order leaves a window where the comic is
        // gone and the next request draws it again.
        await repository.SetStateAsync(id, hidden: true, suppressed: true, reason, actor, http.RequestAborted);

        var comic = await comicRepository.GetByPlaceIdAsync(id);
        if (comic is not null)
        {
            await comicRepository.DeleteAsync(id);

            if (!comic.Id.IsEmpty)
            {
                await blobStorageService.DeleteComicImageAsync(comic.Id.Value);
            }
        }

        var entry = await leaderboardRepository.GetByPlaceIdAsync(id);
        if (entry is not null)
        {
            await leaderboardRepository.DeleteAsync(id, entry.Region);
        }

        await hallOfFameArchive.DeleteAllForPlaceAsync(id, http.RequestAborted);
        await keptComicArchive.DeleteAllForPlaceAsync(id, http.RequestAborted);

        logger.LogWarning("Moderator {Actor} removed all content for {PlaceId}", actor, id);

        telemetryClient.TrackEvent("ModerationRemoval", new Dictionary<string, string>
        {
            ["PlaceId"] = id.Value,
            ["Actor"] = actor
        });

        return Results.NoContent();
    }

    private static string Actor(ICurrentRequestIdentityAccessor identityAccessor)
    {
        var userId = identityAccessor.GetCurrentUserId().Value;
        return string.IsNullOrWhiteSpace(userId) ? "unknown" : userId;
    }

    private static string Note(ModerationActionDto? action, string fallback)
    {
        var reason = action?.Reason?.Trim() ?? string.Empty;

        // Capped: this is operator free text landing in a Table Storage string column, and an
        // unbounded paste would fail the write rather than the request.
        if (reason.Length > 512)
        {
            reason = reason[..512];
        }

        return reason.Length == 0 ? fallback : reason;
    }

    private static IResult BadPlaceId(HttpContext http) => Results.Problem(
        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title: "Bad Request",
        statusCode: StatusCodes.Status400BadRequest,
        detail: "Place ID is required",
        instance: http.Request.Path);
}
