using System.Net;
using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Api.Telemetry;
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
    private readonly IConfiguration _config;

    public RestaurantsController(
        IRestaurantService restaurantService,
        ILogger<RestaurantsController> logger,
        IWebHostEnvironment env,
        IConfiguration config)
    {
        _restaurantService = restaurantService;
        _logger = logger;
        _env = env;
        _config = config;
    }

    /// <summary>
    /// True when the configured Google Maps key is a known dev placeholder, so callers
    /// can short-circuit the real network call and return a stale-friendly response.
    /// </summary>
    private bool IsDevPlaceholderKey() =>
        (_config["GoogleMaps:ApiKey"] ?? string.Empty).StartsWith("dev-placeholder", StringComparison.OrdinalIgnoreCase);

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

            // Dev short-circuit: skip the real Google Maps call when the configured key is a
            // placeholder. Returns an empty list with `stale=true` so the UI can render a
            // helpful "configure your API key" message instead of misleading 503s.
            if (IsDevPlaceholderKey())
            {
                _logger.LogWarning(
                    "Skipping Google Maps call: GoogleMaps:ApiKey is a dev placeholder. " +
                    "Add a real key to appsettings.Development.json or Key Vault to enable nearby search.");

                var devResponse = new NearbyRestaurantsResponse
                {
                    Restaurants = new List<RestaurantDto>(),
                    TotalCount = 0,
                    CachedAt = DateTimeOffset.UtcNow
                };
                devResponse.Stale = true;
                return Ok(devResponse);
            }

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
            // Differentiate upstream 4xx (our problem) from 5xx / IO (their problem).
            // A Google 400 means the request itself was malformed (bad key, missing field mask,
            // invalid locationRestriction) — that's a 400 to the client, not a 503.
            var upstream = (int?)ex.StatusCode;
            var isClientError = upstream is >= 400 and < 500;
            var statusCode = isClientError
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status503ServiceUnavailable;
            var title = isClientError
                ? "Google Maps Request Rejected"
                : "Google Maps API Unavailable";

            PoSeeReviewTelemetry.ComicGenerationErrors.Add(1, new[]
            {
                new KeyValuePair<string, object?>("error_type", isClientError ? "google_maps_4xx" : "google_maps_5xx"),
                new KeyValuePair<string, object?>("provider", "google-maps"),
                new KeyValuePair<string, object?>("phase", "nearby_search"),
                new KeyValuePair<string, object?>("upstream_status", upstream ?? 0)
            });

            _logger.LogError(
                ex,
                "Google Maps API error for nearby search. UpstreamStatus={UpstreamStatus} ClientError={ClientError}",
                upstream,
                isClientError);

            var detail = _env.IsDevelopment()
                ? ex.Message
                : (isClientError
                    ? "The nearby search request was rejected by the upstream provider."
                    : "Unable to fetch restaurant data from Google Maps API");

            return StatusCode(statusCode, new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = HttpContext.Request.Path
            });
        }
        catch (Exception ex)
        {
            // Anything we did NOT anticipate is a real bug — let GlobalExceptionHandler classify it.
            // Re-throw so it gets the (Provider, Phase, ErrorType) tags and shows up in App Insights
            // with the correct severity, instead of being silently swallowed as 503.
            _logger.LogError(ex,
                "Unexpected error in nearby restaurant search at ({Latitude}, {Longitude})",
                latitude, longitude);
            throw;
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

