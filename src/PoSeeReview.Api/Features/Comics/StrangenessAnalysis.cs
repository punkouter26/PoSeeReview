namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Result of one strangeness analysis call. A record rather than a
/// <c>(int, int, string)</c> tuple: the three values are all unlabelled scalars at the call
/// sites, and a positional tuple made them trivially easy to transpose.
/// </summary>
/// <param name="StrangenessScore">0-100, clamped by the service.</param>
/// <param name="PanelCount">1-2, clamped by the service.</param>
/// <param name="Narrative">Short paragraph the image model draws from.</param>
public sealed record StrangenessAnalysis(
    int StrangenessScore,
    int PanelCount,
    string Narrative);
