using System.Security.Claims;
using System.Threading.RateLimiting;

namespace PoSeeReview.Api.Middleware;

internal static class RateLimitingServiceCollectionExtensions
{
    internal static IServiceCollection AddConfiguredRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned by client IP. The limit has to clear normal browsing for a whole
            // NAT'd office sharing one egress IP, not just one user: every SPA route change
            // costs a handful of requests. 60/min was roughly ten page views and throttled
            // real sessions. The SPA document and /auth/* opt out entirely (see Program.cs).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                PerMinute(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    configuration.GetValue("RateLimiting:GlobalPermitLimit", 240)));

            // Per caller, not app-wide: a single shared bucket let one user at 3 requests a
            // minute lock generation for everybody else.
            options.AddPolicy("comics-post", context =>
                PerMinute(PartitionKey(context), configuration.GetValue("RateLimiting:ComicsPostPermitLimit", 3)));

            // Reporting is cheap for the server and must stay available to an angry user with a
            // real complaint, but it writes a row per call and is worth a ceiling.
            options.AddPolicy("reports-post", context =>
                PerMinute(PartitionKey(context), configuration.GetValue("RateLimiting:ReportsPostPermitLimit", 10)));

            options.OnRejected = (context, _) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiter");

                logger.LogWarning(
                    "Rate limit exceeded for {IpAddress}",
                    context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>
    /// The authenticated user, falling back to the remote IP. The limiter runs after
    /// authentication, so the principal is already populated.
    /// </summary>
    internal static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}";

    private static RateLimitPartition<string> PerMinute(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
