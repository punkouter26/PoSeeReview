using System.Net;
using Azure;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Core.Interfaces;
using PoSeeReview.Infrastructure.Comics;
using PoSeeReview.Infrastructure.Configuration;
using PoSeeReview.Infrastructure.Leaderboard;
using PoSeeReview.Infrastructure.Repositories;
using PoSeeReview.Infrastructure.Restaurants;
using PoSeeReview.Infrastructure.Storage;
using Polly;
using Polly.Retry;

namespace PoSeeReview.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all infrastructure services and Azure clients
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<AzureStorageOptions>(
            configuration.GetSection(AzureStorageOptions.SectionName));
        services.Configure<AzureOpenAIOptions>(
            configuration.GetSection(AzureOpenAIOptions.SectionName));
        services.Configure<ComicOptions>(
            configuration.GetSection(ComicOptions.SectionName));

        // Storage clients: cloud resolves via System-assigned Managed Identity against the
        // account endpoints (NET_RULES 5.4); connection strings remain only for local Azurite.
        var tableEndpoint = configuration["AzureStorage:TableEndpoint"];
        var blobEndpoint = configuration["AzureStorage:BlobEndpoint"];

        if (!string.IsNullOrEmpty(tableEndpoint))
        {
            var credential = new DefaultAzureCredential();
            services.AddSingleton(_ => new TableServiceClient(new Uri(tableEndpoint), credential));
            services.AddSingleton(_ => new BlobServiceClient(
                new Uri(blobEndpoint ?? throw new InvalidOperationException(
                    "AzureStorage:BlobEndpoint is required when AzureStorage:TableEndpoint is set.")),
                credential));
        }
        else
        {
            var tableConnectionString = configuration.GetConnectionString("AzureTableStorage")
                ?? configuration["AzureTableStorage"]
                ?? Environment.GetEnvironmentVariable("AZURE_TABLE_STORAGE_CONNECTION_STRING")
                ?? throw new InvalidOperationException(
                    "Set AzureStorage:TableEndpoint (Managed Identity) or ConnectionStrings:AzureTableStorage (Azurite).");

            var blobConnectionString = configuration.GetConnectionString("AzureBlobStorage")
                ?? configuration["AzureBlobStorage"]
                ?? Environment.GetEnvironmentVariable("AZURE_BLOB_STORAGE_CONNECTION_STRING")
                ?? throw new InvalidOperationException(
                    "Set AzureStorage:BlobEndpoint (Managed Identity) or ConnectionStrings:AzureBlobStorage (Azurite).");

            services.AddSingleton(_ => new TableServiceClient(tableConnectionString));
            services.AddSingleton(_ => new BlobServiceClient(blobConnectionString));
        }

        // Register Azure OpenAI client
        var openAiOptions = configuration.GetSection(AzureOpenAIOptions.SectionName)
            .Get<AzureOpenAIOptions>()
            ?? throw new InvalidOperationException("AzureOpenAI configuration is required");

        if (string.IsNullOrEmpty(openAiOptions.Endpoint) || string.IsNullOrEmpty(openAiOptions.ApiKey))
        {
            throw new InvalidOperationException("AzureOpenAI Endpoint and ApiKey must be configured");
        }

        services.AddSingleton(_ => new AzureOpenAIClient(
            new Uri(openAiOptions.Endpoint),
            new AzureKeyCredential(openAiOptions.ApiKey)));

        // Register repositories
        services.AddScoped<RestaurantRepository>();
        services.AddScoped<IComicRepository, ComicRepository>();
        services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();

        // Register services
        services.AddHttpClient<GoogleMapsService>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.ConnectionClose = false;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.UseJitter = true;

                // Trip the circuit after 5 consecutive failures, keep it open for 30s.
                // Prevents Maps outages from cascading into comic 5xx storms.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

                // Aggregate per-attempt budget must stay below the 10s user-facing latency target.
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
            });
        services.AddScoped<IRestaurantService, RestaurantService>();

        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddScoped<IAzureOpenAIService, AzureOpenAIService>();

        // Named HttpClient used by GeminiComicService — generous timeout for image generation
        services.AddHttpClient("GeminiApi")
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(90));

        services.AddScoped<IImageGenerationService>(sp =>
            new GeminiComicService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeminiComicService>>(),
                sp.GetRequiredService<Microsoft.ApplicationInsights.TelemetryClient>()));
        services.AddScoped<IComicTextOverlayService, ComicTextOverlayService>();
        services.AddScoped<IComicGenerationService, ComicGenerationService>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();

        return services;
    }
}
