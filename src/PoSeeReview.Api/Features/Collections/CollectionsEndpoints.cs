using Microsoft.Extensions.Options;
using PoSeeReview.Api.Identity;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Collections;

/// <summary>
/// Kept comics. Maps <c>/api/collections</c> (NET_RULES 3.3).
/// <para>
/// <c>ComicHistoryService</c> already remembers what a user has seen, but only in one browser's
/// <c>localStorage</c>, and comics expire in 24 hours — so a history entry is a list of dead
/// links on a device the user may not be holding. Keeping copies the artwork into a container
/// the cleanup service never touches and files it against the principal instead of the browser.
/// </para>
/// <para>
/// The permanence is why <see cref="IKeptComicArchive"/> exists: these are, along with the Hall
/// of Fame, the copies that survive everything else, which makes them the ones a takedown has to
/// go after by name.
/// </para>
/// </summary>
internal static class CollectionsEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route here requires a session by way of the deny-by-default fallback policy —
        // a collection is per-principal, so an anonymous caller has nothing to read or write.
        var group = app.MapGroup("/api/collections").WithTags("Collections");

        group.MapGet("", ListAsync);
        group.MapGet("/{placeId}/image", GetImageAsync);
        group.MapPost("/{placeId}", KeepAsync);
        group.MapDelete("/{placeId}", ForgetAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        KeptComicRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        IOptions<CollectionsOptions> options,
        HttpContext http)
    {
        var userId = identityAccessor.GetCurrentUserId();
        var rows = await repository.GetForUserAsync(userId, http.RequestAborted);
        var limit = options.Value.MaxKeptPerUser;

        return Results.Ok(new KeptComicsResponse
        {
            Comics = [.. rows.Select(Project)],
            Limit = limit,
            Remaining = Math.Max(0, limit - rows.Count)
        });
    }

    /// <summary>
    /// Streams a kept copy from this origin.
    /// <para>
    /// Not a SAS URL. A kept comic is private to one person and outlives every signature this
    /// app can mint, so the only correct way to serve it is behind the session that owns it —
    /// the ownership check below is the authorization, not decoration.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetImageAsync(
        string placeId,
        KeptComicRepository repository,
        KeptComicBlobStore blobStore,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return Results.NotFound();
        }

        var userId = identityAccessor.GetCurrentUserId();
        var row = await repository.GetAsync(userId, PlaceId.From(placeId), http.RequestAborted);

        if (row is null || string.IsNullOrWhiteSpace(row.BlobName))
        {
            return Results.NotFound();
        }

        var stream = await blobStore.OpenAsync(row.BlobName, http.RequestAborted);
        if (stream is null)
        {
            return Results.NotFound();
        }

        // Private, not public: this is one user's copy behind their session, and a shared cache
        // holding it would serve it to the next person through the same proxy.
        http.Response.Headers.CacheControl = "private, max-age=86400";

        return Results.File(stream, "image/png", enableRangeProcessing: false);
    }

    private static async Task<IResult> KeepAsync(
        string placeId,
        KeptComicRepository repository,
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ICurrentRequestIdentityAccessor identityAccessor,
        IOptions<CollectionsOptions> options,
        TimeProvider timeProvider,
        ILogger<KeptComicRepository> logger,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return Problem(http, StatusCodes.Status400BadRequest, "Bad Request", "Place ID is required");
        }

        var id = PlaceId.From(placeId);
        var userId = identityAccessor.GetCurrentUserId();

        var alreadyKept = await repository.GetAsync(userId, id, http.RequestAborted);
        if (alreadyKept is not null)
        {
            // Idempotent: keeping something twice is the same as keeping it once, and telling
            // the user it "failed" would be a lie about a state they already have.
            return Results.Ok(Project(alreadyKept));
        }

        var limit = options.Value.MaxKeptPerUser;
        if (await repository.CountForUserAsync(userId, http.RequestAborted) >= limit)
        {
            return Problem(http, StatusCodes.Status409Conflict, "Collection Full",
                $"You are keeping the maximum of {limit} comics. Forget one to make room.");
        }

        var comic = await comicRepository.GetByPlaceIdAsync(id);
        if (comic is null)
        {
            return Problem(http, StatusCodes.Status404NotFound, "Not Found",
                "There is no comic for this restaurant to keep.");
        }

        // Read the live blob now, while it still exists. This is the whole transaction: after
        // the 24-hour window the source is gone, so a keep that deferred the copy would be a
        // keep that quietly kept nothing.
        Stream? artwork = null;
        try
        {
            artwork = await blobStorageService.OpenComicImageStreamAsync(comic.ImageUrl, http.RequestAborted);
            var kept = await repository.KeepAsync(userId, comic, artwork, timeProvider.GetUtcNow(), http.RequestAborted);
            return Results.Ok(Project(kept));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to keep comic {PlaceId}", placeId);
            return Problem(http, StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Could not keep that comic. Please try again.");
        }
        finally
        {
            if (artwork is not null)
            {
                await artwork.DisposeAsync();
            }
        }
    }

    private static async Task<IResult> ForgetAsync(
        string placeId,
        KeptComicRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return Problem(http, StatusCodes.Status400BadRequest, "Bad Request", "Place ID is required");
        }

        var removed = await repository.ForgetAsync(
            identityAccessor.GetCurrentUserId(), PlaceId.From(placeId), http.RequestAborted);

        // 204 either way. Forgetting something already forgotten is the state the caller asked
        // for, and a 404 there reads as a failure the user has no way to act on.
        _ = removed;
        return Results.NoContent();
    }

    private static KeptComicDto Project(KeptComicEntity entity) => new()
    {
        PlaceId = entity.PlaceId,
        RestaurantName = entity.RestaurantName,
        StrangenessScore = entity.StrangenessScore,
        HasImage = !string.IsNullOrWhiteSpace(entity.BlobName),
        ImageUrl = string.IsNullOrWhiteSpace(entity.BlobName)
            ? string.Empty
            : $"/api/collections/{Uri.EscapeDataString(entity.PlaceId)}/image",
        KeptAt = entity.KeptAt
    };

    private static IResult Problem(HttpContext http, int status, string title, string detail) =>
        Results.Problem(
            type: status switch
            {
                StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            },
            title: title,
            statusCode: status,
            detail: detail,
            instance: http.Request.Path);
}
