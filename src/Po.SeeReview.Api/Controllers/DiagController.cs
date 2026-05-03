using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Application.Diagnostics;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Diagnostics endpoint — exposes configuration keys/values with middle characters masked for security.
/// Accessible at /api/diag in Development only.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DiagController(
    IWebHostEnvironment environment,
    DiagnosticsSnapshotQueryHandler diagnosticsSnapshotQueryHandler) : ControllerBase
{
    /// <summary>
    /// Returns all configuration values with secrets partially masked.
    /// Restricted to Development environment only.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(DiagnosticsSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDiagnostics(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test"))
        {
            return NotFound(); // Avoid fingerprinting — don't reveal the endpoint exists
        }
        var diagnostics = await diagnosticsSnapshotQueryHandler.ExecuteAsync(cancellationToken);
        diagnostics.Environment = environment.EnvironmentName;
        return Ok(diagnostics);
    }
}
