using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Po.SeeReview.Api.Middleware;

/// <summary>
/// Middleware to handle exceptions and return RFC 7807 Problem Details responses
/// </summary>
public class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Title = "An error occurred while processing your request",
            Status = (int)HttpStatusCode.InternalServerError,
            Instance = context.Request.Path
        };

        // Add exception details in development environment
        if (_environment.IsDevelopment())
        {
            problemDetails.Detail = exception.Message;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }
        else
        {
            problemDetails.Detail = "An unexpected error occurred. Please try again later.";
        }

        // Map specific exception types to appropriate status codes
        problemDetails.Status = exception switch
        {
            ArgumentNullException => (int)HttpStatusCode.BadRequest,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            InvalidOperationException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            KeyNotFoundException => (int)HttpStatusCode.NotFound,
            _ => (int)HttpStatusCode.InternalServerError
        };

        // Update problem type based on status code
        problemDetails.Type = problemDetails.Status switch
        {
            400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            401 => "https://tools.ietf.org/html/rfc7235#section-3.1",
            404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = problemDetails.Status.Value;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, options));
    }
}
