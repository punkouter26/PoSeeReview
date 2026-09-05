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

        // Literal segment, so it is matched ahead of anything parameterised in this group.
        group.MapGet("/weekly", GetWeeklyHallOfFame);

        return app;
    }

    /// <summary>Weeks of archive a single request may ask for.</summary>
    private const int MaxWeeks = 12;

    /// <summary>
    /// The permanent weekly archive. Unlike the live board, these rows outlive the 24-hour
    /// comic they came from — which is the entire point, and also why an entry can carry a
    /// blob URL that no longer resolves.
    /// </summary>
    private static async Task<IResult> GetWeeklyHallOfFame(
        HallOfFameRepository hallOfFame,
        IBlobStorageService blobStorageService,
        ILogger<HallOfFameRepository> logger,
        TimeProvider timeProvider,
        HttpContext http,
        string region = "US",
        int weeks = 4,
        int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            region = "US";
        }

        if (!RegionFormat.IsMatch(region))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid region",
                detail: $"Region '{region}' has invalid format. Must start with a 2-letter country code (e.g., US, GB, US-WA).");
        }

        if (weeks < 1 || weeks > MaxWeeks)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid weeks", detail: $"Weeks must be between 1 and {MaxWeeks}");
        }

        if (limit < 1 || limit > 50)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid limit", detail: "Limit must be between 1 and 50");
        }

        var regionCode = RegionCode.From(region);
        var now = timeProvider.GetUtcNow();
        var response = new HallOfFameResponse { Region = region.ToUpperInvariant() };

        for (var offset = 0; offset < weeks; offset++)
        {
            var instant = now.AddDays(-7 * offset);
            var weekKey = HallOfFameEntity.WeekKeyFor(instant);

            var entities = await hallOfFame.GetWeekAsync(regionCode, weekKey, limit, http.RequestAborted);
            if (entities.Count == 0)
            {
                // Empty weeks are skipped rather than rendered: a column of "nothing happened"
                // headings is worse than a shorter archive.
                continue;
            }

            var week = new HallOfFameWeekDto
            {
                WeekKey = weekKey,
                WeekStart = HallOfFameEntity.WeekStartFor(weekKey)
            };

            var rank = 1;
            foreach (var entity in entities)
            {
                week.Entries.Add(new HallOfFameEntryDto
                {
                    Rank = rank++,
                    PlaceId = entity.PlaceId,
                    RestaurantName = entity.RestaurantName,
                    Address = entity.Address,
                    Region = entity.Region,
                    StrangenessScore = entity.StrangenessScore,
                    ComicBlobUrl = entity.ComicBlobUrl,
                    ArchivedAt = entity.ArchivedAt,
                    ImageExpired = await IsImageGoneAsync(blobStorageService, logger, entity.ComicBlobUrl)
                });
            }

            response.Weeks.Add(week);
        }

        return Results.Ok(response);
    }

    /// <summary>
    /// Whether an archived comic's artwork is still there, so the client can render a score
    /// card instead of a broken image. Only blobs this app uploaded can be probed; anything
    /// else (including seeded test URLs) is assumed present.
    /// </summary>
    private static async Task<bool> IsImageGoneAsync(
        IBlobStorageService blobStorageService,
        ILogger logger,
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        try
        {
            if (!new Uri(url).AbsolutePath.Contains("/comics/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !await blobStorageService.BlobExistsAsync(url);
        }
        catch (Exception ex) when (ex is UriFormatException or RequestFailedException)
        {
            // An unprobeable URL is not evidence of deletion; assume the image is fine and let
            // the browser be the judge.
            logger.LogDebug(ex, "Could not probe archived comic artwork");
            return false;
        }
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

        if (!RegionFormat.IsMatch(region))
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
