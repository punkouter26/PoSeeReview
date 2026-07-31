using System.Collections.Generic;
using System.Threading;
using Azure.Data.Tables;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Repository for managing comic entities in Azure Table Storage with 24-hour cache TTL.
/// </summary>
public class ComicRepository : IComicRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<ComicRepository> _logger;

    public ComicRepository(
        TableServiceClient tableServiceClient,
        IOptions<AzureStorageOptions> options,
        ILogger<ComicRepository> logger)
    {
        var tableName = options.Value.ComicsTableName ?? "PoSeeReviewComics";
        _tableClient = tableServiceClient.GetTableClient(tableName);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a comic by place ID. Returns expired comics (caller must check ExpiresAt).
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Comic entity if found, null otherwise</returns>
    public async Task<Comic?> GetByPlaceIdAsync(PlaceId placeId)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        try
        {
            var response = await _tableClient.GetEntityAsync<ComicEntity>(
                partitionKey: ComicEntity.PartitionKeyValue,
                rowKey: placeId.Value
            );

            return response.Value.ToDomain();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Inserts or updates a comic entity. Sets 24-hour expiration from current time.
    /// </summary>
    /// <param name="comic">Comic entity to persist</param>
    /// <exception cref="ArgumentNullException">If comic or required fields are null</exception>
    public async Task UpsertAsync(Comic comic)
    {
        if (comic == null)
            throw new ArgumentNullException(nameof(comic));

        if (comic.PlaceId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(comic));

        var entity = ComicEntity.FromDomain(comic);
        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Deletes a comic by place ID.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    public async Task DeleteAsync(PlaceId placeId)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        try
        {
            await _tableClient.DeleteEntityAsync(
                partitionKey: ComicEntity.PartitionKeyValue,
                rowKey: placeId.Value
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted, ignore
        }
    }

    /// <summary>
    /// Deletes a specific comic by Place ID and generation timestamp
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <param name="generatedAt">Generation timestamp used as RowKey</param>
    public async Task DeleteAsync(PlaceId placeId, DateTimeOffset generatedAt)
    {
        if (placeId.IsEmpty)
        {
            throw new ArgumentException("PlaceId cannot be empty", nameof(placeId));
        }

        try
        {
            // Comics are stored with PartitionKey=ComicEntity.PartitionKeyValue and RowKey=placeId
            // (see ComicEntity.FromDomain). The generatedAt timestamp is not part of the key.
            await _tableClient.DeleteEntityAsync(
                partitionKey: ComicEntity.PartitionKeyValue,
                rowKey: placeId.Value);
            _logger.LogInformation(
                "Deleted comic for PlaceId {PlaceId} generated at {GeneratedAt}",
                placeId,
                generatedAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(
                ex,
                "Comic not found for deletion: PlaceId {PlaceId}, GeneratedAt {GeneratedAt}",
                placeId,
                generatedAt);
        }
    }

    /// <summary>
    /// Retrieves all comics for a specific place (for takedown requests)
    /// </summary>
    /// <param name="placeId">Google Maps Place ID</param>
    /// <returns>List of all comics for this place</returns>
    public async Task<IReadOnlyList<Comic>> GetComicsByPlaceIdAsync(PlaceId placeId)
    {
        if (placeId.IsEmpty)
        {
            throw new ArgumentException("PlaceId cannot be empty", nameof(placeId));
        }

        var rowKey = placeId.Value;

        var comics = new List<Comic>();

        // Comics are stored with PartitionKey=ComicEntity.PartitionKeyValue and RowKey=placeId
        // (see ComicEntity.FromDomain); there is at most one comic per place.
        var filter = TableClient.CreateQueryFilter<ComicEntity>(entity =>
            entity.PartitionKey == ComicEntity.PartitionKeyValue && entity.RowKey == rowKey);

        var query = _tableClient.QueryAsync<ComicEntity>(filter: filter);

        await foreach (var entity in query)
        {
            comics.Add(entity.ToDomain());
        }

        _logger.LogInformation(
            "Retrieved {Count} comic(s) for PlaceId {PlaceId}",
            comics.Count,
            placeId);

        return comics;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Comic>> GetExpiredComicsAsync(
        DateTimeOffset cutoff,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults must be greater than zero");
        }

        var expiredComics = new List<Comic>(capacity: Math.Min(maxResults, 100));

        var filter = TableClient.CreateQueryFilter<ComicEntity>(entity =>
            entity.PartitionKey == ComicEntity.PartitionKeyValue && entity.ExpiresAt < cutoff);

        var query = _tableClient.QueryAsync<ComicEntity>(
            filter: filter,
            maxPerPage: Math.Min(maxResults, 100),
            cancellationToken: cancellationToken);

        await foreach (var entity in query.WithCancellation(cancellationToken))
        {
            expiredComics.Add(entity.ToDomain());

            if (expiredComics.Count >= maxResults)
            {
                break;
            }
        }

        return expiredComics;
    }
}
