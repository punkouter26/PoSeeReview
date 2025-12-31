namespace Po.SeeReview.Infrastructure.Configuration;

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
}
