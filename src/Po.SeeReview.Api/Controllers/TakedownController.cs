using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Controller for handling takedown requests from restaurant owners
/// POST /api/takedown - Submit a takedown request for a comic
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TakedownController : ControllerBase
{
    private readonly IComicRepository _comicRepository;
    private readonly ILeaderboardRepository _leaderboardRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<TakedownController> _logger;

    public TakedownController(
        IComicRepository comicRepository,
        ILeaderboardRepository leaderboardRepository,
        IBlobStorageService blobStorageService,
        ILogger<TakedownController> logger)
    {
        _comicRepository = comicRepository;
        _leaderboardRepository = leaderboardRepository;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    /// <summary>
    /// Submit a takedown request for a comic associated with a restaurant
    /// </summary>
    /// <param name="request">Takedown request details</param>
    /// <returns>Confirmation of takedown request processing</returns>
    /// <response code="200">Takedown request processed successfully</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="404">Comic or restaurant not found</response>
    [HttpPost]
    [ProducesResponseType(typeof(TakedownResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TakedownResponse>> SubmitTakedownRequest([FromBody] TakedownRequest request)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.PlaceId))
        {
            _logger.LogWarning("Takedown request submitted with empty PlaceId");
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = "PlaceId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            _logger.LogWarning("Takedown request submitted for {PlaceId} with empty email", request.PlaceId);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = "Contact email is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning("Takedown request submitted for {PlaceId} with no reason", request.PlaceId);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = "Reason for takedown is required"
            });
        }

        _logger.LogInformation(
            "Processing takedown request for PlaceId {PlaceId} from {Email}",
            request.PlaceId,
            request.ContactEmail);

        try
        {
            // Get all comics for this place
            var comics = await _comicRepository.GetComicsByPlaceIdAsync(request.PlaceId);

            if (comics == null || !comics.Any())
            {
                _logger.LogWarning("No comics found for PlaceId {PlaceId}", request.PlaceId);
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Comics not found",
                    Detail = $"No comics found for restaurant with PlaceId {request.PlaceId}"
                });
            }

            // Delete all comics and their blobs
            int deletedCount = 0;
            foreach (var comic in comics)
            {
                // Delete from blob storage
                if (!string.IsNullOrEmpty(comic.ImageUrl))
                {
                    try
                    {
                        await _blobStorageService.DeleteBlobAsync(comic.ImageUrl);
                        _logger.LogInformation("Deleted blob for comic {ComicId}", comic.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete blob for comic {ComicId}", comic.Id);
                    }
                }

                // Delete from table storage
                await _comicRepository.DeleteAsync(comic.PlaceId, comic.CreatedAt);
                deletedCount++;
            }

            // Remove from leaderboard
            await _leaderboardRepository.DeleteByPlaceIdAsync(request.PlaceId);

            _logger.LogInformation(
                "Takedown completed for {PlaceId}. Deleted {Count} comic(s) and leaderboard entry",
                request.PlaceId,
                deletedCount);

            return Ok(new TakedownResponse
            {
                Success = true,
                Message = $"Takedown request processed successfully. {deletedCount} comic(s) removed.",
                PlaceId = request.PlaceId,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing takedown request for {PlaceId}", request.PlaceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Takedown processing failed",
                Detail = "An error occurred while processing the takedown request. Please try again later."
            });
        }
    }
}

/// <summary>
/// Request model for comic takedown
/// </summary>
public class TakedownRequest
{
    /// <summary>
    /// Google Maps Place ID of the restaurant
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Contact email for the restaurant owner or authorized representative
    /// </summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Reason for requesting takedown
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name (optional, for verification)
    /// </summary>
    public string? RestaurantName { get; set; }
}

/// <summary>
/// Response model for takedown request
/// </summary>
public class TakedownResponse
{
    /// <summary>
    /// Whether the takedown was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message describing the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Place ID that was processed
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the request was processed
    /// </summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
