using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Po.SeeReview.Api.Health;

/// <summary>
/// Health check for Azure Blob Storage connectivity
/// </summary>
public class AzureBlobStorageHealthCheck : IHealthCheck
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageHealthCheck> _logger;

    public AzureBlobStorageHealthCheck(
        BlobServiceClient blobServiceClient,
        ILogger<AzureBlobStorageHealthCheck> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get blob service properties to verify connectivity
            await _blobServiceClient.GetPropertiesAsync(cancellationToken);

            _logger.LogDebug("Azure Blob Storage health check passed");
            return HealthCheckResult.Healthy("Azure Blob Storage is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob Storage health check failed");
            return HealthCheckResult.Unhealthy(
                "Azure Blob Storage is not accessible",
                ex);
        }
    }
}
