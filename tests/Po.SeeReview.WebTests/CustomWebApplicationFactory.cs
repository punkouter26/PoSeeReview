using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Po.SeeReview.WebTests;

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
}
