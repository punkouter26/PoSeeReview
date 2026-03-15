using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;

namespace Po.SeeReview.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that spins up a dedicated Testcontainers Azurite instance
/// so the WebApp can reach real Azure Storage emulation during tests.
/// Implements IAsyncLifetime: xUnit calls InitializeAsync BEFORE any test class is created,
/// so the container is running and env vars are set before WebApp.CreateBuilder() reads them.
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>, IAsyncLifetime
    where TProgram : class
{
    // One container per factory instance (one per IClassFixture<> type)
    private AzuriteContainer? _azuriteContainer;

    public CustomWebApplicationFactory()
    {
        // Env vars that must be present BEFORE WebApplication.CreateBuilder(args) runs because
        // AddInfrastructure reads builder.Configuration during service registration — before
        // ConfigureAppConfiguration callbacks fire.
        // Storage connection strings are set in InitializeAsync once the container is ready.
        SetIfEmpty("DISABLE_SERILOG", "true");
        SetIfEmpty("DISABLE_USER_AGENT_VALIDATION", "true");

        // External API stubs — allow AddInfrastructure to pass its required-key checks.
        // Tests that validate real AI/Maps calls are expected to skip via HasValidApiKey guards.
        SetIfEmpty("AzureOpenAI__Endpoint", "https://test-openai.openai.azure.com");
        SetIfEmpty("AzureOpenAI__ApiKey", "test-api-key-00000000000000000000000000000000");
        SetIfEmpty("AzureOpenAI__DeploymentName", "gpt-4");
        SetIfEmpty("AzureOpenAI__DalleDeploymentName", "dall-e-3");
        SetIfEmpty("GoogleMaps__ApiKey", "test-google-maps-api-key");
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — xUnit lifecycle hooks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the Azurite container and sets connection-string env vars before
    /// any test class is constructed and before CreateClient() is called.
    /// </summary>
    public async Task InitializeAsync()
    {
        _azuriteContainer = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .Build();
        await _azuriteContainer.StartAsync();

        var cs = _azuriteContainer.GetConnectionString();
        // Override storage env vars with the actual Testcontainers Azurite connection string.
        // These are read by AddInfrastructure when CreateClient() triggers WebApp startup.
        Environment.SetEnvironmentVariable("ConnectionStrings__AzureTableStorage", cs);
        Environment.SetEnvironmentVariable("ConnectionStrings__AzureBlobStorage", cs);
    }

    /// <summary>Disposes the WebApp host, then tears down the Azurite container.</summary>
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_azuriteContainer != null)
            await _azuriteContainer.DisposeAsync();
    }

    private static void SetIfEmpty(string key, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ASPNETCORE_URLS", "http://localhost");
        builder.UseEnvironment("Test");

        // Use simple console logging for tests
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        // Add test configuration with defaults for required services
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Provide default test configuration that satisfies all required dependencies
            var testConfig = new Dictionary<string, string?>
            {
                // Azure Storage - use Azurite emulator (standard ports)
                ["ConnectionStrings:AzureTableStorage"] = "UseDevelopmentStorage=true",
                ["ConnectionStrings:AzureBlobStorage"] = "UseDevelopmentStorage=true",
                
                // Azure OpenAI - use placeholder values for tests that don't call real AI
                ["AzureOpenAI:Endpoint"] = "https://test-openai.openai.azure.com",
                ["AzureOpenAI:ApiKey"] = "test-api-key-for-integration-tests",
                ["AzureOpenAI:DeploymentName"] = "gpt-4",
                ["AzureOpenAI:DalleDeploymentName"] = "dall-e-3",
                
                // Google Maps - use placeholder for tests that don't call real API
                ["GoogleMaps:ApiKey"] = "test-google-maps-api-key"
            };

            // Override with environment variables if explicitly set
            if (Environment.GetEnvironmentVariable("AZURE_TABLE_STORAGE_CONNECTION_STRING") is { } tableStorage)
                testConfig["ConnectionStrings:AzureTableStorage"] = tableStorage;

            if (Environment.GetEnvironmentVariable("AZURE_BLOB_STORAGE_CONNECTION_STRING") is { } blobStorage)
                testConfig["ConnectionStrings:AzureBlobStorage"] = blobStorage;

            if (Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") is { } openAiEndpoint)
                testConfig["AzureOpenAI:Endpoint"] = openAiEndpoint;

            if (Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") is { } openAiKey)
                testConfig["AzureOpenAI:ApiKey"] = openAiKey;

            if (Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") is { } googleMapsKey)
                testConfig["GoogleMaps:ApiKey"] = googleMapsKey;

            config.AddInMemoryCollection(testConfig);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);

        // Set a valid browser user agent to pass UserAgentValidationMiddleware
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }
}
