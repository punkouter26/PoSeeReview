namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Cross-restaurant aggregates over every score this app has ever recorded.
/// <para>
/// Every number here comes from Table rows the app already wrote — no Google Maps call, no AI
/// call. The comic experience is one restaurant at a time; this is the only view that looks
/// across them.
/// </para>
/// </summary>
public sealed record InsightsDto(
    InsightsSummaryDto Summary,
    IReadOnlyList<ScoreVsRatingPointDto> ScoreVsRating,
    IReadOnlyList<ScoreBucketDto> ScoreDistribution,
    IReadOnlyList<RegionStatsDto> Regions,
    IReadOnlyList<WeeklyPointDto> WeeklyTrend);

/// <summary>
/// Scope of the data the charts were computed from. <paramref name="Truncated"/> is surfaced in
/// the UI rather than hidden: the reads are cross-partition scans bounded by a row cap, and a
/// chart drawn from a capped sample should say so.
/// </summary>
public sealed record InsightsSummaryDto(
    int ScoredRestaurants,
    int RegionCount,
    int WeekCount,
    bool Truncated,
    bool IsMockData);

/// <summary>One restaurant's strangeness against its Google star rating.</summary>
public sealed record ScoreVsRatingPointDto(
    string RestaurantName,
    double StarRating,
    double StrangenessScore,
    int TotalReviews);

/// <summary>One 10-point histogram bucket. Emitted even when empty — a gap must read as zero.</summary>
public sealed record ScoreBucketDto(int BucketStart, int BucketEnd, int Count);

/// <summary>Aggregate strangeness for one region.</summary>
public sealed record RegionStatsDto(
    string Region,
    double AverageScore,
    double PeakScore,
    int EntryCount);

/// <summary>One ISO week of scores. Weeks with no entries are omitted, never zero-filled.</summary>
public sealed record WeeklyPointDto(
    string WeekKey,
    DateTimeOffset WeekStart,
    double AverageScore,
    double PeakScore,
    int EntryCount);
