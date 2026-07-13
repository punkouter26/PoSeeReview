namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Data transfer object for leaderboard entries
/// Used in GET /api/leaderboard responses
/// </summary>
public class LeaderboardEntryDto
{
    /// <summary>
    /// 1-based ranking position in leaderboard
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Google Maps Place ID
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name
    /// </summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// Full address
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., US, GB, AU)
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Strangeness score (0-100)
    /// </summary>
    public double StrangenessScore { get; set; }

    /// <summary>
    /// URL to comic image (for thumbnail display)
    /// </summary>
    public string ComicBlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// When this entry was last updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
