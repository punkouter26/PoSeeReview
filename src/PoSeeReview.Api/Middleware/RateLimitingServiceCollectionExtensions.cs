using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue<int>("RateLimiting:GlobalPermitLimit", 240),
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });

            // Stricter per-endpoint limiter for the expensive comic-generation endpoint.
            options.AddFixedWindowLimiter("comics-post", limiterOptions =>
            {
                limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:ComicsPostPermitLimit", 3);
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
                limiterOptions.AutoReplenishment = true;
            });

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
}
