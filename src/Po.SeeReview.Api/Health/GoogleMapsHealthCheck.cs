using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Po.SeeReview.Api.Health;

/// <summary>
/// Health check for Google Maps API connectivity
/// </summary>
public class GoogleMapsHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleMapsHealthCheck> _logger;

    public GoogleMapsHealthCheck(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GoogleMapsHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = _configuration["GoogleMaps:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Google Maps API key is not configured");
                return HealthCheckResult.Degraded("Google Maps API key is not configured");
            }

            var httpClient = _httpClientFactory.CreateClient();

            // Simple API key validation call (Geocoding API is used for validation)
            var response = await httpClient.GetAsync(
                $"https://maps.googleapis.com/maps/api/geocode/json?address=test&key={apiKey}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Google Maps API health check passed");
                return HealthCheckResult.Healthy("Google Maps API is accessible");
            }
            else
            {
                _logger.LogWarning("Google Maps API returned status code: {StatusCode}", response.StatusCode);
                return HealthCheckResult.Degraded($"Google Maps API returned {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Maps API health check failed");
            return HealthCheckResult.Unhealthy(
                "Google Maps API is not accessible",
                ex);
        }
    }
}
