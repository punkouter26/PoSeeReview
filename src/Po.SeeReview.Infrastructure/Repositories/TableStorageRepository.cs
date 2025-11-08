using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Infrastructure.Repositories;

/// <summary>
/// Base implementation of Table Storage repository
/// </summary>
/// <typeparam name="T">Entity type that implements ITableEntity</typeparam>
public class TableStorageRepository<T> : ITableStorageRepository<T> where T : class, ITableEntity, new()
{
    protected readonly TableClient _tableClient;
    protected readonly ILogger<TableStorageRepository<T>> _logger;

    public TableStorageRepository(
        TableServiceClient tableServiceClient,
        string tableName,
        ILogger<TableStorageRepository<T>> logger)
    {
        _tableClient = tableServiceClient.GetTableClient(tableName);
        _logger = logger;

        // Ensure table exists
        _tableClient.CreateIfNotExists();
    }

    public virtual async Task<T?> GetByIdAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey, cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Entity not found: PartitionKey={PartitionKey}, RowKey={RowKey}", partitionKey, rowKey);
            return null;
        }
    }

    public virtual async Task<IEnumerable<T>> QueryAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var entities = new List<T>();

        await foreach (var entity in _tableClient.QueryAsync<T>(filter, cancellationToken: cancellationToken))
        {
            entities.Add(entity);
        }

        return entities;
    }

    public virtual async Task<T> UpsertAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        _logger.LogInformation("Upserted entity: PartitionKey={PartitionKey}, RowKey={RowKey}",
            entity.PartitionKey, entity.RowKey);
        return entity;
    }

    public virtual async Task DeleteAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        await _tableClient.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: cancellationToken);
        _logger.LogInformation("Deleted entity: PartitionKey={PartitionKey}, RowKey={RowKey}", partitionKey, rowKey);
    }

    public virtual async Task<bool> ExistsAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(partitionKey, rowKey, cancellationToken);
        return entity != null;
    }
}
