namespace PoSeeReview.Shared.Contracts;

/// <summary>What a screen decided about a piece of generated text.</summary>
public enum ContentScreenOutcome
{
    /// <summary>Nothing of concern. Publish.</summary>
    Allow,

    /// <summary>
    /// Publish, but put it in front of a human. Reserved for language that is legitimate in a
    /// restaurant review and risky as a model-authored claim about a named business.
    /// </summary>
    Flag,

    /// <summary>Do not publish. The generation is refused before the paid image call.</summary>
    Block
}

/// <summary>
/// The result of screening one piece of generated text.
/// </summary>
/// <param name="Outcome">What to do with it.</param>
/// <param name="Category">
/// Short machine-readable label for why, e.g. <c>allegation</c>. Operator-facing; never rendered
/// to end users, because telling someone which word tripped a filter is a guide to evading it.
/// </param>
public sealed record ContentScreenResult(ContentScreenOutcome Outcome, string? Category)
{
    public static readonly ContentScreenResult Allowed = new(ContentScreenOutcome.Allow, null);

    public bool IsBlocked => Outcome == ContentScreenOutcome.Block;

    public bool IsFlagged => Outcome == ContentScreenOutcome.Flag;
}

/// <summary>
/// Screens model-generated text before it is published.
/// <para>
/// The app publishes AI-written prose about real, named businesses, derived from third-party
/// reviews, and until now nothing looked at it between the model and the user. Declared in
/// Shared because the Comics slice calls it and the Moderation slice implements it
/// (NET_RULES 2.2).
/// </para>
/// <para>
/// This is the seam a hosted classifier drops into. The shipped implementation is a lexical
/// floor, not a classifier — see the implementation for exactly what it does and does not
/// claim to catch.
/// </para>
/// </summary>
public interface IContentSafetyScreener
{
    Task<ContentScreenResult> ScreenAsync(string text, CancellationToken cancellationToken = default);
}
