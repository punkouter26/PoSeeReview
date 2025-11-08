using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Repository for leaderboard persistence in Azure Table Storage
/// Handles inverted RowKey for descending sort by strangeness score
/// </summary>
public interface ILeaderboardRepository
{
    /// <summary>
    /// Retrieves top N entries for a region, pre-sorted by strangeness score (descending)
    /// </summary>
    /// <param name="region">Geographic region code</param>
    /// <param name="limit">Maximum number of entries to return</param>
    /// <returns>List of leaderboard entries sorted by score (highest first)</returns>
    Task<List<LeaderboardEntry>> GetTopEntriesAsync(string region, int limit);

    /// <summary>
    /// Gets a specific leaderboard entry by place ID and region
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="region">Geographic region code</param>
    /// <returns>Leaderboard entry if found, null otherwise</returns>
    Task<LeaderboardEntry?> GetByPlaceIdAsync(string placeId, string region);

    /// <summary>
    /// Inserts or updates a leaderboard entry
    /// Uses upsert semantics - replaces existing entry for same PlaceId/Region
    /// </summary>
    /// <param name="entry">Leaderboard entry to persist</param>
    Task UpsertAsync(LeaderboardEntry entry);

    /// <summary>
    /// Deletes a leaderboard entry
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="region">Geographic region code</param>
    Task DeleteAsync(string placeId, string region);

    /// <summary>
    /// Deletes all leaderboard entries for a specific place ID across all regions
    /// Used for takedown requests
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    Task DeleteByPlaceIdAsync(string placeId);
}
