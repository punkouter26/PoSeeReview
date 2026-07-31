namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// A Google Maps review with its AI strangeness analysis. Lives in Shared because both the
/// Restaurants and Comics slices consume it (NET_RULES 2.2 — slices must not reference
/// each other; shared models belong in PoSeeReview.Shared).
/// </summary>
public class Review
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
    /// AI-calculated strangeness score (0 = normal, 100 = very strange)
    /// </summary>
    public double StrangenessScore { get; set; }
}
