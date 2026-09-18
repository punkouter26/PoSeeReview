using System.Net;
using System.Text;
using PoSeeReview.Api.Middleware;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Answers link-preview crawlers on <c>/comic/{placeId}</c> with a real Open Graph document.
/// <para>
/// Everything else on that route falls through untouched to the SPA fallback, so a human still
/// gets the Blazor client. That is the whole reason this is middleware rather than an endpoint:
/// a mapped endpoint at <c>/comic/{placeId}</c> would out-rank <c>MapFallbackToFile</c> and this
/// class would then own serving <c>index.html</c> — including reproducing how static web assets
/// resolve it in Development, which it has no business knowing.
/// </para>
/// <para>
/// It runs before <c>UseRateLimiter</c> and before authentication on purpose. A 429 or a 401 on a
/// preview fetch produces the same broken card as no tags at all, and the page is public anyway.
/// </para>
/// </summary>
internal sealed class SocialPreviewMiddleware(RequestDelegate next, ILogger<SocialPreviewMiddleware> logger)
{
    private const string RoutePrefix = "/comic/";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryGetPlaceId(context.Request, out var placeId)
            || !SocialCrawlers.IsSocialCrawler(context.Request.Headers.UserAgent.ToString()))
        {
            await next(context);
            return;
        }

        Comic? comic = null;
        try
        {
            // Resolved here rather than as an InvokeAsync parameter: UseMiddleware injects those
            // from RequestServices before the body runs, which would build the entire scoped
            // comic-generation graph (restaurant service, chat, image and blob clients) for every
            // request this middleware sees — including every static asset, since it is registered
            // ahead of UseStaticFiles.
            var comicRepository = context.RequestServices.GetRequiredService<IComicRepository>();
            comic = await comicRepository.GetByPlaceIdAsync(PlaceId.From(placeId));
        }
        catch (Exception ex)
        {
            // A preview is decoration. Failing to build one must never turn a shared link into an
            // error page, so fall through to the SPA exactly as if no comic were cached.
            logger.LogWarning(ex, "Social preview lookup failed for placeId {PlaceId}", placeId);
        }

        if (comic is null || comic.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("No live comic for placeId {PlaceId}; serving the app shell to the crawler", placeId);
            await next(context);
            return;
        }

        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        var canonicalUrl = $"{origin}{RoutePrefix}{Uri.EscapeDataString(placeId)}";
        // Our own card, not the blob. The blob URL carries a SAS signature that lapses in about
        // a week, so every share older than that unfurled as an empty card; this one is composed
        // on demand from whatever the comic still is.
        var cardUrl = $"{origin}/share/{Uri.EscapeDataString(placeId)}/card.png";

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        // Comics expire in 24h, so let a crawler re-fetch rather than pin a card to a comic that
        // may already be gone. og:image no longer needs this protection — it points at
        // /share/{placeId}/card.png on this origin instead of a SAS-signed blob URL that used to
        // 403 the moment its signature lapsed — but the tags themselves still go stale.
        context.Response.Headers.CacheControl = "public, max-age=900";

        await context.Response.WriteAsync(BuildDocument(comic, canonicalUrl, cardUrl), Encoding.UTF8);
    }

    private static bool TryGetPlaceId(HttpRequest request, out string placeId)
    {
        placeId = string.Empty;

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var path = request.Path.Value;
        if (path is null || !path.StartsWith(RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Exactly one segment after /comic/ — /comic/abc/anything is not a comic page.
        var candidate = path[RoutePrefix.Length..].Trim('/');
        if (candidate.Length == 0 || candidate.Contains('/'))
        {
            return false;
        }

        placeId = Uri.UnescapeDataString(candidate);
        return true;
    }

    /// <summary>
    /// Both the restaurant name and the narrative are derived from third-party review text, so
    /// every interpolation here is HTML-encoded. An unescaped quote in a restaurant name would
    /// break out of a <c>content="..."</c> attribute.
    /// </summary>
    private static string BuildDocument(Comic comic, string canonicalUrl, string cardUrl)
    {
        var title = Encode($"{comic.RestaurantName} — strangeness {comic.StrangenessScore}/100");
        var description = Encode(Summarize(comic.Narrative));
        var image = Encode(cardUrl);
        var url = Encode(canonicalUrl);
        var name = Encode(comic.RestaurantName);

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{title}</title>
            <link rel="canonical" href="{url}">
            <meta name="description" content="{description}">
            <meta property="og:type" content="article">
            <meta property="og:site_name" content="PoSeeReview">
            <meta property="og:title" content="{title}">
            <meta property="og:description" content="{description}">
            <meta property="og:url" content="{url}">
            <meta property="og:image" content="{image}">
            <meta property="og:image:width" content="1200">
            <meta property="og:image:height" content="630">
            <meta property="og:image:alt" content="Strangeness score card for {name}, drawn from its reviews">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{title}">
            <meta name="twitter:description" content="{description}">
            <meta name="twitter:image" content="{image}">
            </head>
            <body>
            <h1>{title}</h1>
            <p>{description}</p>
            <p><a href="{url}">Open this comic on PoSeeReview</a></p>
            </body>
            </html>
            """;
    }

    /// <summary>Open Graph descriptions are truncated by most clients around 200 characters.</summary>
    private static string Summarize(string narrative)
    {
        var text = narrative.Trim();
        if (text.Length == 0)
        {
            return "A four-panel comic strip drawn from real restaurant reviews.";
        }

        return text.Length <= 200 ? text : text[..200].TrimEnd() + "…";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>Registration helper so <c>Program.cs</c> reads as pipeline steps, not type names.</summary>
internal static class SocialPreviewMiddlewareExtensions
{
    public static IApplicationBuilder UseSocialPreview(this IApplicationBuilder app) =>
        app.UseMiddleware<SocialPreviewMiddleware>();
}
