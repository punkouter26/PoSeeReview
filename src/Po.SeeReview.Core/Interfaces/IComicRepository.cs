using System.Collections.Generic;
using System.Threading;
using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Repository for Comic entities with 24-hour cache management
/// </summary>
public interface IComicRepository
{
    /// <summary>
    /// Retrieves a comic by Place ID (returns even if expired - caller checks ExpiresAt)
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <returns>Comic if found, null otherwise</returns>
    Task<Comic?> GetByPlaceIdAsync(string placeId);

    /// <summary>
    /// Inserts or updates a comic in storage
    /// </summary>
    /// <param name="comic">Comic entity to upsert</param>
    Task UpsertAsync(Comic comic);

    /// <summary>
    /// Deletes a comic by Place ID
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    Task DeleteAsync(string placeId);

    /// <summary>
    /// Deletes a specific comic by Place ID and generation timestamp
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="generatedAt">Generation timestamp used as RowKey</param>
    Task DeleteAsync(string placeId, DateTimeOffset generatedAt);

    /// <summary>
    /// Retrieves all comics for a specific place (for takedown requests)
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <returns>List of all comics for this place</returns>
    Task<IReadOnlyList<Comic>> GetComicsByPlaceIdAsync(string placeId);

    /// <summary>
    /// Retrieves a batch of expired comics that should be purged from storage
    /// </summary>
    /// <param name="cutoff">Expiration threshold (usually DateTimeOffset.UtcNow)</param>
    /// <param name="maxResults">Maximum number of comics to return in one batch</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    Task<IReadOnlyList<Comic>> GetExpiredComicsAsync(DateTimeOffset cutoff, int maxResults, CancellationToken cancellationToken = default);
}
