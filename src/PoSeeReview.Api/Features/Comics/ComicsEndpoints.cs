using System.Diagnostics;
using PoSeeReview.Api.Telemetry;
using PoSeeReview.Application.Comics;
using PoSeeReview.Core;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Comic generation and retrieval slice. Maps <c>/api/comics</c> (NET_RULES 3.3).
/// </summary>
internal static class ComicsEndpoints
{
    public static IEndpointRouteBuilder MapComicsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/comics").WithTags("Comics");

        group.MapPost("/{placeId}", GenerateComic).RequireRateLimiting("comics-post");
        group.MapGet("/{placeId}", GetCachedComic);

        return app;
    }

    private static async Task<IResult> GenerateComic(
        string placeId,
        GenerateComicCommandHandler generateComicCommandHandler,
        ILogger<GenerateComicCommandHandler> logger,
        HttpContext http,
        bool forceRegenerate = false)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            logger.LogWarning("GenerateComic called with empty placeId");
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Place ID is required",
                instance: http.Request.Path);
        }

        try
        {
            using var activity = PoSeeReviewTelemetry.ActivitySource.StartActivity("GenerateComic");
            activity?.SetTag("place_id", placeId);
            activity?.SetTag("force_regenerate", forceRegenerate);

            var startTime = Stopwatch.GetTimestamp();
            logger.LogInformation("Generating comic for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}",
                placeId, forceRegenerate);

            var comic = await generateComicCommandHandler.ExecuteAsync(placeId, forceRegenerate, http.RequestAborted);

            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            var tags = new[]
            {
                new KeyValuePair<string, object?>("cache_hit", comic.IsCached),
                new KeyValuePair<string, object?>("force_regenerate", forceRegenerate)
            };

            PoSeeReviewTelemetry.ComicsGenerated.Add(1, tags);
            PoSeeReviewTelemetry.ComicGenerationDuration.Record(elapsedMs, tags);

            if (forceRegenerate)
            {
                PoSeeReviewTelemetry.ForceRegenerateRequests.Add(1, new[]
                {
                    new KeyValuePair<string, object?>("place_id", placeId)
                });
            }

            activity?.SetTag("comic_id", comic.Id);
            activity?.SetTag("cache_hit", comic.IsCached);
            activity?.SetTag("duration_ms", elapsedMs);

            var dto = new ComicDto
            {
                ComicId = comic.Id,
                PlaceId = comic.PlaceId,
                RestaurantName = comic.RestaurantName,
                Narrative = comic.Narrative,
                StrangenessScore = comic.StrangenessScore,
                BlobUrl = comic.ImageUrl,
                GeneratedAt = comic.CreatedAt,
                ExpiresAt = comic.ExpiresAt,
                IsCached = comic.IsCached
            };

            logger.LogInformation("Comic generated successfully for placeId: {PlaceId}", placeId);
            return Results.Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
            {
                new KeyValuePair<string, object?>("error_type", "restaurant_not_found"),
                new KeyValuePair<string, object?>("place_id", placeId)
            });
            logger.LogWarning(ex, "Restaurant not found: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                title: "Not Found",
                statusCode: StatusCodes.Status404NotFound,
                detail: $"Restaurant not found: {placeId}",
                instance: http.Request.Path);
        }
        catch (InsufficientReviewsException ex)
        {
            PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
            {
                new KeyValuePair<string, object?>("error_type", "insufficient_reviews"),
                new KeyValuePair<string, object?>("place_id", placeId)
            });
            logger.LogWarning(ex, "Insufficient reviews for placeId: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message,
                instance: http.Request.Path);
        }
        catch (InsufficientStrangenessException ex)
        {
            PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
            {
                new KeyValuePair<string, object?>("error_type", "insufficient_strangeness"),
                new KeyValuePair<string, object?>("place_id", placeId)
            });
            logger.LogInformation(ex, "Strangeness below threshold for placeId: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc4918#section-11.2",
                title: "Unprocessable Entity",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "This restaurant's reviews are too ordinary to make a good comic. Try a place with weirder reviews!",
                instance: http.Request.Path);
        }
        catch (Exception ex)
        {
            PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
            {
                new KeyValuePair<string, object?>("error_type", "unknown"),
                new KeyValuePair<string, object?>("place_id", placeId)
            });
            logger.LogError(ex, "Failed to generate comic for placeId: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title: "Internal Server Error",
                statusCode: StatusCodes.Status500InternalServerError,
                detail: "Comic generation failed. Please try again later.",
                instance: http.Request.Path);
        }
    }

    private static async Task<IResult> GetCachedComic(
        string placeId,
        GetCachedComicQueryHandler getCachedComicQueryHandler,
        ILogger<GetCachedComicQueryHandler> logger,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Place ID is required",
                instance: http.Request.Path);
        }

        try
        {
            var cachedComic = await getCachedComicQueryHandler.ExecuteAsync(placeId, http.RequestAborted);

            if (cachedComic != null && cachedComic.ExpiresAt > DateTimeOffset.UtcNow)
            {
                var dto = new ComicDto
                {
                    ComicId = cachedComic.Id,
                    PlaceId = cachedComic.PlaceId,
                    RestaurantName = cachedComic.RestaurantName,
                    Narrative = cachedComic.Narrative,
                    StrangenessScore = cachedComic.StrangenessScore,
                    BlobUrl = cachedComic.ImageUrl,
                    GeneratedAt = cachedComic.CreatedAt,
                    ExpiresAt = cachedComic.ExpiresAt,
                    IsCached = true
                };
                return Results.Ok(dto);
            }

            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                title: "Not Found",
                statusCode: StatusCodes.Status404NotFound,
                detail: "No cached comic found",
                instance: http.Request.Path);
        }
        catch (KeyNotFoundException)
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                title: "Not Found",
                statusCode: StatusCodes.Status404NotFound,
                detail: "No comic found for this restaurant",
                instance: http.Request.Path);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Cannot generate comic for placeId: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message,
                instance: http.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving cached comic for placeId: {PlaceId}", placeId);
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title: "Internal Server Error",
                statusCode: StatusCodes.Status500InternalServerError,
                detail: "Failed to retrieve cached comic",
                instance: http.Request.Path);
        }
    }
}
