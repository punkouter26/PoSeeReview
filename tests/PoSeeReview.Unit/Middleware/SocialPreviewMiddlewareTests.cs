using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Middleware;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using Xunit;

namespace PoSeeReview.Unit.Middleware;

/// <summary>
/// The preview document interpolates a restaurant name and a narrative that both originate in
/// third-party review text, and it is the one place in the app that emits raw HTML. It also has
/// to stay invisible to real users: anything it serves to a browser replaces the SPA.
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class SocialPreviewMiddlewareTests
{
    private const string CrawlerUserAgent = "facebookexternalhit/1.1";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/131.0";

    private static (DefaultHttpContext Context, MemoryStream Body) NewRequest(string path, string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("poseereview.example");
        context.Request.Path = path;
        context.Request.Headers.UserAgent = userAgent;

        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static (SocialPreviewMiddleware Middleware, GetCachedComicQueryHandler Handler) Build(
        Comic? comic, Action onNext)
    {
        var generation = new Mock<IComicGenerationService>();
        generation
            .Setup(x => x.GetCachedComicAsync(It.IsAny<PlaceId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(comic);

        var middleware = new SocialPreviewMiddleware(
            _ => { onNext(); return Task.CompletedTask; },
            NullLogger<SocialPreviewMiddleware>.Instance);

        return (middleware, new GetCachedComicQueryHandler(generation.Object));
    }

    private static Task RunAsync(Comic? comic, DefaultHttpContext context, Action onNext)
    {
        var (middleware, handler) = Build(comic, onNext);
        return middleware.InvokeAsync(context, handler);
    }

    private static Comic LiveComic(string restaurantName = "The Owl Cafe", string narrative = "An owl judged the soup.") => new()
    {
        Id = ComicId.From("comic-1"),
        PlaceId = PlaceId.From("place-1"),
        RestaurantName = restaurantName,
        Narrative = narrative,
        StrangenessScore = 87,
        ImageUrl = "https://example.blob.core.windows.net/comics/comic-1.png?sig=abc",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    [Fact]
    public async Task Crawler_OnLiveComic_ServesOpenGraphDocument()
    {
        var (context, body) = NewRequest("/comic/place-1", CrawlerUserAgent);
        var nextCalled = false;

        await RunAsync(LiveComic(), context, () => nextCalled = true);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("text/html", context.Response.ContentType, StringComparison.Ordinal);

        var html = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("og:image", html, StringComparison.Ordinal);
        Assert.Contains("https://example.blob.core.windows.net/comics/comic-1.png?sig=abc", html, StringComparison.Ordinal);
        Assert.Contains("summary_large_image", html, StringComparison.Ordinal);
        Assert.Contains("https://poseereview.example/comic/place-1", html, StringComparison.Ordinal);
        Assert.Contains("strangeness 87/100", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_OnComicRoute_FallsThroughToTheSpa()
    {
        // A human must still get the Blazor client; the preview is for bots only.
        var (context, body) = NewRequest("/comic/place-1", BrowserUserAgent);
        var nextCalled = false;

        await RunAsync(LiveComic(), context, () => nextCalled = true);

        Assert.True(nextCalled);
        Assert.Empty(body.ToArray());
    }

    [Fact]
    public async Task Crawler_WithNoLiveComic_FallsThroughInsteadOfErroring()
    {
        var (context, body) = NewRequest("/comic/place-1", CrawlerUserAgent);
        var nextCalled = false;

        await RunAsync(null, context, () => nextCalled = true);

        Assert.True(nextCalled);
        Assert.Empty(body.ToArray());
    }

    [Fact]
    public async Task Crawler_OnNonComicRoute_FallsThrough()
    {
        var (context, _) = NewRequest("/leaderboard", CrawlerUserAgent);
        var nextCalled = false;

        await RunAsync(LiveComic(), context, () => nextCalled = true);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Crawler_WithHostileRestaurantName_EscapesItIntoTheAttribute()
    {
        // Restaurant names come from Google Places. An unescaped quote would close the
        // content="..." attribute and let review-derived text inject markup.
        var hostile = "Bob's \"Diner\" <script>alert(1)</script>";
        var (context, body) = NewRequest("/comic/place-1", CrawlerUserAgent);

        await RunAsync(LiveComic(restaurantName: hostile), context, () => { });

        var html = Encoding.UTF8.GetString(body.ToArray());
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;Diner&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Crawler_OnNestedComicPath_FallsThrough()
    {
        var (context, _) = NewRequest("/comic/place-1/extra", CrawlerUserAgent);
        var nextCalled = false;

        await RunAsync(LiveComic(), context, () => nextCalled = true);

        Assert.True(nextCalled);
    }
}

/// <summary>
/// Crawlers were previously rejected at 400 by <see cref="UserAgentValidationMiddleware"/>,
/// which is why shared links unfurled blank. They are now allowed through — but only on page
/// routes, never on the API that spends money.
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class SocialCrawlerUserAgentTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(string path, string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Headers.UserAgent = userAgent;

        var middleware = new UserAgentValidationMiddleware(
            _ => Task.CompletedTask,
            NullLogger<UserAgentValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData("facebookexternalhit/1.1")]
    [InlineData("Twitterbot/1.0")]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)")]
    [InlineData("Discordbot/2.0")]
    public async Task PageRoute_AllowsKnownPreviewCrawlers(string userAgent)
    {
        var context = await InvokeAsync("/comic/place-1", userAgent);

        Assert.NotEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_StillRejectsPreviewCrawlers()
    {
        var context = await InvokeAsync("/api/comics/place-1", "facebookexternalhit/1.1");

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task PageRoute_StillRejectsScrapers()
    {
        var context = await InvokeAsync("/comic/place-1", "python-requests/2.31.0");

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }
}
