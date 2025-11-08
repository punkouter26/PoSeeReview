using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// API controller for comic generation and retrieval.
/// Provides endpoints for generating comics from restaurant reviews and caching.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ComicsController : ControllerBase
{
    private readonly IComicGenerationService _comicGenerationService;
    private readonly ILogger<ComicsController> _logger;

    public ComicsController(
        IComicGenerationService comicGenerationService,
        ILogger<ComicsController> logger)
    {
        _comicGenerationService = comicGenerationService ?? throw new ArgumentNullException(nameof(comicGenerationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a comic for a restaurant or returns cached version.
    /// Generation time: 8-10 seconds (DALL-E API latency).
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <param name="forceRegenerate">Force regeneration even if valid cache exists</param>
    /// <returns>Generated or cached comic</returns>
    /// <response code="200">Comic generated successfully (or returned from cache)</response>
    /// <response code="400">Invalid place ID or request parameters</response>
    /// <response code="404">Restaurant not found</response>
    /// <response code="500">Comic generation failed (e.g., DALL-E API error)</response>
    [HttpPost("{placeId}")]
    [ProducesResponseType(typeof(ComicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ComicDto>> GenerateComic(
        [FromRoute] string placeId,
        [FromQuery] bool forceRegenerate = false)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            _logger.LogWarning("GenerateComic called with empty placeId");
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Place ID is required",
                Instance = HttpContext.Request.Path
            });
        }

        try
        {
            _logger.LogInformation("Generating comic for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}",
                placeId, forceRegenerate);

            var comic = await _comicGenerationService.GenerateComicAsync(placeId, forceRegenerate);

            var dto = new ComicDto
            {
                ComicId = comic.Id,
                PlaceId = comic.PlaceId,
                RestaurantName = comic.RestaurantName,
                Narrative = comic.Narrative,
                StrangenessScore = comic.StrangenessScore,
                BlobUrl = comic.ImageUrl,
                GeneratedAt = comic.CreatedAt,
                ExpiresAt = comic.ExpiresAt,
                IsCached = comic.IsCached
            };

            _logger.LogInformation("Comic generated successfully for placeId: {PlaceId}", placeId);

            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Restaurant not found: {PlaceId}", placeId);
            return NotFound(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"Restaurant not found: {placeId}",
                Instance = HttpContext.Request.Path
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reviews"))
        {
            _logger.LogWarning(ex, "Insufficient reviews for placeId: {PlaceId}", placeId);
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate comic for placeId: {PlaceId}", placeId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = $"Comic generation failed: {ex.Message}",
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Retrieves the most recent cached comic for a restaurant.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Cached comic if found and not expired</returns>
    /// <response code="200">Cached comic found</response>
    /// <response code="404">No cached comic found (not generated or expired)</response>
    [HttpGet("{placeId}")]
    [ProducesResponseType(typeof(ComicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComicDto>> GetCachedComic([FromRoute] string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Place ID is required",
                Instance = HttpContext.Request.Path
            });
        }

        try
        {
            // Try to get cached comic only (don't generate)
            var cachedComic = await _comicGenerationService.GetCachedComicAsync(placeId);

            if (cachedComic != null && cachedComic.ExpiresAt > DateTime.UtcNow)
            {
                var dto = new ComicDto
                {
                    ComicId = cachedComic.Id,
                    PlaceId = cachedComic.PlaceId,
                    RestaurantName = cachedComic.RestaurantName,
                    Narrative = cachedComic.Narrative,
                    StrangenessScore = cachedComic.StrangenessScore,
                    BlobUrl = cachedComic.ImageUrl,
                    GeneratedAt = cachedComic.CreatedAt,
                    ExpiresAt = cachedComic.ExpiresAt,
                    IsCached = true
                };

                return Ok(dto);
            }

            // No cached comic or expired
            return NotFound(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = "No cached comic found",
                Instance = HttpContext.Request.Path
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = "No comic found for this restaurant",
                Instance = HttpContext.Request.Path
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot generate comic for placeId: {PlaceId}", placeId);
            return BadRequest(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached comic for placeId: {PlaceId}", placeId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "Failed to retrieve cached comic",
                Instance = HttpContext.Request.Path
            });
        }
    }
}
