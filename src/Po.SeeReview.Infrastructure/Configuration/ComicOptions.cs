namespace Po.SeeReview.Infrastructure.Configuration;

/// <summary>
/// Configuration options for comic generation business rules.
/// Centralises constants that were previously hard-coded in ComicGenerationService
/// and LeaderboardService so they are runtime-configurable without a redeploy.
/// </summary>
public class ComicOptions
{
    public const string SectionName = "Comics";

    /// <summary>
    /// Minimum number of raw reviews needed before a comic can be generated.
    /// </summary>
    public int MinimumReviewsRequired { get; set; } = 5;

    /// <summary>
    /// Maximum number of reviews forwarded to GPT for strangeness analysis
    /// (controls cost — fewer reviews = cheaper prompt).
    /// </summary>
    public int MaximumReviewsForAnalysis { get; set; } = 5;

    /// <summary>
    /// How many days a generated comic is cached before it is considered stale.
    /// </summary>
    public int CacheDurationDays { get; set; } = 7;

    /// <summary>
    /// Minimum strangeness score (0–100) required for a restaurant to appear
    /// on the global leaderboard.
    /// </summary>
    public int MinimumStrangenessScore { get; set; } = 20;
}
