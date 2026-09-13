using PoSeeReview.Api.Abstractions;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// Deterministic sample data so the four charts can be developed and demoed before the app has
/// enough real history to clear their minimum sample sizes.
/// <para>
/// Registered as <see cref="IMockable"/> alongside the service interface, so
/// <c>/diag/mock-status</c> reports it and the client's "USING MOCK DATA" banner lights up
/// (NET_RULES 6.5). Registration is environment-gated: this type is never constructed in
/// Production, following the same posture as <c>FakeAuthHandler</c>.
/// </para>
/// </summary>
public sealed class MockInsightsService : IInsightsService, IMockable
{
    /// <summary>
    /// Fixed rather than random. A chart that redraws differently on every refresh cannot be
    /// screenshotted, diffed, or asserted on by a UI test.
    /// </summary>
    private static readonly (string Name, string Region, double Stars, double Score, int Reviews)[] Sample =
    [
        ("The Gilded Spatula", "US-WA-Seattle", 4.6, 91, 1840),
        ("Nonna's Basement", "US-WA-Seattle", 3.2, 78, 402),
        ("Cafe Perpetual", "US-WA-Seattle", 4.1, 64, 913),
        ("Hot Pot Confidential", "US-CA-SF", 4.8, 88, 2611),
        ("The Second Breakfast", "US-CA-SF", 2.9, 72, 188),
        ("Salt & Suspicion", "US-CA-SF", 3.7, 55, 640),
        ("Fry Theory", "US-CA-SF", 4.4, 41, 1275),
        ("Wok This Way", "US-NY-NYC", 4.2, 96, 3302),
        ("The Quiet Deli", "US-NY-NYC", 3.5, 83, 214),
        ("Midnight Congee", "US-NY-NYC", 4.9, 69, 1508),
        ("Bagel Anomaly", "US-NY-NYC", 2.4, 37, 96),
        ("The Last Taqueria", "GB-LDN", 4.0, 74, 705),
        ("Pudding Club", "GB-LDN", 3.8, 58, 331),
        ("Chip Shop Eternal", "GB-LDN", 4.5, 24, 1122)
    ];

    public Task<InsightsDto> GetInsightsAsync(CancellationToken cancellationToken = default)
    {
        var scatter = Sample
            .Select(s => new ScoreVsRatingPointDto(s.Name, s.Stars, s.Score, s.Reviews))
            .OrderBy(p => p.StarRating)
            .ToList();

        var counts = new int[10];
        foreach (var entry in Sample)
        {
            counts[Math.Clamp((int)(entry.Score / 10), 0, 9)]++;
        }

        var distribution = Enumerable.Range(0, 10)
            .Select(i => new ScoreBucketDto(i * 10, (i * 10) + 10, counts[i]))
            .ToList();

        var regions = Sample
            .GroupBy(s => s.Region, StringComparer.Ordinal)
            .Select(g => new RegionStatsDto(
                g.Key,
                Math.Round(g.Average(s => s.Score), 1),
                Math.Round(g.Max(s => s.Score), 1),
                g.Count()))
            .OrderByDescending(r => r.AverageScore)
            .ToList();

        // Five consecutive weeks ending on the most recently completed one, so the trend line
        // always looks current without depending on today's date landing mid-week.
        var thisMonday = DateTimeOffset.UtcNow.Date.AddDays(-(int)DateTimeOffset.UtcNow.DayOfWeek + 1);
        var weekly = new List<WeeklyPointDto>();
        double[] averages = [52.4, 61.8, 58.2, 70.5, 73.1];
        double[] peaks = [78, 88, 83, 96, 91];

        for (var i = 0; i < averages.Length; i++)
        {
            var start = new DateTimeOffset(thisMonday.AddDays(-7 * (averages.Length - i)), TimeSpan.Zero);
            weekly.Add(new WeeklyPointDto(
                WeekKey: $"{System.Globalization.ISOWeek.GetYear(start.UtcDateTime)}-W{System.Globalization.ISOWeek.GetWeekOfYear(start.UtcDateTime):D2}",
                WeekStart: start,
                AverageScore: averages[i],
                PeakScore: peaks[i],
                EntryCount: 6 + i));
        }

        var summary = new InsightsSummaryDto(
            ScoredRestaurants: Sample.Length,
            RegionCount: regions.Count,
            WeekCount: weekly.Count,
            Truncated: false,
            IsMockData: true);

        return Task.FromResult(new InsightsDto(summary, scatter, distribution, regions, weekly));
    }
}
