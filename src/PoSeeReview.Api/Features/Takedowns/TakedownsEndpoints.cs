using FluentValidation;
using Microsoft.ApplicationInsights;
using PoSeeReview.Api.Middleware;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Takedowns;

/// <summary>
/// Content takedown slice. Maps <c>/api/takedowns</c>, guarded by an X-Api-Key filter
/// (NET_RULES 3.3). Validation via FluentValidation (NET_RULES 2.2).
/// </summary>
internal static class TakedownsEndpoints
{
    public static IEndpointRouteBuilder MapTakedownEndpoints(this IEndpointRouteBuilder app)
    {
        // Guarded by its own X-Api-Key filter rather than a user session, so it opts out of
        // the cookie-based fallback policy.
        var group = app.MapGroup("/api/takedowns").WithTags("Takedowns")
            .AllowAnonymous()
            .AddEndpointFilter(new ApiKeyEndpointFilter(
                app.ServiceProvider.GetRequiredService<IConfiguration>(),
                app.ServiceProvider.GetRequiredService<ILogger<ApiKeyEndpointFilter>>())
            { ConfigurationKey = TakedownOptions.ApiKeyConfigurationKey });

        group.MapPost("", SubmitAsync);

        return app;
    }

    internal static async Task<IResult> SubmitAsync(
        TakedownRequestDto request,
        IValidator<TakedownRequestDto> validator,
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ILeaderboardRepository leaderboardRepository,
        IHallOfFameArchive hallOfFameArchive,
        ILogger<TakedownRequestDto> logger,
        TelemetryClient telemetryClient,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        // Do not log/telemeter requester name or contact email — they are PII (NET_RULES 5.1/6.1).
        logger.LogInformation("Received takedown request for {PlaceId} in {Region}", request.PlaceId, request.Region);

        telemetryClient.TrackEvent("TakedownRequestReceived", new Dictionary<string, string>
        {
            ["PlaceId"] = request.PlaceId,
            ["Region"] = request.Region
        });

        var placeId = PlaceId.From(request.PlaceId);
        var region = RegionCode.From(request.Region);

        var existingComic = await comicRepository.GetByPlaceIdAsync(placeId);
        if (existingComic != null)
        {
            await comicRepository.DeleteAsync(placeId);

            if (!existingComic.Id.IsEmpty)
            {
                await blobStorageService.DeleteComicImageAsync(existingComic.Id.Value);
            }

            await leaderboardRepository.DeleteAsync(placeId, region);
            logger.LogInformation("Removed cached comic and leaderboard entry for {PlaceId}", request.PlaceId);
        }
        else
        {
            logger.LogInformation("No cached comic found for {PlaceId} during takedown", request.PlaceId);
        }

        // Runs whether or not a live comic existed. The weekly archive is built to outlive the
        // 24-hour comic, so it is precisely the copy that survives when everything else has
        // already expired — and leaving it would mean a completed takedown that still shows the
        // restaurant's name and score on a page designed never to expire.
        await hallOfFameArchive.DeleteAllForPlaceAsync(placeId, cancellationToken);

        return Results.Accepted(value: new
        {
            message = "Your takedown request was received. Our team will follow up via email within 2 business days.",
            requestId = Guid.NewGuid()
        });
    }
}
