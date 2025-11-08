using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for managing the global strangeness leaderboard
/// Tracks top 10 strangest comics per region
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Retrieves top N comics for a region ranked by strangeness score
    /// </summary>
    /// <param name="region">Geographic region code (e.g., US-WA-Seattle)</param>
    /// <param name="limit">Number of entries to return (default 10, max 50)</param>
    /// <returns>List of leaderboard entries with assigned ranks</returns>
    Task<List<LeaderboardEntry>> GetTopComicsAsync(string region, int limit = 10);

    /// <summary>
    /// Inserts or updates a leaderboard entry for a restaurant
    /// Updates if new score is higher than existing entry
    /// </summary>
    /// <param name="entry">Leaderboard entry to upsert</param>
    Task UpsertEntryAsync(LeaderboardEntry entry);
}
