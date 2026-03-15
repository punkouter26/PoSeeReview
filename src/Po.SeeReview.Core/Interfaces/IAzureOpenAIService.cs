namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for analyzing restaurant reviews using Azure OpenAI GPT-4o-mini
/// Calculates strangeness scores and generates narrative summaries
/// </summary>
public interface IAzureOpenAIService
{
    /// <summary>
    /// Analyzes a list of restaurant reviews to determine strangeness and create a narrative
    /// </summary>
    /// <param name="reviews">List of review texts to analyze</param>
    /// <returns>Tuple of (strangeness score 0-100, panel count 1-4, narrative paragraph)</returns>
    /// <exception cref="ArgumentException">If reviews list is empty</exception>
    Task<(int StrangenessScore, int PanelCount, string Narrative)> AnalyzeStrangenessAsync(List<string> reviews);

    /// <summary>
    /// Generates concise English captions for each panel of a comic strip.
    /// Falls back to sentence-splitting if the GPT call fails.
    /// </summary>
    /// <param name="narrative">Narrative paragraph from <see cref="AnalyzeStrangenessAsync"/></param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>List of exactly <paramref name="panelCount"/> short captions</returns>
    Task<List<string>> GeneratePanelDialogueAsync(string narrative, int panelCount);
}
