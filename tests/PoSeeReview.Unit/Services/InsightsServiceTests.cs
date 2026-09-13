using PoSeeReview.Api.Features.Insights;
using Xunit;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// The Insights aggregation, which is the only part of that slice worth covering by unit test —
/// the repository is two table scans and the endpoint is a passthrough.
/// <para>
/// Most of these guard decisions that would otherwise silently produce a plausible-looking but
/// wrong chart: a histogram that drops its empty buckets reads as missing data rather than as
/// zero, a weekly line that zero-fills a quiet week asserts an average strangeness of zero for
/// restaurants nobody drew, and a distribution that counts every regeneration weights itself by
/// how often someone hit redraw.
/// </para>
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class InsightsServiceTests
{
    private static readonly InsightsOptions Permissive = new()
    {
        MinScatterPoints = 1,
        MinDistributionRestaurants = 1,
        MinRegions = 1,
        MinWeeks = 1
    };

    private static ArchivedScoreRow Score(
        string placeId,
        double score,
        string region = "US-WA-Seattle",
        string weekKey = "2026-W10",
        string name = "Somewhere") => new()
        {
            PlaceId = placeId,
            RestaurantName = name,
            Region = region,
            WeekKey = weekKey,
            StrangenessScore = score
        };

    private static RestaurantFactsRow Facts(string placeId, double rating, int reviews = 100) => new()
    {
        PlaceId = placeId,
        Name = "Somewhere",
        AverageRating = rating,
        TotalReviews = reviews
    };

    private static InsightsSnapshot Snapshot(
        IEnumerable<ArchivedScoreRow> scores,
        IEnumerable<RestaurantFactsRow>? restaurants = null,
        bool truncated = false) =>
        new(
            [.. scores],
            (restaurants ?? []).ToDictionary(r => r.PlaceId, StringComparer.Ordinal),
            truncated);

    [Fact]
    public void Aggregate_EmptySnapshot_ReturnsZeroedSummaryRatherThanThrowing()
    {
        var result = InsightsService.Aggregate(Snapshot([]), Permissive);

        Assert.Equal(0, result.Summary.ScoredRestaurants);
        Assert.Equal(0, result.Summary.RegionCount);
        Assert.Empty(result.ScoreDistribution);
        Assert.Empty(result.WeeklyTrend);
    }

    [Theory]
    [InlineData(0, 0)]     // exactly zero lands in the first bucket
    [InlineData(9.9, 0)]
    [InlineData(10, 1)]    // a boundary belongs to the bucket it opens
    [InlineData(55, 5)]
    [InlineData(99.9, 9)]
    [InlineData(100, 9)]   // the top bucket is closed, or 100 falls off the end of the array
    public void BucketIndexFor_PlacesBoundaryScoresInTheRightBucket(double score, int expected) =>
        Assert.Equal(expected, InsightsService.BucketIndexFor(score));

    [Fact]
    public void Aggregate_EmitsEveryBucketIncludingEmptyOnes()
    {
        var scores = Enumerable.Range(0, 12).Select(i => Score($"p{i}", 95)).ToList();

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        // Ten buckets always. A gap in a histogram has to read as "none scored here", and an
        // omitted bucket reads as "we did not measure".
        Assert.Equal(10, result.ScoreDistribution.Count);
        Assert.Equal(12, result.ScoreDistribution.Single(b => b.BucketStart == 90).Count);
        Assert.All(result.ScoreDistribution.Where(b => b.BucketStart != 90), b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Aggregate_CountsOnePlaceOnce_UsingItsHighestScore()
    {
        var scores = new[]
        {
            Score("same", 20, weekKey: "2026-W10"),
            Score("same", 90, weekKey: "2026-W11"),
            Score("same", 55, weekKey: "2026-W12")
        };

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        // One restaurant, not three. A place someone regenerates weekly must not outvote a place
        // drawn once.
        Assert.Equal(1, result.Summary.ScoredRestaurants);
        Assert.Equal(1, result.ScoreDistribution.Single(b => b.BucketStart == 90).Count);
        Assert.Equal(0, result.ScoreDistribution.Single(b => b.BucketStart == 20).Count);
    }

    [Fact]
    public void Aggregate_WeeklyTrend_KeepsEveryWeekAPlaceAppearsIn()
    {
        var scores = new[]
        {
            Score("same", 20, weekKey: "2026-W10"),
            Score("same", 90, weekKey: "2026-W11"),
            Score("same", 55, weekKey: "2026-W12")
        };

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        // The opposite rule to the distribution, and deliberately so: this chart is about
        // activity over time, so the per-place dedup would erase the thing it measures.
        Assert.Equal(3, result.WeeklyTrend.Count);
        Assert.Equal(["2026-W10", "2026-W11", "2026-W12"], result.WeeklyTrend.Select(w => w.WeekKey));
    }

    [Fact]
    public void Aggregate_WeeklyTrend_OmitsQuietWeeksRatherThanZeroFilling()
    {
        var scores = new[]
        {
            Score("a", 40, weekKey: "2026-W10"),
            Score("b", 60, weekKey: "2026-W14")
        };

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        // Weeks 11-13 are absent, not present with an average of zero. "Nobody drew a comic" and
        // "every comic scored zero" are different claims.
        Assert.Equal(2, result.WeeklyTrend.Count);
        Assert.DoesNotContain(result.WeeklyTrend, w => w.WeekKey == "2026-W12");
    }

    [Fact]
    public void Aggregate_WeeklyTrend_DropsRowsWithAnUnparseableWeekKey()
    {
        var scores = new[]
        {
            Score("a", 40, weekKey: "2026-W10"),
            Score("b", 60, weekKey: "not-a-week"),
            Score("c", 70, weekKey: "2026-W99")
        };

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        Assert.Single(result.WeeklyTrend);
        Assert.Equal("2026-W10", result.WeeklyTrend[0].WeekKey);

        // Dropped from the trend only. The other three charts need nothing but the score, so the
        // rows still count there.
        Assert.Equal(3, result.Summary.ScoredRestaurants);
    }

    [Fact]
    public void Aggregate_Scatter_ExcludesRowsWithNoRestaurantMatch_ButKeepsThemElsewhere()
    {
        var scores = new[] { Score("has-facts", 70), Score("no-facts", 80) };
        var facts = new[] { Facts("has-facts", 4.2) };

        var result = InsightsService.Aggregate(Snapshot(scores, facts), Permissive);

        Assert.Single(result.ScoreVsRating);
        Assert.Equal("has-facts", scores[0].PlaceId);
        Assert.Equal(2, result.Summary.ScoredRestaurants);
    }

    [Fact]
    public void Aggregate_Scatter_ExcludesUnratedRestaurants()
    {
        // A rating of zero means "not rated", not "rated zero", and plotting it at x=0 invents a
        // terrible restaurant that does not exist.
        var scores = new[] { Score("rated", 70), Score("unrated", 80) };
        var facts = new[] { Facts("rated", 4.2), Facts("unrated", 0) };

        var result = InsightsService.Aggregate(Snapshot(scores, facts), Permissive);

        Assert.Single(result.ScoreVsRating);
    }

    [Fact]
    public void Aggregate_Regions_AreOrderedByAverageScoreDescending()
    {
        var scores = new[]
        {
            Score("a", 20, region: "US-CA-SF"),
            Score("b", 40, region: "US-CA-SF"),
            Score("c", 90, region: "GB-LDN")
        };

        var result = InsightsService.Aggregate(Snapshot(scores), Permissive);

        Assert.Equal(["GB-LDN", "US-CA-SF"], result.Regions.Select(r => r.Region));
        Assert.Equal(30, result.Regions.Single(r => r.Region == "US-CA-SF").AverageScore);
        Assert.Equal(40, result.Regions.Single(r => r.Region == "US-CA-SF").PeakScore);
    }

    [Fact]
    public void Aggregate_BelowMinimumSample_ReturnsAnEmptyChartWithoutEmptyingTheOthers()
    {
        var options = new InsightsOptions
        {
            MinScatterPoints = 10,
            MinDistributionRestaurants = 1,
            MinRegions = 2,
            MinWeeks = 99
        };

        var scores = new[] { Score("a", 30, region: "US"), Score("b", 70, region: "GB") };
        var facts = new[] { Facts("a", 4.0), Facts("b", 3.0) };

        var result = InsightsService.Aggregate(Snapshot(scores, facts), options);

        // Each chart is judged on its own sample. A thin scatter must not blank out a region
        // comparison that has enough to say something.
        Assert.Empty(result.ScoreVsRating);
        Assert.Empty(result.WeeklyTrend);
        Assert.Equal(10, result.ScoreDistribution.Count);
        Assert.Equal(2, result.Regions.Count);
    }

    [Fact]
    public void Aggregate_CarriesTheTruncatedFlagThrough()
    {
        var result = InsightsService.Aggregate(Snapshot([Score("a", 50)], truncated: true), Permissive);

        Assert.True(result.Summary.Truncated);
        Assert.False(result.Summary.IsMockData);
    }
}
