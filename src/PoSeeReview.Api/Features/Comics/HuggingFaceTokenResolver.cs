namespace PoSeeReview.Infrastructure.Comics;

/// <summary>
/// Resolves the HuggingFace access token the same way the official HF SDKs do, so a local
/// <c>hf auth login</c> is sufficient and no token needs to be pasted into config by hand.
/// Resolution order: explicit <c>HuggingFace:ApiKey</c> config → <c>HF_TOKEN</c> env var →
/// the HF CLI token cache (<c>$HF_HOME/token</c> or <c>~/.cache/huggingface/token</c>).
/// In production the token comes from Key Vault via the config path; the file fallback only
/// helps local dev machines that have logged in with the CLI.
/// </summary>
internal static class HuggingFaceTokenResolver
{
    public static string? Resolve(string? configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
            return configuredKey.Trim();

        var envToken = Environment.GetEnvironmentVariable("HF_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
            return envToken.Trim();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var hfHome = Environment.GetEnvironmentVariable("HF_HOME");
        var candidates = new[]
        {
            string.IsNullOrEmpty(hfHome) ? null : Path.Combine(hfHome, "token"),
            Path.Combine(home, ".cache", "huggingface", "token"),
            Path.Combine(home, ".huggingface", "token"),
        };

        foreach (var path in candidates)
        {
            if (path is null || !File.Exists(path))
                continue;

            // The cache file holds the raw token on a single line. Fine-grained HF tokens are
            // long and contain non-alphanumeric characters, so take the whole first non-empty
            // line verbatim — never a character-class slice, which would truncate the token.
            var firstLine = File.ReadLines(path)
                .Select(l => l.Trim())
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (!string.IsNullOrWhiteSpace(firstLine))
                return firstLine;
        }

        return null;
    }
}
