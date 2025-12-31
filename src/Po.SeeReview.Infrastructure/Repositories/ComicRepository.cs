using System.Collections.Generic;
using System.Threading;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Configuration;
using Po.SeeReview.Infrastructure.Entities;

namespace Po.SeeReview.Infrastructure.Repositories;

/// <summary>
/// Repository for managing comic entities in Azure Table Storage with 24-hour cache TTL.
/// </summary>
public class ComicRepository : IComicRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<ComicRepository> _logger;
    private const string PartitionKeyPrefix = "COMIC";

    public ComicRepository(
        TableServiceClient tableServiceClient,
        IOptions<AzureStorageOptions> options,
        ILogger<ComicRepository> logger)
    {
        var tableName = options.Value.ComicsTableName ?? "PoSeeReviewComics";
        _tableClient = tableServiceClient.GetTableClient(tableName);
        _tableClient.CreateIfNotExists();

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a comic by place ID. Returns expired comics (caller must check ExpiresAt).
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>Comic entity if found, null otherwise</returns>
    public async Task<Comic?> GetByPlaceIdAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentNullException(nameof(placeId));

        try
        {
            var response = await _tableClient.GetEntityAsync<ComicEntity>(
                partitionKey: PartitionKeyPrefix,
                rowKey: placeId
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

        if (string.IsNullOrWhiteSpace(comic.PlaceId))
            throw new ArgumentException("PlaceId is required", nameof(comic));

        var entity = ComicEntity.FromDomain(comic);
        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Deletes a comic by place ID.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    public async Task DeleteAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentNullException(nameof(placeId));

        try
        {
            await _tableClient.DeleteEntityAsync(
                partitionKey: PartitionKeyPrefix,
                rowKey: placeId
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
    public async Task DeleteAsync(string placeId, DateTimeOffset generatedAt)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentException("PlaceId cannot be null or empty", nameof(placeId));
        }

        var partitionKey = $"{PartitionKeyPrefix}_{placeId}";
        var rowKey = generatedAt.ToString("yyyyMMddHHmmss");

        try
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
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
    public async Task<IReadOnlyList<Comic>> GetComicsByPlaceIdAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentException("PlaceId cannot be null or empty", nameof(placeId));
        }

        var partitionKey = $"{PartitionKeyPrefix}_{placeId}";
        var comics = new List<Comic>();

        var filter = TableClient.CreateQueryFilter<ComicEntity>(entity =>
            entity.PartitionKey == partitionKey);

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
            entity.PartitionKey == PartitionKeyPrefix && entity.ExpiresAt < cutoff);

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
