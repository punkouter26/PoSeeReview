using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Po.SeeReview.Api;
using Po.SeeReview.Api.Health;
using Po.SeeReview.Api.HostedServices;
using Po.SeeReview.Api.Middleware;
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

    // Configure Azure Key Vault for secrets (all environments)
    // Locally, DefaultAzureCredential delegates to 'az login' session.
    if (!isTestMode)
    {
        try
        {
            // Key Vault URL from environment variable or configuration
            var keyVaultUrl = builder.Configuration["KeyVault:Endpoint"]
                ?? Environment.GetEnvironmentVariable("KeyVault__Endpoint");

            if (!string.IsNullOrEmpty(keyVaultUrl))
            {
                Log.Information("Connecting to Azure Key Vault: {KeyVaultUrl}", keyVaultUrl);

                // When running locally, skip the managed-identity IMDS probe to avoid a
                // ~12-second timeout before falling through to AzureCliCredential.
                var isRunningInAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ExcludeManagedIdentityCredential = !isRunningInAzure,
                };

                var credential = new DefaultAzureCredential(credentialOptions);
                var secretClient = new SecretClient(new Uri(keyVaultUrl), credential);

                // Two-pass loading so app-specific secrets always override shared ones:
                // Pass 1 – shared secrets (AzureOpenAI--, ConnectionStrings--, etc.)
                builder.Configuration.AddAzureKeyVault(secretClient, new SharedKeyVaultSecretManager());
                Log.Information("Key Vault: shared secrets registered");

                // Pass 2 – PoSeeReview-specific secrets (override any shared values)
                builder.Configuration.AddAzureKeyVault(secretClient, new PrefixKeyVaultSecretManager());
                Log.Information("Key Vault: PoSeeReview app-specific secrets registered");
            }
            else
            {
                Log.Warning("KeyVault:Endpoint not configured. Secrets must be provided via environment variables.");
            }
        }
        catch (Exception ex)
        {
            // Don't fail startup if Key Vault is unavailable — log warning and continue.
            // The app can still run with environment variables / appsettings overrides.
            Log.Warning(ex, "Failed to configure Azure Key Vault. Falling back to environment variables and app settings.");
        }
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

    // Configure OpenTelemetry with custom tracing and metrics
    var appInsightsConnectionString = builder.Configuration.GetValue<string>("ApplicationInsights:ConnectionString");
    if (!string.IsNullOrEmpty(appInsightsConnectionString))
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "PoSeeReview.Api", serviceVersion: "1.0.0"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Po.SeeReview.*")
                .AddAzureMonitorTraceExporter(options =>
                {
                    options.ConnectionString = appInsightsConnectionString;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("Po.SeeReview.*")
                .AddAzureMonitorMetricExporter(options =>
                {
                    options.ConnectionString = appInsightsConnectionString;
                }));
    }

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
                policy.WithOrigins("https://posee-review.azurecontainerapps.io")
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
    app.UseMiddleware<UserAgentValidationMiddleware>();
    app.UseMiddleware<ProblemDetailsMiddleware>();

    // Enrich all log entries with correlation ID, user context, and environment
    app.Use(async (ctx, next) =>
    {
        var correlationId = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Activity.Current?.TraceId.ToString()
            ?? ctx.TraceIdentifier;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        using (Serilog.Context.LogContext.PushProperty("UserId", ctx.User?.Identity?.Name ?? "anonymous"))
        using (Serilog.Context.LogContext.PushProperty("SessionId", ctx.Request.Headers["X-Session-ID"].FirstOrDefault() ?? correlationId))
        using (Serilog.Context.LogContext.PushProperty("Environment", app.Environment.EnvironmentName))
        {
            ctx.Response.Headers["X-Correlation-ID"] = correlationId;
            await next();
        }
    });

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
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "Unknown");
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                var userAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault();
                string userAgentValue = userAgent ?? "Unknown";
                diagnosticContext.Set("UserAgent", userAgentValue);
            };
        });
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
