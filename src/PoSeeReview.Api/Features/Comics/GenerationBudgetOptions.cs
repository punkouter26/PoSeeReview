namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Daily spend ceilings for the paid generation pipeline (NET_RULES 1.5 — zero magic numbers).
/// <para>
/// The <c>comics-post</c> rate limiter caps bursts at 3/min partitioned by IP. That bounds
/// nothing over a day: a user on a rotating mobile IP, or a modest traffic spike, can run the
/// image model indefinitely. These are the limits that actually bound the bill.
/// </para>
/// </summary>
public class GenerationBudgetOptions
{
    public const string SectionName = "GenerationBudget";

    /// <summary>
    /// Comics one principal may generate per UTC day. Cache hits are refunded, so this counts
    /// paid generations rather than requests.
    /// </summary>
    public int DailyPerUserLimit { get; set; } = 10;

    /// <summary>
    /// Comics the whole app may generate per UTC day. When this is reached every caller is
    /// refused with distinct copy — nothing they do restores it before the reset.
    /// </summary>
    public int DailyServiceLimit { get; set; } = 500;

    /// <summary>
    /// Set false to disable enforcement entirely (local development against mocks). Counting
    /// still happens, so <c>/diag</c> reports real usage either way.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Attempts at the optimistic-concurrency increment before a request is let through
    /// uncounted. Failing open is deliberate: a contended counter must not become an outage on
    /// the app's primary action. Sustained contention shows up as a budget rejection shortfall
    /// in telemetry rather than as errors for users.
    /// </summary>
    public int MaxConcurrencyRetries { get; set; } = 5;
}
