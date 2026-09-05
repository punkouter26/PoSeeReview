using System.Diagnostics.Metrics;
using System.Diagnostics;
using PoSeeReview.Api;

namespace PoSeeReview.Api.Telemetry;

/// <summary>
/// Custom OpenTelemetry instrumentation for PoSeeReview business metrics and traces.
/// Provides ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class PoSeeReviewTelemetry
{
    /// <summary>
    /// Activity source name for custom distributed tracing.
    /// Matches the pattern configured in Program.cs: "PoSeeReview.*"
    /// </summary>
    public const string ActivitySourceName = "PoSeeReview.Api";

    /// <summary>
    /// Meter name for custom business metrics.
    /// Matches the pattern configured in Program.cs: "PoSeeReview.*"
    /// </summary>
    public const string MeterName = "PoSeeReview.Api";

    /// <summary>
    /// ActivitySource for creating custom spans in distributed traces.
    /// Use to track business operations like comic generation, restaurant lookups, etc.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    /// <summary>
    /// Meter for creating custom business metrics.
    /// Use to track counters, histograms, and gauges for business KPIs.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Custom Metrics

    /// <summary>
    /// Counter for total comics generated.
    /// Tracks comic generation requests (including cache hits and regenerations).
    /// Tags: cache_hit (true/false), force_regenerate (true/false)
    /// </summary>
    public static readonly Counter<long> ComicsGenerated = Meter.CreateCounter<long>(
        name: "po.seereview.comics.generated",
        unit: "comics",
        description: "Total number of comics generated or served from cache");

    /// <summary>
    /// Counter for comic generation failures.
    /// Tracks failed comic generation attempts by error type.
    /// Tags: error_type (dall-e, storage, restaurant_not_found, etc.)
    /// </summary>
    public static readonly Counter<long> ComicGenerationErrors = Meter.CreateCounter<long>(
        name: "po.seereview.comics.errors",
        unit: "errors",
        description: "Total number of comic generation failures");

    /// <summary>
    /// Histogram for comic generation duration.
    /// Tracks time spent generating comics (excludes cache hits).
    /// Unit: milliseconds
    /// Tags: cache_hit (true/false)
    /// </summary>
    public static readonly Histogram<double> ComicGenerationDuration = Meter.CreateHistogram<double>(
        name: "po.seereview.comics.generation_duration",
        unit: "ms",
        description: "Duration of comic generation requests in milliseconds");

    /// <summary>
    /// Counter for force-regenerate requests on the comics POST endpoint.
    /// Elevated frequency may indicate cost abuse or a client bug.
    /// Tags: place_id
    /// </summary>
    public static readonly Counter<long> ForceRegenerateRequests = Meter.CreateCounter<long>(
        name: "po.seereview.comics.force_regenerate",
        unit: "requests",
        description: "Total number of force-regenerate comic requests");

    /// <summary>
    /// Counter for generation requests refused by the daily budget rather than the rate limiter.
    /// A rising service_exhausted count is the signal that the app-wide ceiling is the binding
    /// constraint and needs raising — or that something is burning the budget.
    /// Tags: scope (user/service)
    /// </summary>
    public static readonly Counter<long> GenerationBudgetRejections = Meter.CreateCounter<long>(
        name: "po.seereview.comics.budget_rejections",
        unit: "requests",
        description: "Generation requests refused because a daily budget was exhausted");

    /// <summary>
    /// Counter for viewer reports of comics, by reason. The moderation signal the app had no
    /// way to receive before — the takedown path is admin-key only.
    /// Tags: reason
    /// </summary>
    public static readonly Counter<long> ComicReports = Meter.CreateCounter<long>(
        name: "po.seereview.comics.reports",
        unit: "reports",
        description: "Viewer reports submitted against generated comics");

    /// <summary>
    /// Counter for client-reported funnel steps. The browser-side half of the PRD's success
    /// metrics: location grants, abandons and shares are invisible to the server otherwise.
    /// Tags: step
    /// </summary>
    public static readonly Counter<long> FunnelEvents = Meter.CreateCounter<long>(
        name: "po.seereview.funnel.events",
        unit: "events",
        description: "Client-reported funnel steps");
}
