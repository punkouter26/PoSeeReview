using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.ApplicationInsights.Extensibility;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Po.SeeReview.Api.Telemetry;

internal static class TelemetryServiceCollectionExtensions
{
    internal static IServiceCollection AddConfiguredTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        // Stamp cloud_RoleName from the execution assembly so AI never logs unknown_service:dotnet.
        services.AddSingleton<ITelemetryInitializer, RoleNameTelemetryInitializer>();

        // Always add Application Insights (required by other services for TelemetryClient).
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

        var appInsightsConnectionString = configuration.GetValue<string>("ApplicationInsights:ConnectionString");
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: "PoSeeReview.Api", serviceVersion: "1.0.0"))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Po.SeeReview.*"))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("Po.SeeReview.*")
                    .AddAzureMonitorMetricExporter(options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    }));
        }

        return services;
    }
}
