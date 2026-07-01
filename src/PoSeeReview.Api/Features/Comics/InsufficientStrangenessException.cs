namespace PoSeeReview.Core;

/// <summary>
/// Thrown when a restaurant's reviews are too ordinary — the computed strangeness score falls
/// below <c>Comics:MinimumStrangenessScore</c>. Honors the PRD contract of returning a clear
/// "refreshingly normal" response (HTTP 422) instead of publishing a low-quality comic.
/// Extends InvalidOperationException so general callers keep working while specific callers
/// can catch this precise type.
/// </summary>
public sealed class InsufficientStrangenessException : InvalidOperationException
{
    public int StrangenessScore { get; }
    public int MinimumRequired { get; }

    public InsufficientStrangenessException(int strangenessScore, int minimumRequired)
        : base($"Strangeness score {strangenessScore} is below the minimum {minimumRequired} required to generate a comic.")
    {
        StrangenessScore = strangenessScore;
        MinimumRequired = minimumRequired;
    }
}
