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
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue<int>("RateLimiting:GlobalPermitLimit", 60),
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
