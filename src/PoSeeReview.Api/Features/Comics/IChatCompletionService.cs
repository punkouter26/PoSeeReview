namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Service for analyzing restaurant reviews using Azure OpenAI <c>gpt-5.4-nano</c>
/// (the sole deployment in the shared AI Foundry resource, verified 2026-06-14).
/// Calculates strangeness scores and generates narrative summaries.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Analyzes a list of restaurant reviews to determine strangeness and create a narrative
    /// </summary>
    /// <param name="reviews">List of review texts to analyze</param>
    /// <param name="cancellationToken">Cancels the (potentially slow) model call when the caller abandons the request</param>
    /// <returns>Score, panel count, narrative, and the review fragments that drove the score.</returns>
    /// <exception cref="ArgumentException">If reviews list is empty</exception>
    Task<StrangenessAnalysis> AnalyzeStrangenessAsync(List<string> reviews, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates concise English captions for each panel of a comic strip.
    /// Falls back to sentence-splitting if the GPT call fails.
    /// </summary>
    /// <param name="narrative">Narrative paragraph from <see cref="AnalyzeStrangenessAsync"/></param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <param name="cancellationToken">Cancels the model call when the caller abandons the request</param>
    /// <returns>List of exactly <paramref name="panelCount"/> short captions</returns>
    Task<List<string>> GeneratePanelDialogueAsync(string narrative, int panelCount, CancellationToken cancellationToken = default);
}
