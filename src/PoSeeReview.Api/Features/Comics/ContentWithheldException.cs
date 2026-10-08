namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Moderation withheld the place while its comic was being drawn, so nothing was published.
/// Surfaces as the same 451 a caller gets when the place is already withheld at request time.
/// </summary>
public sealed class ContentWithheldException(bool isSuppressed)
    : Exception("The place was withheld by moderation during generation.")
{
    public bool IsSuppressed { get; } = isSuppressed;
}
