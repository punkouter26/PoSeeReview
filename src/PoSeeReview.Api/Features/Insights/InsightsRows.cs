using System.Globalization;
using Azure;
using Azure.Data.Tables;

namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// Read-only projection of a <c>PoSeeReviewHallOfFame</c> row.
/// <para>
/// Deliberately a separate type from the Leaderboard slice's entity rather than a reference to
/// it: slices must not reference each other (NET_RULES 2.2). Table Storage is schemaless per
/// row, so a POCO carrying a subset of the columns reads the same rows without owning them.
/// </para>
/// </summary>
public class ArchivedScoreRow : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string WeekKey { get; set; } = string.Empty;
    public double StrangenessScore { get; set; }
}

/// <summary>
/// Read-only projection of a <c>PoSeeReviewRestaurants</c> row: the star rating and review count
/// that supply the scatter's x-axis and bubble size.
/// <para>
/// <c>CachedAt</c> is intentionally absent. The Restaurants slice treats a row past the cache
/// window as stale, but for a historical chart the rating as it stood when the comic was drawn
/// is the correct value, not a defect.
/// </para>
/// </summary>
public class RestaurantFactsRow : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
}

/// <summary>Everything one <c>/api/insights</c> call reads, before any aggregation.</summary>
public sealed record InsightsSnapshot(
    IReadOnlyList<ArchivedScoreRow> Scores,
    IReadOnlyDictionary<string, RestaurantFactsRow> Restaurants,
    bool Truncated);

/// <summary>
/// ISO week-key parsing, duplicated here on purpose.
/// <para>
/// The Leaderboard slice writes these keys and this slice reads them; a shared helper would be a
/// cross-slice reference for eight lines of date maths. <see cref="ISOWeek"/> rather than
/// <see cref="Calendar.GetWeekOfYear"/> for the same reason the writer uses it — the latter
/// needs a rule and a first-day argument, and getting either wrong files entries into a
/// neighbouring week at the year boundary.
/// </para>
/// </summary>
internal static class WeekKeys
{
    /// <summary>UTC Monday that starts <paramref name="weekKey"/>, or null when unparseable.</summary>
    public static DateTimeOffset? StartFor(string weekKey)
    {
        var parts = (weekKey ?? string.Empty).Split("-W");

        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var week)
            || year is < 1 or > 9999
            || week is < 1 or > 53
            || week > ISOWeek.GetWeeksInYear(year))
        {
            return null;
        }

        return new DateTimeOffset(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday), TimeSpan.Zero);
    }
}
