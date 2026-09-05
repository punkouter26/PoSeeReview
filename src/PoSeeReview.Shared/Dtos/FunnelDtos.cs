namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// One client-side funnel event. Body of <c>POST /api/analytics/events</c>.
/// <para>
/// The PRD sets targets — time-to-first-comic under 30s, cache hit rate over 40% — that nothing
/// in the app measured: the server tracked only <c>ComicGenerated</c>, which cannot see a user
/// who denied location or abandoned mid-generation. Those steps happen in the browser, so the
/// browser has to report them.
/// </para>
/// <para>
/// Carries no free text, no place identifiers and no coordinates on purpose. It is a counter
/// ping, not a session recording, and the server rejects any step outside the known set.
/// </para>
/// </summary>
public sealed class FunnelEventDto
{
    /// <summary>One of the <see cref="FunnelSteps"/> constants.</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>
    /// Optional milliseconds elapsed for a timed step (currently only comic completion), so
    /// time-to-first-comic is measurable rather than inferred.
    /// </summary>
    public int? DurationMs { get; set; }
}

/// <summary>
/// The funnel steps the server will accept. A closed vocabulary keeps an unbounded set of
/// client-invented dimension values out of telemetry, where they would be a cost problem
/// (NET_RULES 1.5 — zero magic strings).
/// </summary>
public static class FunnelSteps
{
    public const string AppOpened = "app_opened";
    public const string LocationGranted = "location_granted";
    public const string LocationDenied = "location_denied";
    public const string SearchPerformed = "search_performed";
    public const string ResultsShown = "results_shown";
    public const string RestaurantTapped = "restaurant_tapped";
    public const string GenerationStarted = "generation_started";
    public const string GenerationCompleted = "generation_completed";
    public const string GenerationFailed = "generation_failed";
    public const string GenerationAbandoned = "generation_abandoned";
    public const string CacheHit = "cache_hit";
    public const string ComicShared = "comic_shared";
    public const string ComicSaved = "comic_saved";

    /// <summary>Every accepted step, in funnel order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        AppOpened,
        LocationGranted,
        LocationDenied,
        SearchPerformed,
        ResultsShown,
        RestaurantTapped,
        GenerationStarted,
        GenerationCompleted,
        GenerationFailed,
        GenerationAbandoned,
        CacheHit,
        ComicShared,
        ComicSaved
    ];

    /// <summary>True when <paramref name="step"/> is a step this app defined.</summary>
    public static bool IsKnown(string? step) =>
        !string.IsNullOrWhiteSpace(step) && All.Contains(step);
}

/// <summary>
/// Today's funnel counters and the rates derived from them. Response of
/// <c>GET /api/analytics/funnel</c>, rendered on the Diagnostics page.
/// </summary>
public sealed class FunnelSnapshotDto
{
    /// <summary>UTC date these counters cover.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Raw count per <see cref="FunnelSteps"/> key.</summary>
    public Dictionary<string, int> Counts { get; set; } = new();

    /// <summary>Share of location attempts that were granted, 0–100, or null with no attempts.</summary>
    public double? LocationGrantRate { get; set; }

    /// <summary>
    /// Share of restaurant taps that went on to begin a generation, 0–100, or null with no taps.
    /// <para>
    /// Both sides of this are tapped-flow-only events, which matters: an earlier version divided
    /// <em>all</em> delivered comics by taps and reported 200%, because a comic opened from a
    /// shared link or the Hall of Fame has no tap in front of it.
    /// </para>
    /// </summary>
    public double? TapThroughRate { get; set; }

    /// <summary>
    /// Share of started generations that finished, 0–100, or null with none started. Its
    /// complement is the abandon-and-fail rate, which is the clearest signal that the wait is
    /// too long.
    /// </summary>
    public double? GenerationCompletionRate { get; set; }

    /// <summary>Share of comics served from cache, 0–100, or null with no comics. PRD target: &gt; 40.</summary>
    public double? CacheHitRate { get; set; }

    /// <summary>Share of completed comics that were shared, 0–100, or null with no comics.</summary>
    public double? ShareRate { get; set; }

    /// <summary>Mean client-observed generation time in ms, or null when nothing timed completed.</summary>
    public double? AverageGenerationMs { get; set; }
}
