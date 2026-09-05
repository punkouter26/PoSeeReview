using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// Persistence for <see cref="Comic"/> entities with 24-hour cache management. Declared in
/// Shared because the Takedowns slice erases comics without referencing the Comics slice
/// (NET_RULES 2.2).
/// </summary>
public interface IComicRepository
{
    /// <summary>
    /// Retrieves a comic by place identifier (returns even if expired — caller checks ExpiresAt).
    /// </summary>
    /// <param name="placeId">Google Maps place identifier</param>
    /// <returns>Comic if found, null otherwise</returns>
    Task<Comic?> GetByPlaceIdAsync(PlaceId placeId);

    /// <summary>
    /// Inserts or updates a comic in storage.
    /// </summary>
    /// <param name="comic">Comic entity to upsert</param>
    Task UpsertAsync(Comic comic);

    /// <summary>
    /// Deletes every comic for a place.
    /// </summary>
    /// <param name="placeId">Google Maps place identifier</param>
    Task DeleteAsync(PlaceId placeId);

    /// <summary>
    /// Retrieves a batch of expired comics that should be purged from storage.
    /// </summary>
    /// <param name="cutoff">Expiration threshold (usually DateTimeOffset.UtcNow)</param>
    /// <param name="maxResults">Maximum number of comics to return in one batch</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    Task<IReadOnlyList<Comic>> GetExpiredComicsAsync(DateTimeOffset cutoff, int maxResults, CancellationToken cancellationToken = default);
}
