using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Unit.Utilities;

public class HttpContextRequestIdentityAccessorTests
{
    private static readonly IHostEnvironment DevEnv =
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development);

    private static readonly IHostEnvironment ProdEnv =
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production);

    [Fact]
    public void GetCurrentUserId_UsesCanonicalDevHeader_WhenPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-User-Id"] = "ANON463443";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context }, DevEnv);

        var userId = accessor.GetCurrentUserId();

        Assert.Equal(UserId.From("ANON463443"), userId);
    }

    [Fact]
    public void GetCurrentUserId_UsesLegacyDevHeader_WhenCanonicalNotPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-UserId"] = "ANON999111";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context }, DevEnv);

        var userId = accessor.GetCurrentUserId();

        Assert.Equal(UserId.From("ANON999111"), userId);
    }

    [Fact]
    public void GetCurrentUserId_PrefersCanonicalOverLegacy_WhenBothPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Dev-User-Id"] = "ANON111111";
        context.Request.Headers["X-Dev-UserId"] = "ANON222222";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context }, DevEnv);

        var userId = accessor.GetCurrentUserId();

        Assert.Equal(UserId.From("ANON111111"), userId);
    }

    [Fact]
    public void GetCurrentUserId_ReturnsAnonymous_WhenNoContextOrIdentity()
    {
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor(), DevEnv);

        var userId = accessor.GetCurrentUserId();

        Assert.Equal(UserId.From("anonymous"), userId);
    }

    [Fact]
    public void GetCurrentUserId_IgnoresDevHeader_InProduction_UsesAuthenticatedPrincipal()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "real-user")], "cookie"))
        };
        context.Request.Headers["X-Dev-User-Id"] = "spoofed-user";
        var accessor = new HttpContextRequestIdentityAccessor(new HttpContextAccessor { HttpContext = context }, ProdEnv);

        var userId = accessor.GetCurrentUserId();

        Assert.Equal(UserId.From("real-user"), userId);
    }
}
