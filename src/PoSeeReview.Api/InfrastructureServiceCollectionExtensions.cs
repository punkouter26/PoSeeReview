using System.Net;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Features.Diagnostics;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Features.Insights;
using PoSeeReview.Api.Features.Leaderboard;
using PoSeeReview.Api.Features.Moderation;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api.Storage;
using Polly.Retry;
using PoSeeReview.Api.Abstractions;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api;

/// <summary>
/// Extension methods for registering infrastructure services
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Configuration path for <see cref="AiImageProvider"/>. A constant because the deploy
    /// workflow and appsettings must agree with this file exactly, and a typo in a raw string
    /// would silently select the default provider.
    /// </summary>
    public const string AiProviderConfigurationKey = "Ai:ImageProvider";

    /// <summary>
    /// Configuration path for <see cref="AiChatProvider"/>. Absent means "derive from the image
    /// provider", which is what keeps every deployment that predates this setting on the exact
    /// pairing it was already running.
    /// </summary>
    public const string AiChatProviderConfigurationKey = "Ai:ChatProvider";

    /// <summary>
    /// Registers all infrastructure services and Azure clients
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Configure options
        services.Configure<AzureStorageOptions>(
            configuration.GetSection(AzureStorageOptions.SectionName));
        services.Configure<AzureOpenAIOptions>(
            configuration.GetSection(AzureOpenAIOptions.SectionName));
        services.Configure<ComicOptions>(
            configuration.GetSection(ComicOptions.SectionName));
        services.Configure<GenerationBudgetOptions>(
            configuration.GetSection(GenerationBudgetOptions.SectionName));
        services.Configure<LeaderboardOptions>(
            configuration.GetSection(LeaderboardOptions.SectionName));
        services.Configure<HuggingFaceOptions>(
            configuration.GetSection(HuggingFaceOptions.SectionName));
        services.Configure<InsightsOptions>(
            configuration.GetSection(InsightsOptions.SectionName));
        services.Configure<ModerationOptions>(
            configuration.GetSection(ModerationOptions.SectionName));
        services.Configure<OllamaOptions>(
            configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<AiPricingOptions>(
            configuration.GetSection(AiPricingOptions.SectionName));
        services.Configure<EmbeddingOptions>(
            configuration.GetSection(EmbeddingOptions.SectionName));

        // Chat and image are chosen separately. They were one switch, which meant an image-model
        // experiment could not be run without also changing the scorer underneath it — so every
        // before/after comparison was really two changes at once. The default for a missing
        // Ai:ChatProvider reproduces the old pairing exactly, so nothing shifts under an existing
        // deployment that has not been touched.
        var providerSetting = configuration[AiProviderConfigurationKey];
        var imageProvider = string.IsNullOrWhiteSpace(providerSetting)
            ? AiImageProvider.Gemini
            : Enum.TryParse<AiImageProvider>(providerSetting, ignoreCase: true, out var parsedImage)
                ? parsedImage
                : throw new InvalidOperationException(
                    $"'{providerSetting}' is not a valid {AiProviderConfigurationKey}. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<AiImageProvider>())}.");

        var chatProviderSetting = configuration[AiChatProviderConfigurationKey];
        var chatProvider = string.IsNullOrWhiteSpace(chatProviderSetting)
            ? (imageProvider == AiImageProvider.HuggingFace ? AiChatProvider.HuggingFace : AiChatProvider.AzureOpenAI)
            : Enum.TryParse<AiChatProvider>(chatProviderSetting, ignoreCase: true, out var parsedChat)
                ? parsedChat
                : throw new InvalidOperationException(
                    $"'{chatProviderSetting}' is not a valid {AiChatProviderConfigurationKey}. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<AiChatProvider>())}.");

        var useHuggingFace = imageProvider == AiImageProvider.HuggingFace;
        var useAzureChat = chatProvider == AiChatProvider.AzureOpenAI;

        // Storage clients: cloud resolves via System-assigned Managed Identity against the
        // account endpoints (NET_RULES 5.4); connection strings remain only for local Azurite.
        var tableEndpoint = configuration["AzureStorage:TableEndpoint"];
        var blobEndpoint = configuration["AzureStorage:BlobEndpoint"];

        // Storage:UseAzurite short-circuits Key Vault connection strings so local dev can run
        // against the emulator (docker-compose) instead of the real storage account. Without
        // this, Key Vault secrets always win and every local run mutates production data.
        if (configuration.GetValue<bool>("Storage:UseAzurite"))
        {
            const string azurite =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
                "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;" +
                "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" +
                "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;";

            services.AddSingleton(_ => new TableServiceClient(azurite));
            services.AddSingleton(_ => new BlobServiceClient(azurite));
        }
        else if (!string.IsNullOrEmpty(tableEndpoint))
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

        // Register Azure OpenAI client — only when Azure is the active CHAT provider. Under the
        // HuggingFace or Ollama chat path the Azure config may be absent, so we must not
        // fail-fast on missing AzureOpenAI settings.
        if (useAzureChat)
        {
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
        }

        // Provision tables + blob container once at startup (fail-fast) instead of per-request.
        services.AddHostedService<TableStorageInitializer>();

        // Register repositories
        services.AddScoped<RestaurantRepository>();
        services.AddScoped<IComicRepository, ComicRepository>();
        services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();
        services.AddScoped<HallOfFameRepository>();
        // Same instance behind both: the slice reads through the concrete type, Takedowns
        // erases through the Shared contract.
        services.AddScoped<IHallOfFameArchive>(sp => sp.GetRequiredService<HallOfFameRepository>());
        services.AddScoped<ComicReportRepository>();
        services.AddScoped<InsightsRepository>();
        services.AddScoped<ShareLinkRepository>();
        services.AddScoped<ModerationRepository>();
        // Same instance behind both, mirroring HallOfFameRepository: the slice reads through the
        // concrete type, Comics/Reports/Takedowns go through the Shared contract.
        services.AddScoped<IContentModerationGate>(sp => sp.GetRequiredService<ModerationRepository>());
        services.AddScoped<ModerationQueueReader>();
        services.AddScoped<IContentSafetyScreener, LexicalContentSafetyScreener>();
        services.AddModerationAuthorization(configuration);
        services.AddScoped<GenerationBudgetService>();
        services.AddScoped<ComicStatsQueryHandler>();
        services.AddScoped<DiagnosticsSnapshotQueryHandler>();

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

        // Chat provider (strangeness analysis + panel captions). Selected independently of the
        // image provider; see the note above. Ollama is the local, zero-marginal-cost tier.
        switch (chatProvider)
        {
            case AiChatProvider.HuggingFace:
                services.AddScoped<IChatCompletionService, HuggingFaceChatService>();
                break;
            case AiChatProvider.Ollama:
                services.AddScoped<IChatCompletionService, OllamaChatService>();
                break;
            default:
                services.AddScoped<IChatCompletionService, AzureOpenAIChatService>();
                break;
        }

        // Cost accounting for every model call, tagged by provider and model. Registered once so
        // there is a single metric whose value is "what this app spent" rather than one metric per
        // provider on three incomparable scales.
        services.AddSingleton<AiCostTracker>();

        // Single flight per place: stops a double-tap from commissioning the same paid comic
        // twice. Singleton because the gates must be shared across requests, not per request.
        services.AddSingleton<ComicGenerationLock>();


        // Image provider: Google Imagen (GeminiComicService), or FLUX via HF (HuggingFaceComicService).
        // FLUX is the fix for Imagen's garbled baked-in speech bubbles — it honours a negative prompt.
        // Both use a named HttpClient with generous timeouts (image gen is slow) and the standard
        // resilience handler for retry/timeout/circuit-breaker on 5xx/429/timeouts.
        var imageClientName = useHuggingFace ? "HuggingFaceApi" : "GeminiApi";
        services.AddHttpClient(imageClientName)
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(90))
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.UseJitter = true;

                // Image generation is slow, so budgets stay generous but within the 90s HttpClient
                // timeout above. SamplingDuration must be >= 2x AttemptTimeout for the standard handler.
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(40);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);
            });

        if (useHuggingFace)
            services.AddScoped<IImageGenerationService, HuggingFaceComicService>();
        else
            services.AddScoped<IImageGenerationService>(sp =>
                new GeminiComicService(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeminiComicService>>(),
                    sp.GetRequiredService<Microsoft.ApplicationInsights.TelemetryClient>()));
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();
        services.AddScoped<IComicTextOverlayService, ComicTextOverlayService>();
        services.AddScoped<IShareCardService, ShareCardService>();
        services.AddScoped<IComicGenerationService, ComicGenerationService>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();

        // Insights. The mock is gated on the environment as well as the flag: a stray
        // Insights:UseMockData in production config must not be able to replace real numbers
        // with a fixture, which is the same posture FakeAuthHandler takes.
        if (!environment.IsProduction() && configuration.GetValue<bool>($"{InsightsOptions.SectionName}:UseMockData"))
        {
            services.AddScoped<MockInsightsService>();
            services.AddScoped<IInsightsService>(sp => sp.GetRequiredService<MockInsightsService>());
            services.AddScoped<IMockable>(sp => sp.GetRequiredService<MockInsightsService>());
        }
        else
        {
            services.AddScoped<IInsightsService, InsightsService>();
        }

        return services;
    }
}
