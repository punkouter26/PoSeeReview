using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Po.SeeReview.Api;
using Po.SeeReview.Api.Health;
using Po.SeeReview.Api.HostedServices;
using Po.SeeReview.Api.Identity;
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
    builder.Services.AddControllers();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddScoped<ICurrentRequestIdentityAccessor, HttpContextRequestIdentityAccessor>();
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

    // Configure CORS — restrict to known origins
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                // Local dev: allow both Blazor WASM ports and the API itself
                policy.WithOrigins(
                    "http://localhost:5000",
                    "https://localhost:5001",
                    "http://localhost:5245",
                    "https://localhost:7175")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                // Production fallback: same-origin only (API serves Blazor WASM)
                policy.WithOrigins("https://app-poseereview.azurewebsites.net")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
        });
    });

    // Register infrastructure services (Azure clients)
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHostedService<ExpiredComicCleanupService>();

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

    app.UseCors();
    app.UseRateLimiter();

    // Health check endpoints (/api/health, /api/health/live, /api/health/ready)
    app.MapHealthEndpoints();

    app.MapControllers();

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
