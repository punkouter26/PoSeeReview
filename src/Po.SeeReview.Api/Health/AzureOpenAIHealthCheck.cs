using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Po.SeeReview.Api.Health;

/// <summary>
/// Health check for Azure OpenAI connectivity
/// </summary>
public class AzureOpenAIHealthCheck : IHealthCheck
{
    private readonly AzureOpenAIClient? _openAIClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureOpenAIHealthCheck> _logger;

    public AzureOpenAIHealthCheck(
        IConfiguration configuration,
        ILogger<AzureOpenAIHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Initialize client only if configuration is present
        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var apiKey = configuration["AzureOpenAI:ApiKey"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            _openAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_openAIClient == null)
            {
                _logger.LogWarning("Azure OpenAI configuration is incomplete");
                return Task.FromResult(HealthCheckResult.Degraded("Azure OpenAI is not fully configured"));
            }

            // Note: Azure OpenAI client doesn't have a built-in health check endpoint
            // In production, you might want to make a lightweight API call
            // For now, we just check if configuration is present
            _logger.LogDebug("Azure OpenAI health check passed (configuration present)");
            return Task.FromResult(HealthCheckResult.Healthy("Azure OpenAI is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Azure OpenAI check failed",
                ex));
        }
    }
}
