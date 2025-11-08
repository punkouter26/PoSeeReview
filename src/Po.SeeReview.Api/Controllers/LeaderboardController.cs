using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Models;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Controller for global strangeness leaderboard operations
/// GET /api/leaderboard - Retrieve top N comics by region
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        ILeaderboardService leaderboardService,
        ILogger<LeaderboardController> logger)
    {
        _leaderboardService = leaderboardService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the top N strangest restaurant comics for a region
    /// </summary>
    /// <param name="region">ISO 3166-1 alpha-2 country code (e.g., US, GB, AU)</param>
    /// <param name="limit">Maximum number of entries to return (1-50, default 10)</param>
    /// <returns>Leaderboard entries sorted by strangeness score descending</returns>
    /// <response code="200">Returns the leaderboard entries</response>
    /// <response code="400">Invalid region or limit parameter</response>
    [HttpGet]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LeaderboardResponse>> GetLeaderboard(
        [FromQuery] string region = "US",
        [FromQuery] int limit = 10)
    {
        // Validate region (basic validation - not empty, alphanumeric with hyphens)
        if (string.IsNullOrWhiteSpace(region))
        {
            _logger.LogWarning("GetLeaderboard called with empty region");
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid region",
                Detail = "Region parameter is required"
            });
        }

        // Allow flexible region codes for testing (US, GB, US-WA-TEST, etc.)
        if (!System.Text.RegularExpressions.Regex.IsMatch(region, @"^[A-Z]{2}(-[A-Z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("GetLeaderboard called with invalid region format: {Region}", region);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid region",
                Detail = $"Region '{region}' has invalid format. Must start with a 2-letter country code (e.g., US, GB, US-WA)."
            });
        }

        // Validate limit
        if (limit < 1 || limit > 50)
        {
            _logger.LogWarning("GetLeaderboard called with invalid limit: {Limit}", limit);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid limit",
                Detail = "Limit must be between 1 and 50"
            });
        }

        try
        {
            _logger.LogInformation("Fetching leaderboard for region {Region} with limit {Limit}", region, limit);

            var entries = await _leaderboardService.GetTopComicsAsync(region, limit);

            var response = new LeaderboardResponse
            {
                Region = region.ToUpperInvariant(),
                Entries = entries.Select(e => new LeaderboardEntryDto
                {
                    Rank = e.Rank,
                    PlaceId = e.PlaceId,
                    RestaurantName = e.RestaurantName,
                    Address = e.Address,
                    Region = e.Region,
                    StrangenessScore = e.StrangenessScore,
                    ComicBlobUrl = e.ComicBlobUrl,
                    LastUpdated = e.LastUpdated
                }).ToList(),
                LastUpdated = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Retrieved {Count} leaderboard entries for region {Region}", entries.Count, region);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving leaderboard for region {Region}", region);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal server error",
                Detail = "An error occurred while retrieving the leaderboard"
            });
        }
    }
}
