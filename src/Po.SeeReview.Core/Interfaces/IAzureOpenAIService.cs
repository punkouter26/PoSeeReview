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
}
