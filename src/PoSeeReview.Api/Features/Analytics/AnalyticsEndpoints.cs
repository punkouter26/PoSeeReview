using PoSeeReview.Api.Telemetry;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Analytics;

/// <summary>
/// Client funnel telemetry slice. Maps <c>/api/analytics</c> (NET_RULES 3.3).
/// <para>
/// The PRD sets targets — time-to-first-comic under 30 seconds, cache hit rate above 40% — that
/// nothing measured. The server only ever tracked <c>ComicGenerated</c>, which by construction
/// cannot see a user who denied location, searched and gave up, or abandoned a generation
/// halfway. Those steps happen in the browser, so the browser reports them here.
/// </para>
/// </summary>
internal static class AnalyticsEndpoints
{
    /// <summary>Named limiter for event submission.</summary>
    public const string RateLimitPolicy = "analytics-post";

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapPost("/events", RecordEventAsync).RequireRateLimiting(RateLimitPolicy);
        group.MapGet("/funnel", GetFunnelAsync);

        return app;
    }

    private static async Task<IResult> RecordEventAsync(
        FunnelEventDto request,
        FunnelRepository repository,
        TimeProvider timeProvider,
        HttpContext http)
    {
        // A closed vocabulary, enforced server-side. Without this a client bug — or anyone with
        // a session — could mint unbounded distinct dimension values, which in Application
        // Insights is a billing problem rather than a correctness one.
        if (!FunnelSteps.IsKnown(request.Step))
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Unknown funnel step.",
                instance: http.Request.Path);
        }

        // A negative or absurd duration is a broken clock, not a measurement. Clamped away
        // rather than rejected: the step itself still happened and is worth counting.
        var duration = request.DurationMs is > 0 and < MaxPlausibleDurationMs ? request.DurationMs : null;

        var day = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        await repository.RecordAsync(day, request.Step, duration, http.RequestAborted);

        PoSeeReviewTelemetry.FunnelEvents.Add(1,
            [new KeyValuePair<string, object?>("step", request.Step)]);

        return Results.NoContent();
    }

    /// <summary>
    /// Ten minutes. Anything longer than this did not measure a comic generation — the pipeline
    /// times out well before it — so it is a suspended tab or a clock change.
    /// </summary>
    private const int MaxPlausibleDurationMs = 600_000;

    private static async Task<IResult> GetFunnelAsync(
        FunnelRepository repository,
        TimeProvider timeProvider,
        HttpContext http,
        int daysAgo = 0)
    {
        if (daysAgo is < 0 or > 30)
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "daysAgo must be between 0 and 30.",
                instance: http.Request.Path);
        }

        var day = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-daysAgo);
        var snapshot = await repository.GetSnapshotAsync(day, http.RequestAborted);

        return Results.Ok(snapshot);
    }
}
