namespace PoSeeReview.Shared.Enums;

/// <summary>
/// The reactions a viewer can leave on a comic (NET_RULES 1.5 — enums over magic strings).
/// Deliberately a closed set with no free text: a comment box on AI-generated content about a
/// real, named business is a moderation surface this app has no queue for.
/// </summary>
public enum ReactionKind
{
    /// <summary>Funny.</summary>
    Laugh = 0,

    /// <summary>Genuinely bizarre.</summary>
    Mind = 1,

    /// <summary>Unappetising.</summary>
    Grim = 2,

    /// <summary>Affection for the place despite it all.</summary>
    Love = 3
}
