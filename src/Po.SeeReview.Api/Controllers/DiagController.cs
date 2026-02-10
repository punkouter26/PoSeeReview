using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Diagnostics endpoint — exposes configuration keys/values with middle characters masked for security.
/// Accessible at /api/diag. Use for verifying environment configuration in any deployment.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DiagController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>
    /// Returns all configuration values with secrets partially masked.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetDiagnostics()
    {
        var configEntries = new Dictionary<string, string?>();

        // Flatten all configuration sections
        foreach (var section in configuration.AsEnumerable())
        {
            configEntries[section.Key] = MaskValue(section.Key, section.Value);
        }

        var diagnostics = new
        {
            timestamp = DateTime.UtcNow,
            environment = environment.EnvironmentName,
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.ToString(),
            dotnetVersion = Environment.Version.ToString(),
            processId = Environment.ProcessId,
            config = configEntries
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        return Ok(diagnostics);
    }

    /// <summary>
    /// Masks the middle portion of sensitive values for security.
    /// Shows first 3 and last 3 characters; everything else replaced with '***'.
    /// </summary>
    private static string? MaskValue(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Keys that likely contain sensitive data
        var sensitivePatterns = new[]
        {
            "key", "secret", "password", "connectionstring", "token",
            "apikey", "credential", "endpoint", "connection"
        };

        var lowerKey = key.ToLowerInvariant();
        var isSensitive = sensitivePatterns.Any(p => lowerKey.Contains(p));

        if (!isSensitive)
            return value;

        if (value.Length <= 6)
            return "***";

        return string.Concat(value.AsSpan(0, 3), "***", value.AsSpan(value.Length - 3));
    }
}
