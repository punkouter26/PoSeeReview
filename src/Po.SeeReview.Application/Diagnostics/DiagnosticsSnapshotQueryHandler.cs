using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Application.Abstractions;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Application.Diagnostics;

public class DiagnosticsSnapshotQueryHandler(
    IConfiguration configuration,
    HealthCheckService healthCheckService,
    ICurrentRequestIdentityAccessor currentRequestIdentityAccessor,
    ILogger<DiagnosticsSnapshotQueryHandler> logger)
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
        "FeatureFlags",
        "Google",
        "HealthChecks",
        "KeyVault",
        "RateLimiting",
        "Serilog"
    ];

    public async Task<DiagnosticsSnapshotDto> ExecuteAsync(CancellationToken cancellationToken)
    {
        var configEntries = new List<ConfigurationValueDto>();
        foreach (var section in configuration.AsEnumerable())
        {
            if (ShouldExcludeKey(section.Key) || !ShouldIncludeKey(section.Key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(section.Value))
            {
                continue;
            }

            configEntries.Add(new ConfigurationValueDto
            {
                Key = section.Key,
                Value = MaskValue(section.Key, section.Value)
            });
        }

        HealthReport? dependencyReport = null;
        try
        {
            dependencyReport = await healthCheckService.CheckHealthAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to gather dependency status for diagnostics snapshot");
        }

        var keyStatus = new[]
        {
            "KeyVault:Endpoint",
            "ApplicationInsights:ConnectionString",
            "GoogleMaps:ApiKey",
            "AzureOpenAI:Endpoint",
            "AzureOpenAI:ApiKey",
            "ConnectionStrings:Storage",
            "ConnectionStrings:AzureTableStorage",
            "ConnectionStrings:AzureBlobStorage"
        }
        .Select(key => new ConfigurationKeyStatusDto
        {
            Key = key,
            Exists = !string.IsNullOrWhiteSpace(configuration[key]),
            Value = MaskValue(key, configuration[key])
        })
        .ToList();

        return new DiagnosticsSnapshotDto
        {
            Timestamp = DateTime.UtcNow,
            Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Unknown",
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            ProcessId = Environment.ProcessId,
            CurrentUserId = currentRequestIdentityAccessor.GetCurrentUserId(),
            DependencyStatus = dependencyReport == null
                ? null
                : new DependencyStatusSummaryDto
                {
                    Overall = dependencyReport.Status.ToString(),
                    Checks = dependencyReport.Entries.Select(entry => new DependencyCheckDto
                    {
                        Name = entry.Key,
                        Status = entry.Value.Status.ToString(),
                        Description = entry.Value.Description,
                        DurationMilliseconds = entry.Value.Duration.TotalMilliseconds,
                        Error = entry.Value.Exception?.ToString()
                    }).ToList()
                },
            KeyStatus = keyStatus,
            Config = configEntries.OrderBy(entry => entry.Key).ToList()
        };
    }

    private static string? MaskValue(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var lowerKey = key.ToLowerInvariant();
        var isSensitive = SensitivePatterns.Any(pattern => lowerKey.Contains(pattern));
        var looksLikeSecretValue =
            value.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("gho_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("AIza", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("DefaultEndpointsProtocol=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase);

        if (isSensitive || looksLikeSecretValue)
        {
            return MaskMiddle(value);
        }

        if (value.Length > 120)
        {
            return string.Concat(value.AsSpan(0, 117), "...");
        }

        return value;
    }

    private static string MaskMiddle(string value)
    {
        if (value.Length <= 6)
        {
            return new string('*', value.Length);
        }

        return string.Concat(value.AsSpan(0, 3), "***", value.AsSpan(value.Length - 3));
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
