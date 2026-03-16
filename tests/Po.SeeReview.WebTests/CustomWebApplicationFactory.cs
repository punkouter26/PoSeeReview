using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.WebTests;

/// <summary>
/// Custom WebApplicationFactory that configures logging without Serilog
/// to avoid frozen logger issues with multiple test hosts.
/// Replaces Azure-backed services with in-memory fakes so the web tests
/// run without Azurite, Google Maps, or OpenAI connections.
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public CustomWebApplicationFactory()
    {
        // Set environment variables BEFORE Program.cs runs (WebApplicationFactory reads
        // these when building the host, so they must be present before CreateBuilder runs).
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
        Environment.SetEnvironmentVariable("DISABLE_USER_AGENT_VALIDATION", "true");

        // Stub connection strings so AddInfrastructure validation passes. The real
        // Azure-backed services are replaced with fakes in ConfigureServices below.
        Environment.SetEnvironmentVariable("AZURE_TABLE_STORAGE_CONNECTION_STRING", "UseDevelopmentStorage=true");
        Environment.SetEnvironmentVariable("AZURE_BLOB_STORAGE_CONNECTION_STRING", "UseDevelopmentStorage=true");

        // AzureOpenAI options — IConfiguration uses __ as the section separator for env vars
        Environment.SetEnvironmentVariable("AzureOpenAI__Endpoint", "https://test.openai.azure.com/");
        Environment.SetEnvironmentVariable("AzureOpenAI__ApiKey", "test-key-12345678901234567890AB");
        Environment.SetEnvironmentVariable("AzureOpenAI__DeploymentName", "test-deployment");
        Environment.SetEnvironmentVariable("AzureOpenAI__DalleDeploymentName", "test-dalle");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove Serilog logger if present
            var loggerDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILogger<>));
            if (loggerDescriptor != null)
            {
                services.Remove(loggerDescriptor);
            }

            // Replace real Azure-backed services with in-memory fakes
            services.Replace(ServiceDescriptor.Scoped<IComicGenerationService>(_ => new FakeComicGenerationService()));
            services.Replace(ServiceDescriptor.Scoped<IRestaurantService>(_ => new FakeRestaurantService()));
        });

        // Use simple console logging for tests instead of Serilog
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        // Disable HTTPS redirection for tests
        builder.UseSetting("ASPNETCORE_URLS", "http://localhost");

        // Add test configuration with Azure connection strings
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add in-memory configuration with test values
            var testConfig = new Dictionary<string, string?>
            {
                ["ConnectionStrings:AzureTableStorage"] = "UseDevelopmentStorage=true",
                ["ConnectionStrings:AzureBlobStorage"] = "UseDevelopmentStorage=true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ApiKey"] = "test-key-12345",
                ["AzureOpenAI:DeploymentName"] = "test-deployment",
                ["AzureOpenAI:DalleDeploymentName"] = "test-dalle-deployment",
                ["GoogleMaps:ApiKey"] = "test-google-maps-key"
            };

            config.AddInMemoryCollection(testConfig);
        });
    }

    // ── Minimal in-memory fakes ──────────────────────────────────────────────

    /// <summary>
    /// Fake IComicGenerationService: cache always empty, generation always fails with KeyNotFoundException.
    /// </summary>
    private sealed class FakeComicGenerationService : IComicGenerationService
    {
        public Task<Comic> GenerateComicAsync(
            string placeId,
            bool forceRegenerate = false,
            CancellationToken cancellationToken = default)
            => throw new KeyNotFoundException($"Restaurant not found in test environment: {placeId}");

        public Task<Comic?> GetCachedComicAsync(
            string placeId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Comic?>(null);
    }

    /// <summary>
    /// Fake IRestaurantService: always returns empty lists and null details.
    /// </summary>
    private sealed class FakeRestaurantService : IRestaurantService
    {
        public Task<List<Restaurant>> GetNearbyRestaurantsAsync(
            double latitude,
            double longitude,
            int limit = 10,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Restaurant>());

        public Task<Restaurant> GetRestaurantByPlaceIdAsync(
            string placeId,
            CancellationToken cancellationToken = default)
            => throw new KeyNotFoundException($"Restaurant not found in test environment: {placeId}");

        public Task<Restaurant?> GetRestaurantDetailsAsync(
            string placeId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Restaurant?>(null);
    }
}
