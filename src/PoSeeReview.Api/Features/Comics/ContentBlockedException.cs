namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Thrown when the pre-publish content screen refuses a generated narrative.
/// <para>
/// Raised before the image model is called, so nothing was spent and the caller's budget unit is
/// refundable — the same treatment the strangeness floor gets, and for the same reason.
/// </para>
/// <para>
/// The message is deliberately vague about what tripped the screen. Naming the category to an
/// end user is a guide to rewording around it, and the screen's job is not to teach evasion.
/// </para>
/// </summary>
public sealed class ContentBlockedException(string category)
    : Exception("The story drawn from these reviews could not be published.")
{
    /// <summary>Operator-facing label for telemetry and logs. Never sent to a client.</summary>
    public string Category { get; } = category;
}
