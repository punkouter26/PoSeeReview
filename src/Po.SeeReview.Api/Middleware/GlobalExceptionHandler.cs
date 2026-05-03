using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Po.SeeReview.Api.Middleware;

/// <summary>
/// .NET 10 IExceptionHandler implementation that produces RFC 7807 Problem Details responses
/// for unhandled exceptions. Replaces the legacy ProblemDetailsMiddleware pipeline component.
/// Registered via builder.Services.AddExceptionHandler and app.UseExceptionHandler.
/// </summary>
internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception occurred. TraceId={TraceId}", httpContext.TraceIdentifier);

        var (statusCode, problemType) = exception switch
        {
            ArgumentNullException => (HttpStatusCode.BadRequest, "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            ArgumentException => (HttpStatusCode.BadRequest, "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            InvalidOperationException => (HttpStatusCode.BadRequest, "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "https://tools.ietf.org/html/rfc7235#section-3.1"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            _ => (HttpStatusCode.InternalServerError, "https://tools.ietf.org/html/rfc7231#section-6.6.1")
        };

        var problemDetails = new ProblemDetails
        {
            Type = problemType,
            Title = "An error occurred while processing your request",
            Status = (int)statusCode,
            Instance = httpContext.Request.Path
        };

        if (environment.IsDevelopment())
        {
            problemDetails.Detail = exception.Message;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }
        else
        {
            problemDetails.Detail = "An unexpected error occurred. Please try again later.";
        }

        httpContext.Response.StatusCode = (int)statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
