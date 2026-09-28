using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using PoSeeReview.Api.Caching;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Client.Components;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using Xunit;

namespace PoSeeReview.Unit.Features;

/// <summary>
/// Panel captions survive storage and reach the alt text; the shared read cache never stores a
/// failure or a cookie.
/// </summary>
public sealed class ComicCaptionsAndCachingTests
{
    [Fact]
    public void ComicEntity_RoundTripsCaptions_AndOlderRowsReadAsNone()
    {
        var comic = new Comic
        {
            PlaceId = PlaceId.From("place-1"),
            Captions = ["A dog orders soup.", "The soup, \"orders\" back."]
        };

        Assert.Equal(comic.Captions, ComicEntity.FromDomain(comic).ToDomain().Captions);
        Assert.Empty(new ComicEntity { PlaceId = "place-1", CaptionsJson = "" }.ToDomain().Captions);
        Assert.Empty(new ComicEntity { PlaceId = "place-1", CaptionsJson = "{not json" }.ToDomain().Captions);
    }

    [Fact]
    public void AltText_ReadsPanelsInOrder_AndFallsBackToTitle()
    {
        Assert.Equal(
            "Comic strip for Joe's. Panel 1: A dog orders soup. Panel 2: The chef barks.",
            ComicStrip.AltText("Joe's", ["A dog orders soup.", " ", "The chef barks."]));

        Assert.Equal("Comic strip for Joe's", ComicStrip.AltText("Joe's", []));
    }

    [Theory]
    [InlineData(200, false, true)]
    [InlineData(500, false, false)]
    [InlineData(200, true, false)]
    public async Task SharedReadCachePolicy_StoresOnlyCookieFreeSuccesses(int status, bool setsCookie, bool stored)
    {
        IOutputCachePolicy policy = new SharedReadCachePolicy();
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        var context = new OutputCacheContext { HttpContext = http };

        await policy.CacheRequestAsync(context, CancellationToken.None);
        http.Response.StatusCode = status;
        if (setsCookie)
        {
            http.Response.Headers.SetCookie = ".PoSeeReview.Auth=renewed";
        }
        await policy.ServeResponseAsync(context, CancellationToken.None);

        Assert.True(context.AllowCacheLookup);
        Assert.Equal(stored, context.AllowCacheStorage);
    }
}
