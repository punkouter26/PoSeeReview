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
    /// Three hex colours sampled from the finished artwork, so the client can tint itself to
    /// this comic. Empty for anything drawn before the extractor existed, and for any image it
    /// could not read — both render with the brand gradient, which is what every comic did
    /// before this field.
    /// </summary>
    public string[] Palette { get; set; } = [];

    /// <summary>
    /// The <c>Comics:PromptVersion</c> in force when this comic was drawn.
    /// <para>
    /// Part of the cache key, not just a record. The cache is keyed by place and age, so without
    /// a version a prompt tune kept serving the previous output until somebody paid for a forced
    /// regeneration — which made evaluating a prompt change cost one generation per restaurant
    /// tested, and made it easy to compare a new prompt against an old comic. A row whose version
    /// differs simply misses the cache. Zero means the row predates versioning, which also misses.
    /// </para>
    /// </summary>
    public int PromptVersion { get; set; }

    /// <summary>
    /// Vector of the narrative, used to find comics about the same kind of strangeness. Empty
    /// when embeddings are switched off, when the backend was unreachable, or on any row written
    /// before the feature existed — all three mean "not a candidate", which is what the empty
    /// array says. Never used for display.
    /// </summary>
    public float[] Embedding { get; set; } = [];
}
