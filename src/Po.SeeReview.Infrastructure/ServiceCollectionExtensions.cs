using System.Net;
using Azure;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Configuration;
using Po.SeeReview.Infrastructure.Repositories;
using Po.SeeReview.Infrastructure.Services;
using Polly;
using Polly.Retry;

namespace Po.SeeReview.Infrastructure;

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

        // Register Azure Table Storage client
        var tableConnectionString = configuration.GetConnectionString("AzureTableStorage")
            ?? throw new InvalidOperationException("AzureTableStorage connection string is required");
        services.AddSingleton(_ => new TableServiceClient(tableConnectionString));

        // Register Azure Blob Storage client
        var blobConnectionString = configuration.GetConnectionString("AzureBlobStorage")
            ?? throw new InvalidOperationException("AzureBlobStorage connection string is required");
        services.AddSingleton(_ => new BlobServiceClient(blobConnectionString));

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
            });
        services.AddScoped<IRestaurantService, RestaurantService>();
        services.AddScoped<IReviewScraperService, ReviewScraperService>();
        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddScoped<IAzureOpenAIService, AzureOpenAIService>();
        services.AddHttpClient<IDalleComicService, DalleComicService>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddScoped<IComicTextOverlayService, ComicTextOverlayService>();
        services.AddScoped<IComicGenerationService, ComicGenerationService>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();

        return services;
    }
}
