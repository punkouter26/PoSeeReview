using Microsoft.Extensions.Options;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// The aggregation. Pure maths over rows the repository handed back — no storage concerns here,
/// which is what makes it the part worth unit testing.
/// </summary>
public sealed class InsightsService(
    InsightsRepository repository,
    IOptions<InsightsOptions> options,
    ILogger<InsightsService> logger) : IInsightsService
{
    /// <summary>Width of a histogram bucket, in score points.</summary>
    private const int BucketWidth = 10;

    /// <summary>Number of buckets covering 0-100.</summary>
    private const int BucketCount = 100 / BucketWidth;

    public async Task<InsightsDto> GetInsightsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await repository.LoadAsync(cancellationToken);
        return Aggregate(snapshot, options.Value, logger);
    }

    /// <summary>
    /// Projects a raw snapshot into the four charts. Static and dependency-free so a test can
    /// hand it rows directly.
    /// </summary>
    public static InsightsDto Aggregate(InsightsSnapshot snapshot, InsightsOptions options, ILogger? logger = null)
    {
        var scores = snapshot.Scores
            .Where(row => !string.IsNullOrWhiteSpace(row.PlaceId))
            .ToList();

        // One place, one score, for anything that describes the population of restaurants. A
        // place regenerated every week would otherwise weight the distribution by how often
        // someone happened to redraw it rather than by how strange it is.
        var peakByPlace = scores
            .GroupBy(row => row.PlaceId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(row => row.StrangenessScore)!)
            .ToList();

        var scatter = BuildScatter(peakByPlace, snapshot.Restaurants, options);
        var distribution = BuildDistribution(peakByPlace, options);
        var regions = BuildRegions(peakByPlace, options);
        var weekly = BuildWeekly(scores, options, logger);

        var summary = new InsightsSummaryDto(
            ScoredRestaurants: peakByPlace.Count,
            RegionCount: peakByPlace
                .Select(row => Normalize(row.Region))
                .Where(region => region.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            WeekCount: scores
                .Select(row => row.WeekKey)
                .Where(week => !string.IsNullOrWhiteSpace(week))
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Truncated: snapshot.Truncated,
            IsMockData: false);

        return new InsightsDto(summary, scatter, distribution, regions, weekly);
    }

    /// <summary>
    /// Strangeness against star rating, one point per restaurant.
    /// <para>
    /// A score with no matching restaurant row is dropped <em>here only</em> — it has no x-value
    /// — but stays in every other chart, which needs nothing but the score itself. A rating of
    /// zero means "not rated", not "rated zero", so it is excluded the same way.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ScoreVsRatingPointDto> BuildScatter(
        IReadOnlyList<ArchivedScoreRow> peakByPlace,
        IReadOnlyDictionary<string, RestaurantFactsRow> restaurants,
        InsightsOptions options)
    {
        var points = new List<ScoreVsRatingPointDto>();

        foreach (var row in peakByPlace)
        {
            if (!restaurants.TryGetValue(row.PlaceId, out var facts) || facts.AverageRating <= 0)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(row.RestaurantName) ? row.RestaurantName : facts.Name;

            points.Add(new ScoreVsRatingPointDto(
                RestaurantName: name,
                StarRating: Math.Round(facts.AverageRating, 2),
                StrangenessScore: Math.Round(row.StrangenessScore, 1),
                TotalReviews: Math.Max(0, facts.TotalReviews)));
        }

        return points.Count >= options.MinScatterPoints
            ? points.OrderBy(p => p.StarRating).ToList()
            : [];
    }

    /// <summary>
    /// Ten fixed buckets. Empty ones are emitted with a count of zero — a gap in a histogram
    /// must read as "none scored here", not as a missing measurement.
    /// </summary>
    private static IReadOnlyList<ScoreBucketDto> BuildDistribution(
        IReadOnlyList<ArchivedScoreRow> peakByPlace,
        InsightsOptions options)
    {
        if (peakByPlace.Count < options.MinDistributionRestaurants)
        {
            return [];
        }

        var counts = new int[BucketCount];

        foreach (var row in peakByPlace)
        {
            counts[BucketIndexFor(row.StrangenessScore)]++;
        }

        return [.. Enumerable.Range(0, BucketCount).Select(i => new ScoreBucketDto(
            BucketStart: i * BucketWidth,
            BucketEnd: (i * BucketWidth) + BucketWidth,
            Count: counts[i]))];
    }

    /// <summary>
    /// Bucket for a score, clamped at both ends. The top bucket is closed so a score of exactly
    /// 100 lands in the 90-100 bucket rather than falling off the end of the array.
    /// </summary>
    internal static int BucketIndexFor(double score) =>
        Math.Clamp((int)(score / BucketWidth), 0, BucketCount - 1);

    private static IReadOnlyList<RegionStatsDto> BuildRegions(
        IReadOnlyList<ArchivedScoreRow> peakByPlace,
        InsightsOptions options)
    {
        var regions = peakByPlace
            .Select(row => (Region: Normalize(row.Region), row.StrangenessScore))
            .Where(row => row.Region.Length > 0)
            .GroupBy(row => row.Region, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RegionStatsDto(
                Region: group.Key,
                AverageScore: Math.Round(group.Average(row => row.StrangenessScore), 1),
                PeakScore: Math.Round(group.Max(row => row.StrangenessScore), 1),
                EntryCount: group.Count()))
            .OrderByDescending(region => region.AverageScore)
            .ThenBy(region => region.Region, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return regions.Count >= options.MinRegions ? regions : [];
    }

    /// <summary>
    /// Average and peak per ISO week, oldest first.
    /// <para>
    /// Weeks with no entries are omitted rather than zero-filled: an average strangeness of zero
    /// is a claim about restaurants nobody drew, where an absent point is simply honest. A place
    /// scored in several weeks contributes to each of them — this chart is about activity over
    /// time, so the per-place dedup that the distribution needs would be wrong here.
    /// </para>
    /// </summary>
    private static IReadOnlyList<WeeklyPointDto> BuildWeekly(
        IReadOnlyList<ArchivedScoreRow> scores,
        InsightsOptions options,
        ILogger? logger)
    {
        var points = new List<WeeklyPointDto>();

        foreach (var group in scores.GroupBy(row => row.WeekKey, StringComparer.Ordinal))
        {
            var start = WeekKeys.StartFor(group.Key);
            if (start is null)
            {
                logger?.LogWarning(
                    "Dropping {Count} insight row(s) with an unparseable week key {WeekKey}",
                    group.Count(), group.Key);
                continue;
            }

            points.Add(new WeeklyPointDto(
                WeekKey: group.Key,
                WeekStart: start.Value,
                AverageScore: Math.Round(group.Average(row => row.StrangenessScore), 1),
                PeakScore: Math.Round(group.Max(row => row.StrangenessScore), 1),
                EntryCount: group.Count()));
        }

        points.Sort((left, right) => left.WeekStart.CompareTo(right.WeekStart));

        return points.Count >= options.MinWeeks ? points : [];
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}
