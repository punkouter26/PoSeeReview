namespace PoSeeReview.Api.Features.Leaderboard;

/// <summary>
/// Configuration for the Hall of Fame. Owned by this slice so the Leaderboard no longer reads
/// the Comics slice's options object (NET_RULES 2.2 — slices must not reference each other).
/// </summary>
public class LeaderboardOptions
{
    public const string SectionName = "Leaderboard";

    /// <summary>
    /// Minimum strangeness score (0–100) required for a restaurant to appear on the
    /// global leaderboard.
    /// </summary>
    public int MinimumStrangenessScore { get; set; } = 20;
}
