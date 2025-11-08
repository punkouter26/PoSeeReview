using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

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
    /// <returns>Comic entity with image URL, narrative, and strangeness score</returns>
    /// <exception cref="KeyNotFoundException">Restaurant not found</exception>
    /// <exception cref="InvalidOperationException">Insufficient reviews (need at least 5)</exception>
    Task<Comic> GenerateComicAsync(string placeId, bool forceRegenerate = false);

    /// <summary>
    /// Gets cached comic for a restaurant if it exists and hasn't expired
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <returns>Cached comic entity or null if not found or expired</returns>
    Task<Comic?> GetCachedComicAsync(string placeId);
}
