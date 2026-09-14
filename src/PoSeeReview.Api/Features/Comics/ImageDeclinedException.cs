namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Thrown when the image model refused to draw the comic, as opposed to failing to.
/// <para>
/// <b>Why this is a distinct type.</b> Until now every non-success from the image model was
/// indistinguishable, and a safety refusal was handled by retrying with a fixed, generic
/// "happy restaurant" prompt. That is the single worst outcome available: the safety filter
/// declined to depict what these reviews actually say, and the app answered by spending a
/// second paid call to publish a comic about <em>nothing</em>, presented under the reviews'
/// strangeness score with no signal anywhere that the subject had been swapped.
/// </para>
/// <para>
/// A refusal is information. It says the narrative the scorer produced touches something the
/// image model will not depict, which is exactly the kind of signal a moderator should see and
/// a user should be told about plainly.
/// </para>
/// </summary>
public sealed class ImageDeclinedException(string reason)
    : Exception("We couldn't draw this one. The story the reviews suggest isn't something our image model will illustrate.")
{
    /// <summary>
    /// Operator-facing reason from the provider (<c>SAFETY</c>, <c>PROHIBITED_CONTENT</c>, …).
    /// Never sent to a client: it describes what tripped the filter, which is a guide to
    /// rewording around it.
    /// </summary>
    public string Reason { get; } = reason;
}
