using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Health;

internal static class HealthEndpointExtensions
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static void MapHealthEndpoints(this WebApplication app)
    {
        // Full exception detail (types, stack traces) is exposed only in Development or when the
        // operator explicitly opts in via HealthChecks:DetailedErrors — /health is anonymous, so
        // it must not leak internals to unauthenticated callers in Production.
        var includeDetails = app.Environment.IsDevelopment()
            || app.Configuration.GetValue<bool>("HealthChecks:DetailedErrors");

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = (context, report) => WriteDetailedResponse(context, report, includeDetails)
        }).AllowAnonymous();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // Returns 200 if app is running
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteReadyResponse
        }).AllowAnonymous();
    }

    private static Task WriteDetailedResponse(HttpContext context, HealthReport report, bool includeDetails)
    {
        context.Response.ContentType = "application/json";

        var payload = new HealthStatusDto
        {
            Status = report.Status.ToString(),
            Timestamp = DateTime.UtcNow,
            DurationMilliseconds = report.TotalDuration.TotalMilliseconds,
            Checks = report.Entries.Select(e => new HealthCheckStatusDto
            {
                Name = e.Key,
                Status = e.Value.Status.ToString(),
                Description = e.Value.Description,
                DurationMilliseconds = e.Value.Duration.TotalMilliseconds,
                Exception = includeDetails ? e.Value.Exception?.ToString() : null
            }).ToList()
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, CamelCaseOptions));
    }

    private static Task WriteReadyResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        }, CamelCaseOptions);

        return context.Response.WriteAsync(result);
    }
}
