using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Po.SeeReview.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that configures logging without Serilog
/// to avoid frozen logger issues with multiple test hosts
/// </summary>
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public CustomWebApplicationFactory()
    {
        // Set environment variable BEFORE Program.cs runs
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
        Environment.SetEnvironmentVariable("DISABLE_USER_AGENT_VALIDATION", "true");
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
