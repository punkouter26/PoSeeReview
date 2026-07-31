namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Where a comic returned by <c>GET/POST /api/comics</c> came from
/// (NET_RULES 1.5 — enums over magic booleans).
/// </summary>
public enum ComicCacheState
{
    /// <summary>Generated fresh for this request.</summary>
    Generated = 0,

    /// <summary>Served from a cached entry that is still within its TTL.</summary>
    Cached = 1,

    /// <summary>A cached entry existed but had passed its expiry and was regenerated.</summary>
    Expired = 2
}
