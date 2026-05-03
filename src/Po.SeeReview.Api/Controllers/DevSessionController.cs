using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Application.DevSessions;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DevSessionController(DevSessionCommandHandler devSessionCommandHandler, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DevSessionDto), StatusCodes.Status200OK)]
    public ActionResult<DevSessionDto> GetCurrentSession()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        return Ok(devSessionCommandHandler.GetCurrentSession());
    }

    [HttpPost("anon")]
    [ProducesResponseType(typeof(DevSessionDto), StatusCodes.Status200OK)]
    public ActionResult<DevSessionDto> CreateAnonymousSession()
    {
        if (!IsEnabled())
        {
            return NotFound();
        }

        return Ok(devSessionCommandHandler.CreateRandomAnonymousSession());
    }

    private bool IsEnabled() => environment.IsDevelopment() || environment.IsEnvironment("Test");
}
