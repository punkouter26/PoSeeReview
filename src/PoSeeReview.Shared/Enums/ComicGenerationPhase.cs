namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Stages the comic pipeline actually passes through, streamed to the client so the wait is
/// narrated by real progress instead of a timer (NET_RULES 1.5 — enums over magic values).
/// Each member maps to a genuine step in <c>ComicGenerationService.GenerateComicAsync</c>;
/// there is deliberately no separate "writing the narrative" phase, because the score and the
/// narrative come back from one model call.
/// </summary>
public enum ComicGenerationPhase
{
    /// <summary>A valid cached comic was found — no paid work will run.</summary>
    CacheHit = 0,

    /// <summary>Fetching the restaurant and its reviews from Google Maps.</summary>
    FetchingReviews = 1,

    /// <summary>Scoring strangeness and writing the narrative (one chat completion).</summary>
    AnalyzingStrangeness = 2,

    /// <summary>Rendering the panels with the image model.</summary>
    GeneratingArtwork = 3,

    /// <summary>Stamping readable captions over the rendered panels.</summary>
    ComposingStrip = 4,

    /// <summary>Uploading the image and recording the comic and leaderboard entry.</summary>
    Publishing = 5
}
