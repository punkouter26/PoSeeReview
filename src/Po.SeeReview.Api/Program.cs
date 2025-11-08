using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Po.SeeReview.Api.Health;
using Po.SeeReview.Api.HostedServices;
using Po.SeeReview.Api.Middleware;
using Po.SeeReview.Infrastructure;
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

    // Only configure URLs if not already set via environment (e.g., ASPNETCORE_URLS)
    // This allows E2E tests to force HTTP-only mode
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    {
        builder.WebHost.UseUrls("http://0.0.0.0:5000", "https://0.0.0.0:5001");
    }

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
    builder.Services.AddOpenApi();
    
    // Always add Application Insights (required by other services for TelemetryClient)
    // Suppress console telemetry output in development
    builder.Services.AddApplicationInsightsTelemetry(options =>
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

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });

        options.OnRejected = (context, _) =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimiter");

            logger.LogWarning("Rate limit exceeded for {IpAddress}", context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            context.HttpContext.Response.Headers.RetryAfter = "60";
            return ValueTask.CompletedTask;
        };
    });

    // Configure Health Checks
    builder.Services.AddHealthChecks()
        .AddCheck<AzureTableStorageHealthCheck>(
            "azure_table_storage",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready", "storage" })
        .AddCheck<AzureBlobStorageHealthCheck>(
            "azure_blob_storage",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready", "storage" })
        .AddCheck<GoogleMapsHealthCheck>(
            "google_maps_api",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "external" })
        .AddCheck<AzureOpenAIHealthCheck>(
            "azure_openai",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "external" });

    // Configure Swagger/OpenAPI with Swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "SeeReview API",
            Version = "v1",
            Description = "Restaurant review to comic strip generator API - Turning Real Reviews into Surreal Stories",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "SeeReview Team",
                Url = new Uri("https://github.com/your-org/seereview")
            },
            License = new Microsoft.OpenApi.Models.OpenApiLicense
            {
                Name = "MIT",
                Url = new Uri("https://opensource.org/licenses/MIT")
            }
        });

        // Include XML comments if available
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    });

    // Configure CORS for Blazor WASM
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Register infrastructure services (Azure clients)
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHostedService<ExpiredComicCleanupService>();

    var app = builder.Build();

    // Add custom middleware
    // Removed RequestLoggingMiddleware - using Serilog's UseSerilogRequestLogging instead
    app.UseMiddleware<UserAgentValidationMiddleware>();
    app.UseMiddleware<ProblemDetailsMiddleware>();

    // Request logging with Serilog (disabled in test mode)
    if (!isTestMode)
    {
        app.UseSerilogRequestLogging(options =>
        {
            // Simplified logging - only log important requests
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                // Don't log successful static file requests
                if (ex == null && httpContext.Response.StatusCode < 400)
                {
                    var path = httpContext.Request.Path.Value ?? "";
                    if (path.StartsWith("/_framework/") || 
                        path.StartsWith("/css/") || 
                        path.StartsWith("/lib/") ||
                        path.EndsWith(".wasm") ||
                        path.EndsWith(".wasm.gz") ||
                        path.EndsWith(".dat") ||
                        path.EndsWith(".dat.gz") ||
                        path.EndsWith(".js.map") ||
                        path.EndsWith(".js.map.gz"))
                    {
                        return LogEventLevel.Debug; // Don't show in console
                    }
                }
                
                if (ex != null || httpContext.Response.StatusCode >= 500)
                    return LogEventLevel.Error;
                    
                if (httpContext.Response.StatusCode >= 400)
                    return LogEventLevel.Warning;
                    
                return LogEventLevel.Information;
            };
            
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "Unknown");
            };
        });
    }

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseWebAssemblyDebugging();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "SeeReview API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "SeeReview API Documentation";
            options.DisplayRequestDuration();
        });
    }

    // Disabled HTTPS redirection for Test environment (E2E tests use HTTP only)
    // Comment out completely to avoid any redirect issues
    // app.UseHttpsRedirection();
    
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    app.UseCors();
    app.UseRateLimiter();

    // Health check endpoints
    app.MapHealthChecks("/api/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";

            var result = JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                duration = report.TotalDuration,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration,
                    exception = e.Value.Exception?.Message,
                    data = e.Value.Data
                })
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(result);
        }
    });

    app.MapHealthChecks("/api/health/live", new HealthCheckOptions
    {
        Predicate = _ => false // No health checks, just returns 200 if app is running
    });

    app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";

            var result = JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString()
                })
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(result);
        }
    });

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
catch (Exception ex)
{
    if (!isTestMode)
    {
        Log.Fatal(ex, "Application terminated unexpectedly");
    }
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
