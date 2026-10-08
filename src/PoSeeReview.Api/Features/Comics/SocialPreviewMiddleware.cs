using System.Net;
using System.Text;
using PoSeeReview.Api.Middleware;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Answers link-preview crawlers on <c>/comic/{placeId}</c> and <c>/duel/{a}/{b}</c> with a real
/// Open Graph document.
/// <para>
/// Everything else on those routes falls through untouched to the SPA fallback, so a human still
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
    public async Task InvokeAsync(HttpContext context)
    {
        if (RoutePlaceIds(context.Request) is not { } placeIds
            || !SocialCrawlers.IsSocialCrawler(context.Request.Headers.UserAgent.ToString()))
        {
            await next(context);
            return;
        }

        var comics = new List<Comic>(placeIds.Length);
        try
        {
            // Resolved here rather than as InvokeAsync parameters: UseMiddleware injects those
            // from RequestServices before the body runs, which would build scoped services for
            // every request this middleware sees — including every static asset, since it is
            // registered ahead of UseStaticFiles.
            var comicRepository = context.RequestServices.GetRequiredService<IComicRepository>();
            var moderationGate = context.RequestServices.GetRequiredService<IContentModerationGate>();

            foreach (var placeId in placeIds.Select(PlaceId.From))
            {
                // A withheld comic's name and narrative must not reach a crawler either. The
                // gate fails open on a storage error, like every other read of it.
                if ((await moderationGate.EvaluateAsync(placeId, context.RequestAborted)).IsServable
                    && await comicRepository.GetByPlaceIdAsync(placeId) is { } comic
                    && comic.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    comics.Add(comic);
                }
            }
        }
        catch (Exception ex)
        {
            // A preview is decoration. Failing to build one must never turn a shared link into an
            // error page, so fall through to the SPA exactly as if no comic were cached.
            logger.LogWarning(ex, "Social preview lookup failed for {Path}", context.Request.Path);
        }

        if (comics.Count != placeIds.Length)
        {
            logger.LogInformation("No live comic for {Path}; serving the app shell to the crawler", context.Request.Path);
            await next(context);
            return;
        }

        var origin = $"{context.Request.Scheme}://{context.Request.Host}";
        var idPath = string.Join('/', placeIds.Select(Uri.EscapeDataString));

        // Our own card, not the blob. The blob URL carries a SAS signature that lapses in about
        // a week, so every share older than that unfurled as an empty card; this one is composed
        // on demand from whatever the comic still is.
        var document = comics is [var single]
            ? BuildDocument(
                title: $"{single.RestaurantName} — strangeness {single.StrangenessScore}/100",
                description: Summarize(single.Narrative),
                pageUrl: $"{origin}/comic/{idPath}",
                cardUrl: $"{origin}/share/{idPath}/card.png",
                imageAlt: $"Strangeness score card for {single.RestaurantName}, drawn from its reviews",
                linkText: "Open this comic on PoSeeReview")
            : BuildDocument(
                title: $"{comics[0].RestaurantName} vs {comics[1].RestaurantName} — strangeness duel",
                description: $"{comics[0].RestaurantName} scored {comics[0].StrangenessScore}/100 and "
                    + $"{comics[1].RestaurantName} scored {comics[1].StrangenessScore}/100. Whose reviews are stranger?",
                pageUrl: $"{origin}/duel/{idPath}",
                cardUrl: $"{origin}/share/duel/{idPath}/card.png",
                imageAlt: $"Strangeness duel card: {comics[0].RestaurantName} against {comics[1].RestaurantName}",
                linkText: "Open this duel on PoSeeReview");

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        // Comics expire in 24h, so let a crawler re-fetch rather than pin a card to a comic that
        // may already be gone.
        context.Response.Headers.CacheControl = "public, max-age=900";

        await context.Response.WriteAsync(document, Encoding.UTF8);
    }

    /// <summary>
    /// The place ids a preview route names — one after <c>/comic/</c>, two after <c>/duel/</c> —
    /// or null when this is not such a route. An id that is not shaped like one is not a route.
    /// </summary>
    private static string[]? RoutePlaceIds(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return null;
        }

        var path = request.Path.Value ?? string.Empty;
        var (prefix, expected) = path.StartsWith("/comic/", StringComparison.OrdinalIgnoreCase) ? ("/comic/", 1)
            : path.StartsWith("/duel/", StringComparison.OrdinalIgnoreCase) ? ("/duel/", 2)
            : (string.Empty, 0);

        if (expected == 0)
        {
            return null;
        }

        string[] ids = [.. path[prefix.Length..].Trim('/').Split('/').Select(Uri.UnescapeDataString)];

        return ids.Length == expected && ids.All(PlaceId.IsValid) ? ids : null;
    }

    /// <summary>
    /// Restaurant names and narratives are derived from third-party review text, so every
    /// interpolation here is HTML-encoded. An unescaped quote in a restaurant name would break
    /// out of a <c>content="..."</c> attribute.
    /// </summary>
    private static string BuildDocument(
        string title, string description, string pageUrl, string cardUrl, string imageAlt, string linkText)
    {
        title = Encode(title);
        description = Encode(description);
        var image = Encode(cardUrl);
        var url = Encode(pageUrl);

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
            <meta property="og:image:alt" content="{Encode(imageAlt)}">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{title}">
            <meta name="twitter:description" content="{description}">
            <meta name="twitter:image" content="{image}">
            </head>
            <body>
            <h1>{title}</h1>
            <p>{description}</p>
            <p><a href="{url}">{Encode(linkText)}</a></p>
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
            return "A comic strip drawn from real restaurant reviews.";
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
