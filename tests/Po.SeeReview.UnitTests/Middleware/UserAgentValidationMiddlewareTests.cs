using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Po.SeeReview.Api.Middleware;
using Xunit;

namespace Po.SeeReview.UnitTests.Middleware;

public class UserAgentValidationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithValidBrowserUserAgent_AllowsRequest()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        var logger = Mock.Of<ILogger<UserAgentValidationMiddleware>>();
        var middleware = new UserAgentValidationMiddleware(_ => Task.CompletedTask, logger);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.NotEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WithBlockedUserAgent_ReturnsBadRequest()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "curl/8.0";

        var logger = Mock.Of<ILogger<UserAgentValidationMiddleware>>();
        var middleware = new UserAgentValidationMiddleware(_ => Task.CompletedTask, logger);

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
    }
}
