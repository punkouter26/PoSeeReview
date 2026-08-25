using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PoSeeReview.Api.Telemetry;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

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
        // Same paid pipeline as the POST above, so it carries the same limiter — otherwise the
        // stream would be a way around the 3/min cap on the one endpoint that spends money.
        group.MapPost("/{placeId}/stream", GenerateComicStream).RequireRateLimiting("comics-post");
        group.MapGet("/{placeId}", GetCachedComic);

        return app;
    }

    /// <summary>
    /// Maps a generation failure onto the response the client already knows how to render.
    /// Shared by the plain POST and the SSE stream: the stream has committed a 200 by the time
    /// generation fails, so it has to carry the same status in its payload instead of a header,
    /// and both paths must agree on what a 422 means or the UI copy diverges.
    /// </summary>
    private static (int Status, string Title, string Detail, string TypeUri, string ErrorType) DescribeFailure(Exception ex) => ex switch
    {
        KeyNotFoundException => (
            StatusCodes.Status404NotFound, "Not Found",
            "Restaurant not found", "https://tools.ietf.org/html/rfc7231#section-6.5.4", "restaurant_not_found"),

        InsufficientReviewsException e => (
            StatusCodes.Status400BadRequest, "Bad Request",
            e.Message, "https://tools.ietf.org/html/rfc7231#section-6.5.1", "insufficient_reviews"),

        InsufficientStrangenessException => (
            StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity",
            "This restaurant's reviews are too ordinary to make a good comic. Try a place with weirder reviews!",
            "https://tools.ietf.org/html/rfc4918#section-11.2", "insufficient_strangeness"),

        _ => (
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            "Comic generation failed. Please try again later.",
            "https://tools.ietf.org/html/rfc7231#section-6.6.1", "unknown")
    };

    private static void TrackFailure(string errorType, string placeId) =>
        PoSeeReviewTelemetry.ComicGenerationErrors.Add(1,
        [
            new KeyValuePair<string, object?>("error_type", errorType),
            new KeyValuePair<string, object?>("place_id", placeId)
        ]);

    private static void LogFailure(ILogger logger, Exception ex, string errorType, string placeId)
    {
        switch (errorType)
        {
            case "insufficient_strangeness":
                logger.LogInformation(ex, "Strangeness below threshold for placeId: {PlaceId}", placeId);
                break;
            case "unknown":
                logger.LogError(ex, "Failed to generate comic for placeId: {PlaceId}", placeId);
                break;
            default:
                logger.LogWarning(ex, "Comic generation rejected ({ErrorType}) for placeId: {PlaceId}", errorType, placeId);
                break;
        }
    }

    private static void RecordSuccessMetrics(Comic comic, bool forceRegenerate, string placeId, long startTimestamp, Activity? activity)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var tags = new[]
        {
            new KeyValuePair<string, object?>("cache_hit", comic.CacheState == ComicCacheState.Cached),
            new KeyValuePair<string, object?>("force_regenerate", forceRegenerate)
        };

        PoSeeReviewTelemetry.ComicsGenerated.Add(1, tags);
        PoSeeReviewTelemetry.ComicGenerationDuration.Record(elapsedMs, tags);

        if (forceRegenerate)
        {
            PoSeeReviewTelemetry.ForceRegenerateRequests.Add(1,
            [
                new KeyValuePair<string, object?>("place_id", placeId)
            ]);
        }

        activity?.SetTag("comic_id", comic.Id.Value);
        activity?.SetTag("cache_hit", comic.CacheState == ComicCacheState.Cached);
        activity?.SetTag("duration_ms", elapsedMs);
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

        using var activity = PoSeeReviewTelemetry.ActivitySource.StartActivity("GenerateComic");
        activity?.SetTag("place_id", placeId);
        activity?.SetTag("force_regenerate", forceRegenerate);

        var startTime = Stopwatch.GetTimestamp();
        logger.LogInformation("Generating comic for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}",
            placeId, forceRegenerate);

        try
        {
            var comic = await generateComicCommandHandler.ExecuteAsync(PlaceId.From(placeId), forceRegenerate, http.RequestAborted);

            RecordSuccessMetrics(comic, forceRegenerate, placeId, startTime, activity);

            logger.LogInformation("Comic generated successfully for placeId: {PlaceId}", placeId);
            return Results.Ok(comic.ToDto());
        }
        catch (Exception ex)
        {
            var (status, title, detail, typeUri, errorType) = DescribeFailure(ex);
            TrackFailure(errorType, placeId);
            LogFailure(logger, ex, errorType, placeId);

            // The 404 detail names the place; the shared map cannot, since it never sees the id.
            if (errorType == "restaurant_not_found")
            {
                detail = $"Restaurant not found: {placeId}";
            }

            return Results.Problem(
                type: typeUri,
                title: title,
                statusCode: status,
                detail: detail,
                instance: http.Request.Path);
        }
    }

    /// <summary>
    /// Same generation as <see cref="GenerateComic"/>, streamed as server-sent events so the
    /// client can narrate the stages the pipeline is genuinely in rather than animating a timer.
    /// One JSON envelope per <c>data:</c> line keeps the trimmed WASM parser trivial.
    /// </summary>
    private static async Task GenerateComicStream(
        string placeId,
        GenerateComicCommandHandler generateComicCommandHandler,
        ILogger<GenerateComicCommandHandler> logger,
        HttpContext http,
        bool forceRegenerate = false)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache,no-store";
        // ARR/nginx will otherwise buffer the whole response and deliver every phase at once,
        // which still yields a correct comic but silently reduces this to the old fake spinner.
        http.Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(placeId))
        {
            await WriteEventAsync(http, new ComicGenerationEventDto
            {
                Kind = ComicGenerationEventDto.ErrorKind,
                ErrorStatus = StatusCodes.Status400BadRequest,
                ErrorTitle = "Bad Request",
                ErrorDetail = "Place ID is required"
            });
            return;
        }

        using var activity = PoSeeReviewTelemetry.ActivitySource.StartActivity("GenerateComicStream");
        activity?.SetTag("place_id", placeId);
        activity?.SetTag("force_regenerate", forceRegenerate);

        var startTime = Stopwatch.GetTimestamp();
        logger.LogInformation("Streaming comic generation for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}",
            placeId, forceRegenerate);

        // The pipeline reports phases from whatever thread it happens to be on. A channel makes
        // the response body single-writer (this method's drain loop) and preserves order —
        // writing to HttpResponse straight from IProgress.Report would race the completion write.
        var events = Channel.CreateUnbounded<ComicGenerationEventDto>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var generation = ProduceAsync();

        try
        {
            await foreach (var evt in events.Reader.ReadAllAsync(http.RequestAborted))
            {
                await WriteEventAsync(http, evt);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Client hung up mid-stream. RequestAborted already cancelled the pipeline; letting
            // this bubble would send UseExceptionHandler off to write a problem document to a
            // socket that is already gone.
            logger.LogInformation("Comic generation stream abandoned by the client for placeId: {PlaceId}", placeId);
        }

        await generation;

        async Task ProduceAsync()
        {
            try
            {
                var progress = new ChannelProgress(events.Writer);
                var comic = await generateComicCommandHandler.ExecuteAsync(
                    PlaceId.From(placeId), forceRegenerate, http.RequestAborted, progress);

                RecordSuccessMetrics(comic, forceRegenerate, placeId, startTime, activity);
                logger.LogInformation("Comic generated successfully (stream) for placeId: {PlaceId}", placeId);

                events.Writer.TryWrite(new ComicGenerationEventDto
                {
                    Kind = ComicGenerationEventDto.CompleteKind,
                    Comic = comic.ToDto()
                });
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                // Abandoned request: nothing to report to a client that is already gone.
            }
            catch (Exception ex)
            {
                var (status, title, detail, _, errorType) = DescribeFailure(ex);
                TrackFailure(errorType, placeId);
                LogFailure(logger, ex, errorType, placeId);

                if (errorType == "restaurant_not_found")
                {
                    detail = $"Restaurant not found: {placeId}";
                }

                events.Writer.TryWrite(new ComicGenerationEventDto
                {
                    Kind = ComicGenerationEventDto.ErrorKind,
                    ErrorStatus = status,
                    ErrorTitle = title,
                    ErrorDetail = detail
                });
            }
            finally
            {
                events.Writer.TryComplete();
            }
        }
    }

    /// <summary>
    /// camelCase to match the client's source-generated <c>AppJsonContext</c>, which is
    /// configured with <c>JsonKnownNamingPolicy.CamelCase</c>. A mismatch here deserializes
    /// silently into a default-valued envelope rather than throwing.
    /// </summary>
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task WriteEventAsync(HttpContext http, ComicGenerationEventDto evt)
    {
        var json = JsonSerializer.Serialize(evt, StreamJsonOptions);
        await http.Response.WriteAsync($"data: {json}\n\n", Encoding.UTF8);
        await http.Response.Body.FlushAsync();
    }

    /// <summary>
    /// Ordered, non-blocking progress sink. Deliberately not <see cref="Progress{T}"/>: with no
    /// SynchronizationContext (the ASP.NET Core case) that class posts each callback to the
    /// thread pool, so phases could be written out of order or overlap the completion event.
    /// </summary>
    private sealed class ChannelProgress(ChannelWriter<ComicGenerationEventDto> writer)
        : IProgress<ComicGenerationPhase>
    {
        public void Report(ComicGenerationPhase value) =>
            writer.TryWrite(new ComicGenerationEventDto
            {
                Kind = ComicGenerationEventDto.PhaseKind,
                Phase = value
            });
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
            var cachedComic = await getCachedComicQueryHandler.ExecuteAsync(PlaceId.From(placeId), http.RequestAborted);

            if (cachedComic != null && cachedComic.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return Results.Ok(cachedComic.ToDto(isCached: true));
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
