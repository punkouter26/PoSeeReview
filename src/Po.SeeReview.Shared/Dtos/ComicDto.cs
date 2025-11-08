namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// Data transfer object for comic strip with strangeness score
/// </summary>
public class ComicDto
{
    /// <summary>
    /// Unique identifier for the comic
    /// </summary>
    public string ComicId { get; set; } = string.Empty;

    /// <summary>
    /// Google Maps Place ID of the restaurant
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the restaurant
    /// </summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// Narrative paragraph describing the strange aspects
    /// </summary>
    public string Narrative { get; set; } = string.Empty;

    /// <summary>
    /// Strangeness score from 0-100 (0 = normal, 100 = extremely bizarre)
    /// </summary>
    public int StrangenessScore { get; set; }

    /// <summary>
    /// HTTPS URL to comic PNG in Azure Blob Storage
    /// </summary>
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the comic was generated
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>
    /// Cache expiration timestamp (generatedAt + 24 hours)
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// True if returned from cache, false if newly generated
    /// </summary>
    public bool IsCached { get; set; }
}
