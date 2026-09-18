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
    /// Name of the table holding short share codes in both directions (code to place, place to
    /// code). Not co-located with comics: a short link has to keep resolving after the comic it
    /// points at has expired and been cleaned up.
    /// </summary>
    public string ShareLinksTableName { get; set; } = "PoSeeReviewShareLinks";

    /// <summary>
    /// Name of the table holding per-place moderation state (hidden, suppressed, report count).
    /// A row exists only once something has happened, so the table stays proportional to the
    /// problem rather than to the catalogue.
    /// </summary>
    public string ModerationTableName { get; set; } = "PoSeeReviewModeration";
}
