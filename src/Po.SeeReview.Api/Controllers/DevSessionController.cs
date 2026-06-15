using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Application.DevSessions;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DevSessionController(DevSessionCommandHandler devSessionCommandHandler, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>
    /// Returns the current dev session, or an anonymous payload in Production.
    /// In Production the controller stays mounted so the Blazor WASM client never sees a 404
    /// (which previously dropped the <c>X-Dev-User-Id</c> correlation header and made
    /// downstream AI 5xx exceptions harder to attribute in App Insights).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(DevSessionDto), StatusCodes.Status200OK)]
    public ActionResult<DevSessionDto> GetCurrentSession()
    {
        if (!IsEnabled())
        {
            return Ok(DevSessionCommandHandler.BuildAnonymousSession());
        }

        return Ok(devSessionCommandHandler.GetCurrentSession());
    }

    /// <summary>
    /// Creates a fresh anonymous dev session. In Production this still succeeds
    /// (returns a synthetic session) so e2e automation can call the local "GUEST" path
    /// without requiring OAuth.
    /// </summary>
    [HttpPost("anon")]
    [ProducesResponseType(typeof(DevSessionDto), StatusCodes.Status200OK)]
    public ActionResult<DevSessionDto> CreateAnonymousSession()
    {
        if (!IsEnabled())
        {
            return Ok(DevSessionCommandHandler.BuildAnonymousSession());
        }

        return Ok(devSessionCommandHandler.CreateRandomAnonymousSession());
    }

    private bool IsEnabled() => environment.IsDevelopment() || environment.IsEnvironment("Test");
}
