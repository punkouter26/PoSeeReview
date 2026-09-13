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
}
