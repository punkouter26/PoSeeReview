namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// Cross-restaurant analytics slice. Maps <c>/api/insights</c> (NET_RULES 3.3).
/// <para>
/// Requires a session — no <c>.AllowAnonymous()</c>, so the deny-by-default fallback policy
/// applies exactly as it does to every other business slice. It stays off the named limiters
/// because it spends nothing: every number comes from Table rows this app already wrote.
/// </para>
/// </summary>
internal static class InsightsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights").WithTags("Insights");

        group.MapGet("", GetInsightsAsync);

        return app;
    }

    private static async Task<IResult> GetInsightsAsync(
        IInsightsService insightsService,
        ILogger<InsightsService> logger,
        HttpContext http)
    {
        try
        {
            var insights = await insightsService.GetInsightsAsync(http.RequestAborted);
            return Results.Ok(insights);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build insights");

            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title: "Internal Server Error",
                statusCode: StatusCodes.Status500InternalServerError,
                detail: "Insights are temporarily unavailable.",
                instance: http.Request.Path);
        }
    }
}
