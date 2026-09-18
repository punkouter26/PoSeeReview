using System.Text.RegularExpressions;
using Azure;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Leaderboard;

/// <summary>
/// Global strangeness leaderboard slice. Maps <c>/api/leaderboard</c> (NET_RULES 3.3).
/// </summary>
internal static partial class LeaderboardEndpoints
{
    /// <summary>
    /// Accepted region shape: a country code optionally narrowed by subdivisions
    /// (<c>US</c>, <c>US-WA</c>, <c>US-WA-Seattle</c>). Source-generated and shared by both
    /// handlers rather than re-parsed per request.
    /// </summary>
    [GeneratedRegex(@"^[A-Z]{2}(-[A-Z0-9]+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex RegionFormatRegex();

    private static Regex RegionFormat => RegionFormatRegex();

    public static IEndpointRouteBuilder MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/leaderboard").WithTags("Leaderboard");

        group.MapGet("", GetLeaderboard);

        return app;
    }

    private static async Task<IResult> GetLeaderboard(
        ILeaderboardService leaderboardService,
        ILogger<ILeaderboardService> logger,
        int limit = 10,
        string? region = null)
    {
        // No region is now the worldwide board — the page has no picker to send one. A scoped
        // board stays available by URL (?region=US). RegionCode.From maps empty to US, which is
        // exactly the wrong default here, so the emptiness is resolved before it reaches the id.
        var isGlobal = string.IsNullOrWhiteSpace(region);

        if (!isGlobal && !RegionFormat.IsMatch(region!))
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
            logger.LogInformation("Fetching leaderboard for {Scope} with limit {Limit}",
                isGlobal ? "all regions" : $"region {region}", limit);

            var regionCode = isGlobal ? new RegionCode(string.Empty) : RegionCode.From(region);
            var entries = await leaderboardService.GetTopComicsAsync(regionCode, limit);

            var response = new LeaderboardResponse
            {
                Region = isGlobal ? "ALL" : region!.ToUpperInvariant(),
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
