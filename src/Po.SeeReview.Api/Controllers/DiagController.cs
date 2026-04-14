using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Diagnostics endpoint — exposes configuration keys/values with middle characters masked for security.
/// Accessible at /api/diag in Development only.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DiagController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    private static readonly string[] SensitivePatterns =
    [
        "key", "secret", "password", "connectionstring", "token", "apikey", "credential", "clientsecret", "private"
    ];

    private static readonly string[] ExcludedKeyPrefixes =
    [
        "PATH", "PATHEXT", "PSModulePath", "TEMP", "TMP", "USERNAME", "USERPROFILE", "HOMEPATH", "LOCALAPPDATA", "APPDATA",
        "PROGRAMFILES", "PROGRAMDATA", "SYSTEMROOT", "WINDIR", "GIT_", "VSCODE_", "PROCESSOR_", "COMPUTERNAME"
    ];

    // Keep /api/diag focused on app-relevant settings to reduce noise and accidental exposure.
    private static readonly string[] AllowedKeyPrefixes =
    [
        "AllowedHosts",
        "ApplicationInsights",
        "ASPNETCORE_ENVIRONMENT",
        "Authentication",
        "Azure",
        "Cleanup",
        "Comics",
        "ConnectionStrings",
        "Cors",
        "DOTNET_",
        "ExternalAuth",
        "Google",
        "HealthChecks",
        "KeyVault",
        "RateLimiting",
        "Serilog"
    ];

    /// <summary>
    /// Returns all configuration values with secrets partially masked.
    /// Restricted to Development environment only.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetDiagnostics()
    {
        // Security: only expose config info in Development
        if (!environment.IsDevelopment())
        {
            return NotFound(); // Avoid fingerprinting — don't reveal the endpoint exists
        }
        var configEntries = new Dictionary<string, string?>();

        // Flatten all configuration sections
        foreach (var section in configuration.AsEnumerable())
        {
            if (ShouldExcludeKey(section.Key) || !ShouldIncludeKey(section.Key))
            {
                continue;
            }

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

        var lowerKey = key.ToLowerInvariant();
        var isSensitive = SensitivePatterns.Any(p => lowerKey.Contains(p));

        // Common secret formats that should never leak any fragment.
        var looksLikeSecretValue =
            value.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("gho_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("AIza", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("DefaultEndpointsProtocol=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase);

        if (isSensitive || looksLikeSecretValue)
            return "[REDACTED]";

        if (value.Length > 120)
            return string.Concat(value.AsSpan(0, 117), "...");

        return value;
    }

    private static bool ShouldExcludeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        return ExcludedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldIncludeKey(string key)
    {
        return AllowedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
