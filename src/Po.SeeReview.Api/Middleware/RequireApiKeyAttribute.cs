using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Po.SeeReview.Api.Middleware;

/// <summary>
/// Action filter that enforces a static API key on protected endpoints.
/// Callers must supply the key via the <c>X-Api-Key</c> request header.
/// The expected key is read from <c>Takedowns:ApiKey</c> in configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IResourceFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    /// <summary>Configuration key path for the expected secret.</summary>
    public string ConfigurationKey { get; init; } = "Takedowns:ApiKey";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var configuration = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>();

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<RequireApiKeyAttribute>>();

        var expectedKey = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            // Deny all requests when the key has not been configured — fail closed.
            logger.LogError(
                "RequireApiKey: configuration key '{ConfigKey}' is not set. Denying request.",
                ConfigurationKey);

            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service not configured",
                Detail = "This endpoint is temporarily unavailable."
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var suppliedKey)
            || !string.Equals(suppliedKey, expectedKey, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "RequireApiKey: invalid or missing {Header} for {Path}. RemoteIp={Ip}",
                ApiKeyHeader,
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid X-Api-Key header is required."
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
