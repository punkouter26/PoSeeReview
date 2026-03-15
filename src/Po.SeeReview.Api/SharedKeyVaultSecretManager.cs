using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace Po.SeeReview.Api;

/// <summary>
/// Loads shared secrets from the Key Vault (kv-poshared) — those NOT prefixed with any Po-app name.
/// Registered BEFORE PrefixKeyVaultSecretManager so app-specific secrets always take priority.
/// Example shared secrets: "AzureOpenAI--ApiKey", "ConnectionStrings--AzureTableStorage".
/// </summary>
public sealed class SharedKeyVaultSecretManager : KeyVaultSecretManager
{
    /// <summary>
    /// Loads secrets that don't belong to any specific Po-app.
    /// Skips secrets matching the pattern "PoAppName--..." (reserved for individual apps).
    /// </summary>
    public override bool Load(SecretProperties secret)
    {
        var name = secret.Name;

        // Skip any Po-app prefixed secrets (e.g., PoSeeReview--, PoCoupleQuiz--, PoTraffic--)
        if (name.StartsWith("Po", StringComparison.OrdinalIgnoreCase))
        {
            int dashIndex = name.IndexOf("--", StringComparison.Ordinal);
            if (dashIndex > 2) // "Po" + at least one char + "--"
                return false; // belongs to a specific app — skip it
        }

        return true; // shared secret — load it
    }

    /// <summary>
    /// Converts secret name to config key by replacing "--" with ":".
    /// Example: "AzureOpenAI--ApiKey" → "AzureOpenAI:ApiKey"
    /// </summary>
    public override string GetKey(KeyVaultSecret secret) =>
        secret.Name.Replace("--", ":");
}
