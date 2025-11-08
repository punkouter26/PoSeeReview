using System.ComponentModel.DataAnnotations;

namespace Po.SeeReview.Shared.Models;

/// <summary>
/// Data transfer object for leaderboard entries
/// Used in GET /api/leaderboard responses
/// </summary>
public class LeaderboardEntryDto
{
    /// <summary>
    /// 1-based ranking position in leaderboard
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int Rank { get; set; }

    /// <summary>
    /// Google Maps Place ID
    /// </summary>
    [Required]
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name
    /// </summary>
    [Required]
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// Full address
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., US, GB, AU)
    /// </summary>
    [Required]
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Strangeness score (0-100)
    /// </summary>
    [Required]
    [Range(0, 100)]
    public double StrangenessScore { get; set; }

    /// <summary>
    /// URL to comic image (for thumbnail display)
    /// </summary>
    [Required]
    [Url]
    public string ComicBlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// When this entry was last updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
