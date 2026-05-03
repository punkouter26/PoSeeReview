using Microsoft.AspNetCore.Http;
using Po.SeeReview.Api.Identity;

namespace Po.SeeReview.UnitTests.Utilities;

public class HttpContextRequestIdentityAccessorTests
{
    [Fact]
    public void GetCurrentUserId_UsesCanonicalDevHeader_WhenPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-User-Id"] = "ANON463443";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context });

        var userId = accessor.GetCurrentUserId();

        Assert.Equal("ANON463443", userId);
    }

    [Fact]
    public void GetCurrentUserId_UsesLegacyDevHeader_WhenCanonicalNotPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-UserId"] = "ANON999111";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context });

        var userId = accessor.GetCurrentUserId();

        Assert.Equal("ANON999111", userId);
    }

    [Fact]
    public void GetCurrentUserId_PrefersCanonicalOverLegacy_WhenBothPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-User-Id"] = "ANON111111";
        context.Request.Headers["X-Dev-UserId"] = "ANON222222";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context });

        var userId = accessor.GetCurrentUserId();

        Assert.Equal("ANON111111", userId);
    }

    [Fact]
    public void GetCurrentUserId_ReturnsAnonymous_WhenNoContextOrIdentity()
    {
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor());

        var userId = accessor.GetCurrentUserId();

        Assert.Equal("anonymous", userId);
    }
}
