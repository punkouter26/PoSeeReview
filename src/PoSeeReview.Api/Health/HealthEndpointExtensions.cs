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
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedResponse
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // Returns 200 if app is running
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteReadyResponse
        });
    }

    private static Task WriteDetailedResponse(HttpContext context, HealthReport report)
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
                Exception = e.Value.Exception?.ToString()
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
