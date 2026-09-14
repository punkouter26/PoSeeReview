namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Configuration options for comic generation business rules.
/// Centralises constants that were previously hard-coded in the generation pipeline so they
/// are runtime-configurable without a redeploy. The leaderboard threshold lives in
/// LeaderboardOptions, owned by that slice.
/// </summary>
public class ComicOptions
{
    public const string SectionName = "Comics";

    /// <summary>
    /// Minimum number of raw reviews needed before a comic can be generated, also applied to
    /// the surviving set after inappropriate-content filtering. 1 — the default — is the floor:
    /// a comic needs at least one review to draw from, so this is as permissive as the pipeline
    /// allows. Raise it to demand more source material per comic.
    /// </summary>
    public int MinimumReviewsRequired { get; set; } = 1;

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
    /// Minimum strangeness score (0–100) a restaurant must reach before a comic is generated;
    /// anything lower is rejected as too ordinary. Scores are never negative, so 0 — the
    /// default — disables the gate entirely and every restaurant gets a comic. Raise it to
    /// reinstate the "too ordinary → 422" rejection. The leaderboard's own admission
    /// threshold lives in LeaderboardOptions and is unaffected.
    /// </summary>
    public int MinimumStrangenessScore { get; set; }

    /// <summary>
    /// Version stamp for the prompt and model behind a comic, folded into the cache check.
    /// <para>
    /// The cache is keyed by place and age alone, so a prompt tune or a model swap kept serving
    /// the old output until somebody paid for a forced regeneration — which made evaluating a
    /// prompt change cost one generation per restaurant tested, and made it easy to compare a
    /// new prompt against an old comic and conclude the wrong thing. Bump this whenever the
    /// prompt, the rubric or the model changes; rows written under a different version simply
    /// miss the cache and are regenerated on demand, with no migration and no bulk job.
    /// </para>
    /// </summary>
    public int PromptVersion { get; set; } = 1;
}
