using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Po.SeeReview.Api.Telemetry;

/// <summary>
/// Custom OpenTelemetry instrumentation for PoSeeReview business metrics and traces.
/// Provides ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class PoSeeReviewTelemetry
{
    /// <summary>
    /// Activity source name for custom distributed tracing.
    /// Matches the pattern configured in Program.cs: "Po.SeeReview.*"
    /// </summary>
    public const string ActivitySourceName = "Po.SeeReview.Api";

    /// <summary>
    /// Meter name for custom business metrics.
    /// Matches the pattern configured in Program.cs: "Po.SeeReview.*"
    /// </summary>
    public const string MeterName = "Po.SeeReview.Api";

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
    /// Counter for restaurant lookups.
    /// Tracks restaurant API queries by source (Google Maps, cache, etc.).
    /// Tags: source (google_maps, cache), success (true/false)
    /// </summary>
    public static readonly Counter<long> RestaurantLookups = Meter.CreateCounter<long>(
        name: "po.seereview.restaurants.lookups",
        unit: "lookups",
        description: "Total number of restaurant lookups");
}
