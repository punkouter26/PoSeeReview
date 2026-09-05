namespace PoSeeReview.Api.Storage;

/// <summary>
/// Configuration options for Azure Storage services
/// </summary>
public class AzureStorageOptions
{
    public const string SectionName = "AzureStorage";

    /// <summary>
    /// Connection string for Azure Storage (shared for Table and Blob)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Name of the table for comics storage
    /// </summary>
    public string ComicsTableName { get; set; } = "PoSeeReviewComics";

    /// <summary>
    /// Name of the blob container for comic images
    /// </summary>
    public string ComicsContainerName { get; set; } = "comics";

    /// <summary>
    /// Name of the table for restaurant data
    /// </summary>
    public string RestaurantsTableName { get; set; } = "PoSeeReviewRestaurants";

    /// <summary>
    /// Name of the table for leaderboard data
    /// </summary>
    public string LeaderboardTableName { get; set; } = "PoSeeReviewLeaderboard";

    /// <summary>
    /// Name of the table holding viewer reports of comics (public intake, not owner takedowns).
    /// </summary>
    public string ReportsTableName { get; set; } = "PoSeeReviewReports";

    /// <summary>
    /// Name of the table holding per-comic reaction tallies and per-user reaction rows.
    /// </summary>
    public string ReactionsTableName { get; set; } = "PoSeeReviewReactions";

    /// <summary>
    /// Name of the table holding the permanent weekly archive promoted out of the live
    /// leaderboard before expiry cleanup runs.
    /// </summary>
    public string HallOfFameTableName { get; set; } = "PoSeeReviewHallOfFame";

    /// <summary>
    /// Name of the table holding per-user and app-wide daily generation counters. This is the
    /// only durable record of paid spend, so it is not co-located with the comics table whose
    /// rows the cleanup service purges.
    /// </summary>
    public string BudgetTableName { get; set; } = "PoSeeReviewBudget";

    /// <summary>
    /// Name of the table holding daily funnel counters reported by the client.
    /// </summary>
    public string AnalyticsTableName { get; set; } = "PoSeeReviewAnalytics";
}
