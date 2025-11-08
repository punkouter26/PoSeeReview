using Azure.Data.Tables;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Po.SeeReview.Api.Health;

/// <summary>
/// Health check for Azure Table Storage connectivity
/// </summary>
public class AzureTableStorageHealthCheck : IHealthCheck
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<AzureTableStorageHealthCheck> _logger;

    public AzureTableStorageHealthCheck(
        TableServiceClient tableServiceClient,
        ILogger<AzureTableStorageHealthCheck> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to query table service properties to verify connectivity
            await _tableServiceClient.GetPropertiesAsync(cancellationToken);

            _logger.LogDebug("Azure Table Storage health check passed");
            return HealthCheckResult.Healthy("Azure Table Storage is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Table Storage health check failed");
            return HealthCheckResult.Unhealthy(
                "Azure Table Storage is not accessible",
                ex);
        }
    }
}
