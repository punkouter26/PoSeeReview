namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Outcome of a single dependency probe reported by <c>/health</c> and <c>/diag</c>
/// (NET_RULES 1.5 — enums over magic strings). Mirrors
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus</c> plus an
/// explicit <see cref="Unknown"/> for probes that never ran.
/// </summary>
public enum DependencyState
{
    /// <summary>The probe did not run or its result could not be determined.</summary>
    Unknown = 0,

    /// <summary>Dependency is fully operational.</summary>
    Healthy = 1,

    /// <summary>Dependency is reachable but impaired; the app still serves traffic.</summary>
    Degraded = 2,

    /// <summary>Dependency is unavailable.</summary>
    Unhealthy = 3
}
