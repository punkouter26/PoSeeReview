namespace PoSeeReview.Api.Middleware;

/// <summary>
/// Minimal-API endpoint filter enforcing a static <c>X-Api-Key</c> header, read from
/// <see cref="ConfigurationKey"/>. Fails closed (503) when the key is unconfigured.
/// Replaces the former MVC RequireApiKey action filter for slice endpoints (NET_RULES 3.3).
/// </summary>
public sealed class ApiKeyEndpointFilter(IConfiguration configuration, ILogger<ApiKeyEndpointFilter> logger)
    : IEndpointFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    public string ConfigurationKey { get; init; } = "Takedowns:ApiKey";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expectedKey = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            logger.LogError("ApiKey: configuration key '{ConfigKey}' is not set. Denying request.", ConfigurationKey);
            return Results.Problem(
                title: "Service not configured",
                detail: "This endpoint is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!http.Request.Headers.TryGetValue(ApiKeyHeader, out var suppliedKey)
            || !string.Equals(suppliedKey, expectedKey, StringComparison.Ordinal))
        {
            logger.LogWarning("ApiKey: invalid or missing {Header} for {Path}. RemoteIp={Ip}",
                ApiKeyHeader, http.Request.Path, http.Connection.RemoteIpAddress);
            return Results.Problem(
                title: "Unauthorized",
                detail: "A valid X-Api-Key header is required.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
