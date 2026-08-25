using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Service for generating comic strips from restaurant reviews
/// Orchestrates review analysis, narrative generation, and image creation
/// </summary>
public interface IComicGenerationService
{
    /// <summary>
    /// Generates a comic strip for a restaurant based on its reviews
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="forceRegenerate">If true, bypasses cache and generates new comic</param>
    /// <param name="progress">
    /// Optional sink for the pipeline stage currently running, so a caller can narrate a real
    /// 10-second wait. Reports are best-effort and fire-and-forget: a slow or throwing observer
    /// must not stall or fail generation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Comic entity with image URL, narrative, and strangeness score</returns>
    /// <exception cref="KeyNotFoundException">Restaurant not found</exception>
    /// <exception cref="InsufficientReviewsException">Insufficient reviews (need at least 5)</exception>
    Task<Comic> GenerateComicAsync(
        PlaceId placeId,
        bool forceRegenerate = false,
        IProgress<ComicGenerationPhase>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached comic for a restaurant if it exists and hasn't expired
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Cached comic entity or null if not found or expired</returns>
    Task<Comic?> GetCachedComicAsync(PlaceId placeId, CancellationToken cancellationToken = default);
}
