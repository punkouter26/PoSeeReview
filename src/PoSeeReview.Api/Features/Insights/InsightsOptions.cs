namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// Configuration for the Insights slice. Each slice owns its own options type (NET_RULES 2.2).
/// </summary>
public sealed class InsightsOptions
{
    public const string SectionName = "Insights";

    /// <summary>
    /// Hard ceiling on rows read per table. "All time, everywhere" cannot use the
    /// partition-per-region-week layout, so these are cross-partition scans. The cap is what
    /// keeps an honest-at-current-volume query from becoming a timeout later; the response
    /// reports <c>Truncated</c> when it bites.
    /// </summary>
    public int MaxRowsScanned { get; set; } = 5000;

    /// <summary>Below this a scatter shows no relationship, only noise.</summary>
    public int MinScatterPoints { get; set; } = 10;

    /// <summary>Fewer than this across ten buckets is a bar chart of ones.</summary>
    public int MinDistributionRestaurants { get; set; } = 10;

    /// <summary>A one-bar comparison compares nothing.</summary>
    public int MinRegions { get; set; } = 2;

    /// <summary>Two points is a line segment, not a trend.</summary>
    public int MinWeeks { get; set; } = 3;

    /// <summary>
    /// Serves deterministic sample data instead of reading storage. Registration is
    /// environment-gated as well — this flag cannot turn mocks on in Production.
    /// </summary>
    public bool UseMockData { get; set; }
}
