namespace Po.SeeReview.Core.Entities;

/// <summary>
/// Represents a generated comic strip based on restaurant reviews
/// </summary>
public class Comic
{
    /// <summary>
    /// Unique identifier for the comic
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Google Maps Place ID of the restaurant
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the restaurant
    /// </summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>
    /// URL to the comic image in Azure Blob Storage
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Narrative paragraph describing the strange aspects of the restaurant
    /// </summary>
    public string Narrative { get; set; } = string.Empty;

    /// <summary>
    /// Strangeness score from 0-100 (0 = normal, 100 = extremely bizarre)
    /// </summary>
    public int StrangenessScore { get; set; }

    /// <summary>
    /// Cache expiration timestamp (24 hours from creation)
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Indicates if this comic was served from cache
    /// Used for client transparency
    /// </summary>
    public bool IsCached { get; set; }

    /// <summary>
    /// Timestamp when the comic was generated
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
