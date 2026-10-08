using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoSeeReview.Api.Health;

/// <summary>
/// Reports whether a Google Maps key is configured. It deliberately makes no outbound call:
/// <c>/health</c> and <c>/diag</c> are anonymous, and a probe that geocodes is a billed request
/// (with the key in its query string) that anyone can trigger in a loop.
/// </summary>
public class GoogleMapsHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(configuration["GoogleMaps:ApiKey"])
            ? HealthCheckResult.Degraded("Google Maps API key is not configured")
            : HealthCheckResult.Healthy("Google Maps API key is configured"));
}
