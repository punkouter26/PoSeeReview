using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace Po.SeeReview.Api;

/// <summary>
/// Custom KeyVaultSecretManager that filters secrets by app prefix.
/// Secrets prefixed with "PoSeeReview--" are loaded with the prefix stripped.
/// Shared secrets (no app prefix) are also loaded.
/// This allows the shared Key Vault (kv-poshared) to store secrets for multiple apps.
/// </summary>
public sealed class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private const string AppPrefix = "PoSeeReview--";

    /// <summary>
    /// Loads secrets that either:
    /// 1. Start with the app prefix (PoSeeReview--) — app-specific secrets
    /// 2. Don't contain any app prefix pattern (shared secrets)
    /// Skips secrets belonging to other apps (e.g., PoCoupleQuiz--, PoSnakeGame--).
    /// </summary>
    public override bool Load(SecretProperties secret)
    {
        var name = secret.Name;

        // Always load our app-specific secrets
        if (name.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        // Load shared secrets (those that don't start with "Po" followed by a word and "--")
        // This skips other apps' secrets like PoCoupleQuiz--, PoSnakeGame--, etc.
        if (!name.StartsWith("Po", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if it matches the pattern of another app's prefixed secret
        // Other app prefixes look like: PoAppName-- or PoAppName-
        var dashIndex = name.IndexOf("--", StringComparison.Ordinal);
        if (dashIndex < 0)
            dashIndex = name.IndexOf('-');

        if (dashIndex > 2) // "Po" + at least one char + dash
        {
            var prefix = name[..dashIndex];
            // If it starts with "Po" and has a prefix that isn't "PoSeeReview", skip it
            if (!prefix.Equals("PoSeeReview", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps the Key Vault secret name to the configuration key.
    /// Strips the "PoSeeReview--" prefix if present, then replaces "--" with ":".
    /// </summary>
    public override string GetKey(KeyVaultSecret secret)
    {
        var name = secret.Name;

        // Strip app prefix if present
        if (name.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[AppPrefix.Length..];
        }

        // Replace "--" with ":" (standard Azure Key Vault convention)
        return name.Replace("--", ":");
    }
}
