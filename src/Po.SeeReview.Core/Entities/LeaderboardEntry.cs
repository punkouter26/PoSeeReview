namespace Po.SeeReview.Core.Entities;

/// <summary>
/// Domain entity representing a leaderboard entry for top-ranked comics
/// Tracks highest strangeness score per restaurant per region
/// </summary>
public class LeaderboardEntry
{
    /// <summary>
    /// 1-based ranking position in leaderboard
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Google Maps Place ID (unique identifier for restaurant)
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant display name
    /// </summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// Full address of the restaurant
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Geographic region code (e.g., US-WA-Seattle)
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Highest strangeness score achieved by any comic for this restaurant (0-100)
    /// </summary>
    public double StrangenessScore { get; set; }

    /// <summary>
    /// URL to the comic image in Azure Blob Storage (thumbnail for leaderboard display)
    /// </summary>
    public string ComicBlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// Last time this leaderboard entry was updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
