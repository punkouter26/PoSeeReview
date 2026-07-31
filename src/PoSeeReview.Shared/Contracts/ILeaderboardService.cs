using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// Manages the global strangeness leaderboard (top N comics per region). Declared in Shared
/// because the Comics slice publishes scores to it without referencing the Leaderboard slice
/// (NET_RULES 2.2).
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Retrieves top N comics for a region ranked by strangeness score.
    /// </summary>
    /// <param name="region">Geographic region code (e.g. US-WA-Seattle)</param>
    /// <param name="limit">Number of entries to return (default 10, max 50)</param>
    /// <returns>List of leaderboard entries with assigned ranks</returns>
    Task<List<LeaderboardEntry>> GetTopComicsAsync(RegionCode region, int limit = 10);

    /// <summary>
    /// Inserts or updates a leaderboard entry, keeping the higher of the two scores.
    /// </summary>
    /// <param name="entry">Leaderboard entry to upsert</param>
    Task UpsertEntryAsync(LeaderboardEntry entry);
}
