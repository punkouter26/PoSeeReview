using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// A generated comic strip derived from a restaurant's reviews. Consumed by the Comics and
/// Takedowns slices, so it lives in Shared (NET_RULES 2.2).
/// </summary>
public class Comic
{
    /// <summary>
    /// Unique identifier for the comic
    /// </summary>
    public ComicId Id { get; set; } = ComicId.Empty;

    /// <summary>
    /// Google Maps place identifier of the restaurant
    /// </summary>
    public PlaceId PlaceId { get; set; } = PlaceId.Empty;

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
    /// Whether this comic was generated fresh, served from cache, or regenerated after expiry.
    /// </summary>
    public ComicCacheState CacheState { get; set; } = ComicCacheState.Generated;

    /// <summary>
    /// Timestamp when the comic was generated
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Originating principal, so dev-bypass users can still be traced in storage.
    /// </summary>
    public UserId RequestedByUserId { get; set; } = UserId.Anonymous;

    /// <summary>
    /// Verbatim review fragments the analyser weighted, with the points each contributed.
    /// Empty for comics generated before receipts existed, and for any run where the model
    /// returned quotes that could not be matched back to the source reviews.
    /// </summary>
    public IReadOnlyList<StrangenessReceipt> Receipts { get; set; } = [];
}
