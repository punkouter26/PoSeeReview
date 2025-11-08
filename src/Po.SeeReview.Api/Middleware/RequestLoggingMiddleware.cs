using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Serilog.Context;

namespace Po.SeeReview.Api.Middleware;

/// <summary>
/// Middleware to enrich request logging with contextual information
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly TelemetryClient _telemetryClient;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        TelemetryClient telemetryClient)
    {
        _next = next;
        _logger = logger;
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate correlation ID for request tracking
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                           ?? Guid.NewGuid().ToString();

        context.TraceIdentifier = correlationId;
        Activity.Current?.SetIdFormat(ActivityIdFormat.W3C);
        if (Activity.Current is { } currentActivity)
        {
            currentActivity.SetTag("CorrelationId", correlationId);
        }

        var requestTelemetry = context.Features.Get<RequestTelemetry>();
        if (requestTelemetry != null)
        {
            requestTelemetry.Context.Operation.Id = correlationId;
            requestTelemetry.Context.Operation.ParentId ??= Activity.Current?.ParentId;
            requestTelemetry.Properties["RequestPath"] = context.Request.Path;
            requestTelemetry.Properties["UserAgent"] = context.Request.Headers.UserAgent.FirstOrDefault() ?? "Unknown";
        }

        // Push correlation ID to Serilog context
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path))
        using (LogContext.PushProperty("RequestMethod", context.Request.Method))
        using (LogContext.PushProperty("UserAgent", context.Request.Headers["User-Agent"].ToString()))
        {
            // Add correlation ID to response headers
            context.Response.Headers.Append("X-Correlation-ID", correlationId);

            var startTime = DateTime.UtcNow;

            try
            {
                await _next(context);
            }
            finally
            {
                var elapsed = DateTime.UtcNow - startTime;

                _logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds);

                _telemetryClient.GetMetric("Http.RequestDurationMs").TrackValue(elapsed.TotalMilliseconds);
                _telemetryClient.TrackEvent("HttpRequestCompleted", new Dictionary<string, string>
                {
                    ["Path"] = context.Request.Path,
                    ["Method"] = context.Request.Method,
                    ["StatusCode"] = context.Response.StatusCode.ToString(),
                    ["CorrelationId"] = correlationId
                });
            }
        }
    }
}
