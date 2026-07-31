namespace PoSeeReview.Shared;

/// <summary>
/// Environment names beyond the framework's built-in Development/Staging/Production
/// (NET_RULES 1.5 — zero magic strings). Shared so the API host and the WASM client agree.
/// </summary>
public static class HostEnvironments
{
    /// <summary>Automated-test environment: HTTP-only, AI boundaries mocked.</summary>
    public const string Test = "Test";
}
