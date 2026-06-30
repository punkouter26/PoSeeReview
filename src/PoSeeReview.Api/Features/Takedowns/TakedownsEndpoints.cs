using FluentValidation;
using Microsoft.ApplicationInsights;
using PoSeeReview.Api.Middleware;
using PoSeeReview.Core.Interfaces;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Takedowns;

/// <summary>
/// Content takedown slice. Maps <c>/api/takedowns</c>, guarded by an X-Api-Key filter
/// (NET_RULES 3.3). Validation via FluentValidation (NET_RULES 2.2).
/// </summary>
internal static class TakedownsEndpoints
{
    public static IEndpointRouteBuilder MapTakedownEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/takedowns").WithTags("Takedowns")
            .AddEndpointFilter(new ApiKeyEndpointFilter(
                app.ServiceProvider.GetRequiredService<IConfiguration>(),
                app.ServiceProvider.GetRequiredService<ILogger<ApiKeyEndpointFilter>>())
            { ConfigurationKey = "Takedowns:ApiKey" });

        group.MapPost("", SubmitAsync);

        return app;
    }

    internal static async Task<IResult> SubmitAsync(
        TakedownRequestDto request,
        IValidator<TakedownRequestDto> validator,
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ILeaderboardRepository leaderboardRepository,
        ILogger<TakedownRequestDto> logger,
        TelemetryClient telemetryClient,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        logger.LogInformation("Received takedown request for {PlaceId} from {Requester}", request.PlaceId, request.RequesterName);

        telemetryClient.TrackEvent("TakedownRequestReceived", new Dictionary<string, string>
        {
            ["PlaceId"] = request.PlaceId,
            ["Requester"] = request.RequesterName,
            ["Email"] = request.ContactEmail,
            ["Region"] = request.Region
        });

        var existingComic = await comicRepository.GetByPlaceIdAsync(request.PlaceId);
        if (existingComic != null)
        {
            await comicRepository.DeleteAsync(request.PlaceId);

            if (!string.IsNullOrWhiteSpace(existingComic.Id))
            {
                await blobStorageService.DeleteComicImageAsync(existingComic.Id);
            }

            await leaderboardRepository.DeleteAsync(request.PlaceId, request.Region);
            logger.LogInformation("Removed cached comic and leaderboard entry for {PlaceId}", request.PlaceId);
        }
        else
        {
            logger.LogInformation("No cached comic found for {PlaceId} during takedown", request.PlaceId);
        }

        return Results.Accepted(value: new
        {
            message = "Your takedown request was received. Our team will follow up via email within 2 business days.",
            requestId = Guid.NewGuid()
        });
    }
}
