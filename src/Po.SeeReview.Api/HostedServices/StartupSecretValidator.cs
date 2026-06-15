using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Po.SeeReview.Api.HostedServices;

/// <summary>
/// Fail-fast validator that runs once at startup. In Production it throws if any of the
/// AI/Map secrets are missing — we never want a misconfigured deployment to silently serve
/// fabricated data (see PoFunQuiz memory: <c>pofunquiz-mock-data-fix.md</c>).
/// In Development / Test environments it logs warnings only so local iteration is unaffected.
/// </summary>
public sealed class StartupSecretValidator(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<StartupSecretValidator> logger) : IHostedService
{
    // Static templates — CA2254-safe and zero-allocation per call.
    private static readonly Action<ILogger, string, Exception?> MissingInProd =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(9001, "MissingRequiredSecret"),
            "Required configuration key '{Key}' is missing in Production. Refusing to start.");

    private static readonly Action<ILogger, string, Exception?> MissingInDev =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(9002, "MissingOptionalSecret"),
            "Configuration key '{Key}' is missing. AI/Map features will be unavailable.");

    /// <summary>
    /// Sole Azure AI Foundry deployment the app is allowed to call. Verified 2026-06-14
    /// via <c>az cognitiveservices account deployment list -g PoShared -n po-aiservices-shared</c>.
    /// A drift here means the secret has been edited to point at a non-existent deployment
    /// and every comic request will 5xx with DeploymentNotFound — we fail-fast instead.
    /// </summary>
    private const string ExpectedDeploymentName = "gpt-5.4-nano";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Order matters: keep these cheap and idempotent.
        var required = new[]
        {
            "AzureOpenAI:Endpoint",
            "AzureOpenAI:ApiKey",
            // Note: the value must match the live deployment in po-aiservices-shared (currently gpt-5.4-nano).
            "AzureOpenAI:DeploymentName",
            "GoogleMaps:ApiKey",
            "Google:GeminiApiKey"
        };
        var missing = new List<string>(capacity: required.Length);
        foreach (var key in required)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(key);
            }
        }

        if (missing.Count == 0)
        {
            // Drift guard: refuse to boot in non-Development if the configured deployment name
            // doesn't match the sole live deployment in po-aiservices-shared.
            var configuredDeployment = configuration["AzureOpenAI:DeploymentName"];
            if (!environment.IsDevelopment()
                && !environment.IsEnvironment("Test")
                && !string.Equals(configuredDeployment, ExpectedDeploymentName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"StartupSecretValidator: AzureOpenAI:DeploymentName is '{configuredDeployment}', " +
                    $"but the only deployment in po-aiservices-shared is '{ExpectedDeploymentName}'. " +
                    "Update kv-poshared secret 'AzureOpenAI--DeploymentName' to match.");
            }

            return Task.CompletedTask;
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
        {
            foreach (var key in missing)
            {
                MissingInDev(logger, key, null);
            }
        }
        else
        {
            foreach (var key in missing)
            {
                MissingInProd(logger, key, null);
            }

            throw new InvalidOperationException(
                $"StartupSecretValidator: {missing.Count} required configuration value(s) missing in Production: " +
                string.Join(", ", missing));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
