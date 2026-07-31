using System.Text.RegularExpressions;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Leaderboard;

/// <summary>
/// Global strangeness leaderboard slice. Maps <c>/api/leaderboard</c> (NET_RULES 3.3).
/// </summary>
internal static class LeaderboardEndpoints
{
    public static IEndpointRouteBuilder MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/leaderboard").WithTags("Leaderboard")
            .MapGet("", GetLeaderboard);

        return app;
    }

    private static async Task<IResult> GetLeaderboard(
        ILeaderboardService leaderboardService,
        ILogger<ILeaderboardService> logger,
        string region = "US",
        int limit = 10)
    {
        // An explicitly-empty region (?region=) falls back to the default rather than 400.
        if (string.IsNullOrWhiteSpace(region))
        {
            region = "US";
        }

        if (!Regex.IsMatch(region, @"^[A-Z]{2}(-[A-Z0-9]+)*$", RegexOptions.IgnoreCase))
        {
            logger.LogWarning("GetLeaderboard called with invalid region format: {Region}", region);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid region",
                detail: $"Region '{region}' has invalid format. Must start with a 2-letter country code (e.g., US, GB, US-WA).");
        }

        if (limit < 1 || limit > 50)
        {
            logger.LogWarning("GetLeaderboard called with invalid limit: {Limit}", limit);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid limit", detail: "Limit must be between 1 and 50");
        }

        try
        {
            logger.LogInformation("Fetching leaderboard for region {Region} with limit {Limit}", region, limit);

            var entries = await leaderboardService.GetTopComicsAsync(RegionCode.From(region), limit);

            var response = new LeaderboardResponse
            {
                Region = region.ToUpperInvariant(),
                Entries = entries.Select(e => new LeaderboardEntryDto
                {
                    Rank = e.Rank,
                    PlaceId = e.PlaceId.Value,
                    RestaurantName = e.RestaurantName,
                    Address = e.Address,
                    Region = e.Region.Value,
                    StrangenessScore = e.StrangenessScore,
                    ComicBlobUrl = e.ComicBlobUrl,
                    LastUpdated = e.LastUpdated
                }).ToList(),
                LastUpdated = DateTimeOffset.UtcNow
            };

            logger.LogInformation("Retrieved {Count} leaderboard entries for region {Region}", entries.Count, region);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving leaderboard for region {Region}", region);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal server error", detail: "An error occurred while retrieving the leaderboard");
        }
    }
}
