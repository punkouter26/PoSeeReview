using PoSeeReview.Shared.Contracts;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Result of one strangeness analysis call. Replaces the former
/// <c>(int, int, string)</c> tuple: adding receipts as a fourth element would have made an
/// already-positional signature unreadable at the call sites.
/// </summary>
/// <param name="StrangenessScore">0-100, clamped by the service.</param>
/// <param name="PanelCount">1-2, clamped by the service.</param>
/// <param name="Narrative">Short paragraph the image model draws from.</param>
/// <param name="Receipts">
/// Review fragments the model says drove the score. UNVERIFIED at this layer — the model is
/// free to paraphrase or invent, so <c>ComicGenerationService</c> checks each quote against the
/// reviews that were actually sent before any of it reaches a user.
/// </param>
public sealed record StrangenessAnalysis(
    int StrangenessScore,
    int PanelCount,
    string Narrative,
    IReadOnlyList<StrangenessReceipt> Receipts)
{
    /// <summary>Analyses from providers that do not return receipts.</summary>
    public StrangenessAnalysis(int strangenessScore, int panelCount, string narrative)
        : this(strangenessScore, panelCount, narrative, [])
    {
    }
}
