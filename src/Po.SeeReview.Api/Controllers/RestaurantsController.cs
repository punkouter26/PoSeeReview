using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Core.Utilities;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// API controller for restaurant discovery and details
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RestaurantsController : ControllerBase
{
    private readonly IRestaurantService _restaurantService;
    private readonly ILogger<RestaurantsController> _logger;

    public RestaurantsController(
        IRestaurantService restaurantService,
        ILogger<RestaurantsController> logger)
    {
        _restaurantService = restaurantService;
        _logger = logger;
    }

    /// <summary>
    /// Gets nearby restaurants within 5km radius
    /// </summary>
    /// <param name="latitude">User's latitude (-90 to 90)</param>
    /// <param name="longitude">User's longitude (-180 to 180)</param>
    /// <param name="limit">Maximum number of results (1-50, default 10)</param>
    /// <returns>List of nearby restaurants with distance</returns>
    /// <response code="200">Successfully retrieved restaurants</response>
    /// <response code="400">Invalid coordinates or limit</response>
    /// <response code="503">Google Maps API unavailable</response>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(NearbyRestaurantsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NearbyRestaurantsResponse>> GetNearbyRestaurants(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] int limit = 10)
    {
        // Validate required parameters
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Bad Request",
                Detail = "Both latitude and longitude are required"
            });
        }

        try
        {
            var lat = latitude.Value;
            var lon = longitude.Value;

            _logger.LogInformation(
                "Getting nearby restaurants at ({Latitude}, {Longitude}), limit {Limit}",
                lat, lon, limit);

            var restaurants = await _restaurantService.GetNearbyRestaurantsAsync(lat, lon, limit);

            // Calculate distance from user location for each restaurant
            var restaurantDtos = restaurants.Select(r => new RestaurantDto
            {
                PlaceId = r.PlaceId,
                Name = r.Name,
                Address = r.Address,
                Latitude = r.Latitude,
                Longitude = r.Longitude,
                AverageRating = r.AverageRating,
                TotalReviews = r.TotalReviews,
                Region = r.Region ?? "US",
                Distance = GeoUtils.CalculateDistance(lat, lon, r.Latitude, r.Longitude)
            }).ToList();

            var response = new NearbyRestaurantsResponse
            {
                Restaurants = restaurantDtos,
                TotalCount = restaurantDtos.Count,
                CachedAt = DateTimeOffset.UtcNow
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request parameters");
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Parameters",
                Detail = ex.Message
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Google Maps API error");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Google Maps API Unavailable",
                Detail = "Unable to fetch restaurant data from Google Maps API"
            });
        }
    }

    /// <summary>
    /// Gets detailed restaurant information by Google place ID
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Restaurant details with reviews and strangeness scores</returns>
    /// <response code="200">Successfully retrieved restaurant</response>
    /// <response code="404">Restaurant not found</response>
    /// <response code="503">Google Maps API unavailable</response>
    [HttpGet("{placeId}")]
    [ProducesResponseType(typeof(RestaurantDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RestaurantDetailsDto>> GetRestaurantByPlaceId(string placeId)
    {
        // Validate placeId
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return NotFound(new ProblemDetails
            {
                Status = 404,
                Title = "Not Found",
                Detail = "Place ID is required"
            });
        }

        try
        {
            _logger.LogInformation("Getting restaurant details for {PlaceId}", placeId);

            var restaurant = await _restaurantService.GetRestaurantByPlaceIdAsync(placeId);

            var detailsDto = new RestaurantDetailsDto
            {
                PlaceId = restaurant.PlaceId,
                Name = restaurant.Name,
                Address = restaurant.Address,
                Latitude = restaurant.Latitude,
                Longitude = restaurant.Longitude,
                AverageRating = restaurant.AverageRating,
                TotalReviews = restaurant.TotalReviews,
                Reviews = restaurant.Reviews.Select(r => new ReviewDto
                {
                    AuthorName = r.AuthorName,
                    Text = r.Text,
                    Rating = r.Rating,
                    Time = r.Time,
                    StrangenessScore = r.StrangenessScore
                }).ToList()
            };

            return Ok(detailsDto);
        }
        catch (ArgumentNullException ex)
        {
            _logger.LogWarning(ex, "Invalid placeId parameter");
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid PlaceId",
                Detail = ex.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Restaurant not found: {PlaceId}", placeId);
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Restaurant Not Found",
                Detail = $"Restaurant with placeId '{placeId}' not found"
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Google Maps API error");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Google Maps API Unavailable",
                Detail = "Unable to fetch restaurant data from Google Maps API"
            });
        }
    }
}

/// <summary>
/// Response DTO for GET /api/restaurants/nearby
/// </summary>
public class NearbyRestaurantsResponse
{
    public List<RestaurantDto> Restaurants { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
