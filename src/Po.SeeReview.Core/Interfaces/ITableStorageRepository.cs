using Azure.Data.Tables;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Generic repository interface for Azure Table Storage operations
/// </summary>
/// <typeparam name="T">Entity type that implements ITableEntity</typeparam>
public interface ITableStorageRepository<T> where T : class, ITableEntity, new()
{
    /// <summary>
    /// Retrieves an entity by partition key and row key
    /// </summary>
    Task<T?> GetByIdAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries entities with optional filter
    /// </summary>
    Task<IEnumerable<T>> QueryAsync(string? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an entity
    /// </summary>
    Task<T> UpsertAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity by partition key and row key
    /// </summary>
    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an entity exists
    /// </summary>
    Task<bool> ExistsAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);
}
