using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.ApplicationInsights.Extensibility;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PoSeeReview.Api.Telemetry;

internal static class TelemetryServiceCollectionExtensions
{
    // Production telemetry budget: cap ingestion at a few items/sec via adaptive
    // sampling, but never sample away Exceptions (they are the highest-signal,
    // lowest-volume type). Traces/requests/dependencies absorb the sampling.
    private const int MaxTelemetryItemsPerSecond = 5;

    internal static IServiceCollection AddConfiguredTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Stamp cloud_RoleName from the execution assembly so AI never logs unknown_service:dotnet.
        services.AddSingleton<ITelemetryInitializer, RoleNameTelemetryInitializer>();

        // Always add Application Insights (required by other services for TelemetryClient).
        // We disable the built-in adaptive sampler here so we can install a custom one
        // below that excludes Exceptions (the default sampler would drop them too).
        services.AddApplicationInsightsTelemetry(options =>
        {
            options.EnableAdaptiveSampling = false;
            options.EnablePerformanceCounterCollectionModule = false;
            options.EnableEventCounterCollectionModule = false;
            options.EnableDependencyTrackingTelemetryModule = false;
            options.EnableHeartbeat = false;
            options.EnableAppServicesHeartbeatTelemetryModule = false;
            options.EnableAzureInstanceMetadataTelemetryModule = false;
            options.EnableQuickPulseMetricStream = false;
            options.EnableAuthenticationTrackingJavaScript = false;
        });

        // Strict production profile: aggressive adaptive sampling that preserves
        // Exceptions and drops trace noise. Non-production keeps full fidelity for
        // debugging. (Telemetry budget — directive #9.)
        if (environment.IsProduction())
        {
            services.Configure<TelemetryConfiguration>(telemetryConfig =>
            {
                telemetryConfig.DefaultTelemetrySink.TelemetryProcessorChainBuilder
                    .UseAdaptiveSampling(
                        maxTelemetryItemsPerSecond: MaxTelemetryItemsPerSecond,
                        excludedTypes: "Exception")
                    .Build();
            });
        }

        var appInsightsConnectionString = configuration.GetValue<string>("ApplicationInsights:ConnectionString");
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: "PoSeeReview.Api", serviceVersion: "1.0.0"))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("PoSeeReview.*"))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("PoSeeReview.*")
                    .AddAzureMonitorMetricExporter(options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    }));
        }

        return services;
    }
}
