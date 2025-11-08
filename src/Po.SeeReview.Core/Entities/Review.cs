namespace Po.SeeReview.Core.Entities;

/// <summary>
/// Represents a Google Maps review with strangeness analysis
/// </summary>
public class Review
{
    /// <summary>
    /// Review author display name
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Review text content
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Star rating (1-5)
    /// </summary>
    public int Rating { get; set; }

    /// <summary>
    /// Review timestamp
    /// </summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>
    /// AI-calculated strangeness score (0-100) from Azure OpenAI
    /// </summary>
    public double StrangenessScore { get; set; }
}
