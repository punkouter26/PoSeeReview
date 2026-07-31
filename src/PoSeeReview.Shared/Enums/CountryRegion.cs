namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Closed set of ISO 3166-1 alpha-2 country codes the coordinate-to-region mapper can emit.
/// Replaces the bare <c>"US"</c>/<c>"CA"</c>/<c>"GB"</c>/<c>"AU"</c> literals that were duplicated
/// between the region mapper and the cache-probe loop (NET_RULES 1.5 — zero magic strings).
/// </summary>
public enum CountryRegion
{
    /// <summary>United States — also the fallback when coordinates match nothing.</summary>
    US = 0,

    /// <summary>Canada.</summary>
    CA = 1,

    /// <summary>United Kingdom.</summary>
    GB = 2,

    /// <summary>Australia.</summary>
    AU = 3
}
