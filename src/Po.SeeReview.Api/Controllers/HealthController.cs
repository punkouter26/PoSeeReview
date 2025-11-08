using Microsoft.AspNetCore.Mvc;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Health check endpoint for monitoring and diagnostics
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the health status of the API
    /// </summary>
    /// <returns>Health status response</returns>
    /// <response code="200">API is healthy and running</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        _logger.LogDebug("Health check requested");

        var healthStatus = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            service = "PoSeeReview API"
        };

        return Ok(healthStatus);
    }
}
