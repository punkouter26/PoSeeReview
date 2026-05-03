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
    private readonly IWebHostEnvironment _env;

    public RestaurantsController(
        IRestaurantService restaurantService,
        ILogger<RestaurantsController> logger,
        IWebHostEnvironment env)
    {
        _restaurantService = restaurantService;
        _logger = logger;
        _env = env;
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

            var restaurants = await _restaurantService.GetNearbyRestaurantsAsync(lat, lon, limit, HttpContext.RequestAborted);

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
        catch (OperationCanceledException)
        {
            // Client disconnected or request timed out — no response to write.
            _logger.LogInformation("Nearby restaurant search cancelled (client disconnected or timed out)");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Request Cancelled",
                Detail = "The request was cancelled before it could complete."
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Google Maps API error for nearby search: {Message}", ex.Message);
            // In Development expose the actual Google API error (key issues, quota, etc.)
            var detail = _env.IsDevelopment()
                ? ex.Message
                : "Unable to fetch restaurant data from Google Maps API";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Google Maps API Unavailable",
                Detail = detail
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in nearby restaurant search at ({Latitude}, {Longitude})", latitude, longitude);
            var detail = _env.IsDevelopment() ? ex.Message : "An unexpected error occurred while fetching restaurants.";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service Error",
                Detail = detail
            });
        }
    }

    /// <summary>
    /// Searches for restaurants by location query (city name, ZIP code, etc.)
    /// </summary>
    /// <param name="location">Location query string (e.g., "Seattle", "98101")</param>
    /// <param name="limit">Maximum number of results (1-50, default 10)</param>
    /// <returns>List of restaurants near the specified location</returns>
    /// <response code="200">Successfully retrieved restaurants</response>
    /// <response code="400">Invalid location query</response>
    /// <response code="503">Google Maps API unavailable</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(NearbyRestaurantsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NearbyRestaurantsResponse>> SearchRestaurantsByLocation(
        [FromQuery] string location,
        [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Bad Request",
                Detail = "Location query is required"
            });
        }

        (double lat, double lon)? coordinates;
        try
        {
            coordinates = await _restaurantService.GeocodeLocationAsync(location, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Geocoding cancelled for location '{Location}' (client disconnected)", location);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Request Cancelled",
                Detail = "The request was cancelled before geocoding could complete."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geocoding failed for location '{Location}': {Message}", location, ex.Message);
            var detail = _env.IsDevelopment() ? ex.Message : "Unable to resolve the specified location.";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Geocoding Failed",
                Detail = detail
            });
        }

        if (coordinates == null)
        {
            _logger.LogWarning("Location '{Location}' not recognized — returning 400", location);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Unrecognized Location",
                Detail = $"Location '{location}' could not be geocoded. Try including city/state or a full ZIP/postal code."
            });
        }

        return await GetNearbyRestaurants(coordinates.Value.lat, coordinates.Value.lon, limit);
    }

}

