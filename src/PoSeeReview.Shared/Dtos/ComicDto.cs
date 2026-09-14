namespace PoSeeReview.Shared.Dtos;

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

    /// <summary>
    /// Three hex colours sampled from the artwork, so the page can tint itself to this comic.
    /// <para>
    /// Extracted on the server, because the blob is served without CORS headers and a browser
    /// canvas that has drawn it cannot be read back. Empty for comics drawn before the extractor
    /// existed and for any image it could not decode; both cases render the brand gradient,
    /// which is what every comic did before this field.
    /// </para>
    /// </summary>
    public string[] Palette { get; set; } = [];
}
