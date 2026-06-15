using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Api.Telemetry;
using Po.SeeReview.Core;

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
        // Classify the failure by exception type → provider/phase for App Insights slicing.
        // Zero-allocation: all templates are static and tags are KeyValuePair arrays.
        var (provider, phase, errorType) = Classify(exception);

        logger.LogError(
            exception,
            "Unhandled exception. TraceId={TraceId} Provider={Provider} Phase={Phase} ErrorType={ErrorType}",
            httpContext.TraceIdentifier,
            provider,
            phase,
            errorType);

        // Record on the same meter the controller uses so the dashboard doesn't need a new column.
        PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
        {
            new KeyValuePair<string, object?>("error_type", errorType),
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("phase", phase)
        });

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
            Instance = httpContext.Request.Path,
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred. Please try again later."
        };

        // Surface the classification in the response body so Blazor's TryExtractProblemDetail
        // can include it in the user-facing toast (hypothesis #10).
        problemDetails.Extensions["errorType"] = errorType;
        problemDetails.Extensions["provider"] = provider;
        problemDetails.Extensions["phase"] = phase;

        if (environment.IsDevelopment())
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }

        httpContext.Response.StatusCode = (int)statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    /// <summary>
    /// Map an exception to a (provider, phase, error_type) tuple. Walks the InnerException chain
    /// once — Polly-retry-wrapped AI calls usually have the original SDK exception two levels down.
    /// </summary>
    private static (string Provider, string Phase, string ErrorType) Classify(Exception exception)
    {
        var ex = exception;
        for (var i = 0; i < 4 && ex is not null; i++, ex = ex.InnerException)
        {
            var name = ex.GetType().FullName ?? string.Empty;

            if (name.StartsWith("Azure.AI.OpenAI", StringComparison.Ordinal)
                || name.StartsWith("OpenAI", StringComparison.Ordinal))
            {
                return ("azure-openai", "strangeness", name);
            }

            if (name.StartsWith("Google", StringComparison.Ordinal)
                || name.Contains("Gemini", StringComparison.Ordinal))
            {
                return ("gemini", "imagegen", name);
            }

            if (name.Contains("Json", StringComparison.Ordinal))
            {
                return ("schema", "deserialize", name);
            }
        }

        return exception switch
        {
            KeyNotFoundException => ("restaurant", "lookup", "restaurant_not_found"),
            InsufficientReviewsException => ("restaurant", "validate", "insufficient_reviews"),
            ArgumentException or ArgumentNullException or InvalidOperationException => ("api", "validate", "bad_request"),
            UnauthorizedAccessException => ("api", "auth", "unauthorized"),
            _ => ("unknown", "unhandled", exception.GetType().Name)
        };
    }
}
