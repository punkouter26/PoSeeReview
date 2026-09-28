using Microsoft.AspNetCore.OutputCaching;

namespace PoSeeReview.Api.Caching;

/// <summary>
/// Output caching for GETs whose body is the same for every caller (leaderboard, weekly Hall of
/// Fame, insights).
/// <para>
/// The framework's default policy refuses any authenticated request, and deny-by-default auth
/// makes every business request authenticated, so the default would cache nothing here.
/// <c>UseOutputCache</c> runs after <c>UseAuthorization</c>, so a cached body is still only ever
/// served to a caller who passed the gate. Sixty seconds keeps a moderation hide or takedown from
/// lingering past a minute.
/// </para>
/// </summary>
internal sealed class SharedReadCachePolicy : IOutputCachePolicy
{
    public const string Name = "shared-read";

    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);

    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var isGet = HttpMethods.IsGet(context.HttpContext.Request.Method)
                    || HttpMethods.IsHead(context.HttpContext.Request.Method);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = isGet;
        context.AllowCacheStorage = isGet;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Duration;
        context.CacheVaryByRules.QueryKeys = "*";

        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        // Only a success is worth replaying, and never a response that sets a cookie — a renewed
        // session cookie replayed to someone else would be a session handed to a stranger.
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
