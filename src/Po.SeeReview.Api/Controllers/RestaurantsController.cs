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

        var coordinates = GetCoordinatesForLocation(location);
        if (coordinates == null)
        {
            _logger.LogWarning("Location '{Location}' not recognized — returning 400", location);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Unrecognized Location",
                Detail = $"Location '{location}' is not recognized. "
                    + "Supported cities: Seattle, San Francisco, New York, Los Angeles, Chicago, "
                    + "Boston, Portland, Austin, Denver, Miami (or their ZIP codes)."
            });
        }

        return await GetNearbyRestaurants(coordinates.Value.lat, coordinates.Value.lon, limit);
    }

    private (double lat, double lon)? GetCoordinatesForLocation(string location)
    {
        // Simplified geocoding - in production, use Google Geocoding API
        var locationLower = location.ToLowerInvariant().Trim();
        
        return locationLower switch
        {
            "seattle" or "seattle, wa" or "98101" => (47.6062, -122.3321),
            "san francisco" or "sf" or "94102" => (37.7749, -122.4194),
            "new york" or "nyc" or "10001" => (40.7128, -74.0060),
            "los angeles" or "la" or "90001" => (34.0522, -118.2437),
            "chicago" or "60601" => (41.8781, -87.6298),
            "boston" or "02101" => (42.3601, -71.0589),
            "portland" or "portland, or" or "97201" => (45.5152, -122.6784),
            "austin" or "78701" => (30.2672, -97.7431),
            "denver" or "80201" => (39.7392, -104.9903),
            "miami" or "33101" => (25.7617, -80.1918),
            _ => null
        };
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
            _logger.LogError(ex, "Google Maps API error for placeId {PlaceId}: {Message}", placeId, ex.Message);
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
    }
}
