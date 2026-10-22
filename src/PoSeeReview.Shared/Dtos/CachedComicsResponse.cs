namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Which of a batch of places already have a live cached comic.
/// <para>
/// A cache hit is free and lands in well under a second; a miss spends a paid image call and
/// about ten. The discovery grid never distinguished them, so the app's cheapest and fastest
/// result looked exactly like its most expensive one.
/// </para>
/// </summary>
public class CachedComicsResponse
{
    /// <summary>
    /// The subset of the requested place ids with an unexpired comic. Order is not meaningful.
    /// </summary>
    public List<string> CachedPlaceIds { get; set; } = [];

    /// <summary>
    /// Strangeness score of each cached place, keyed by place id. The map colours its pins from
    /// it; a place that is not drawn yet has no score to show and is absent.
    /// </summary>
    public Dictionary<string, int> Scores { get; set; } = [];

    /// <summary>
    /// The weirdness forecast for places with no live comic: the score the scorer last gave their
    /// reviews under the current prompt, keyed by place id. Read from a cache, never computed for
    /// the request, so it covers only places someone has tried before. Absent when unknown.
    /// </summary>
    public Dictionary<string, int> Forecasts { get; set; } = [];

    /// <summary>The score below which a comic is refused as too ordinary (<c>Comics:MinimumStrangenessScore</c>).</summary>
    public int ForecastFloor { get; set; }
}
