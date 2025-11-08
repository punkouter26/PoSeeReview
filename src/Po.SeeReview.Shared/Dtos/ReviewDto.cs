namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// DTO for Google Maps review with AI strangeness analysis
/// </summary>
public class ReviewDto
{
    /// <summary>
    /// Review author display name
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Review text content (minimum 5 words)
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
    /// AI-calculated strangeness score (0=normal, 100=very strange)
    /// </summary>
    public double StrangenessScore { get; set; }
}
