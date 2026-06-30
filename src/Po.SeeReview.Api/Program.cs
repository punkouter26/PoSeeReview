using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Po.SeeReview.Api;
using Po.SeeReview.Api.Features;
using Po.SeeReview.Api.Health;
using Po.SeeReview.Api.HostedServices;
using Po.SeeReview.Api.Identity;
using Po.SeeReview.Api.Lobby;
using Po.SeeReview.Api.Middleware;
using Po.SeeReview.Api.Telemetry;
using Po.SeeReview.Application;
using Po.SeeReview.Application.Abstractions;
using Po.SeeReview.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Only configure Serilog if not running in test mode
var isTestMode = Environment.GetEnvironmentVariable("DISABLE_SERILOG") == "true";

if (!isTestMode)
{
    // Configure Serilog early for startup logging
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateBootstrapLogger();
}

try
{
    if (!isTestMode)
    {
        Log.Information("Starting SeeReview API");
    }

    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureAzureKeyVault(isTestMode);

    // Replace default logging with Serilog (unless in test mode)
    if (!isTestMode)
    {
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());
    }

    // Add services to the container.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddScoped<ICurrentRequestIdentityAccessor, HttpContextRequestIdentityAccessor>();
    builder.Services.AddValidatorsFromAssemblyContaining<Po.SeeReview.Shared.Validation.TakedownRequestValidator>();
    builder.Services.AddApplication();

    builder.Services.AddConfiguredTelemetry(builder.Configuration);
    builder.Services.AddConfiguredRateLimiting(builder.Configuration);

    // Configure Health Checks
    builder.Services.AddHealthChecks()
        .AddCheck<AzureTableStorageHealthCheck>(
            "azure_table_storage",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready", "storage"])
        .AddCheck<AzureBlobStorageHealthCheck>(
            "azure_blob_storage",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready", "storage"])
        .AddCheck<GoogleMapsHealthCheck>(
            "google_maps_api",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready", "external"]);

    // Configure OpenAPI (built-in .NET 10 support)
    builder.Services.AddOpenApi();

    // CORS intentionally omitted: the API serves the Blazor WASM client from the
    // same origin (BFF), so cross-origin policy is unnecessary (NET_RULES 2.2).

    // Register infrastructure services (Azure clients)
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHostedService<ExpiredComicCleanupService>();
    // Fail-fast guard for required AI/Map secrets in Production (PoFunQuiz pattern).
    builder.Services.AddHostedService<StartupSecretValidator>();

    // Register SignalR for real-time multiplayer lobby
    builder.Services.AddSignalR();

    var app = builder.Build();

    // Add custom middleware
    // Removed RequestLoggingMiddleware - using Serilog's UseSerilogRequestLogging instead
    app.UseExceptionHandler();
    app.UseMiddleware<UserAgentValidationMiddleware>();

    app.UseCorrelationEnrichment();

    // Request logging with Serilog (disabled in test mode)
    if (!isTestMode)
    {
        app.UseConfiguredSerilogRequestLogging();
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseWebAssemblyDebugging();
        app.MapScalarApiReference(); // Modern API documentation UI
    }

    // HTTPS redirection — disabled for Test/E2E environments that operate on HTTP only
    if (!app.Environment.IsEnvironment("Test"))
    {
        app.UseHttpsRedirection();
    }

    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    app.UseRateLimiter();

    // Health check endpoints (/health, /health/live, /health/ready)
    app.MapHealthEndpoints();

    // Feature slices (Minimal API, MapGroup) — NET_RULES 3.3
    app.MapFeatureEndpoints();

    // SignalR hub for multiplayer lobby
    app.MapHub<LobbyHub>("/lobby");

    // Fallback to index.html for all non-API routes (Blazor SPA routing)
    app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), builder =>
    {
        builder.UseRouting();
        builder.UseEndpoints(endpoints =>
        {
            endpoints.MapFallbackToFile("index.html");
        });
    });

    if (!isTestMode)
    {
        Log.Information("SeeReview API started successfully");
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    if (!isTestMode)
    {
        Log.Fatal(ex, "Application terminated unexpectedly");
    }
    // Always rethrow so WebApplicationFactory can see the exception
    throw;
}
finally
{
    if (!isTestMode)
    {
        Log.CloseAndFlush();
    }
}

// Make Program class accessible to integration tests
public partial class Program { }
