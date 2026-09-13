using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Collections;

/// <summary>
/// Persistence for kept comics. Owned by the Collections slice; the delete-by-place half is also
/// exposed through <see cref="IKeptComicArchive"/> so Takedowns can reach it (NET_RULES 2.2).
/// </summary>
public sealed class KeptComicRepository(
    TableServiceClient tableServiceClient,
    KeptComicBlobStore blobStore,
    IOptions<AzureStorageOptions> options,
    ILogger<KeptComicRepository> logger) : IKeptComicArchive
{
    private readonly TableClient _table =
        tableServiceClient.GetTableClient(options.Value.CollectionsTableName);

    /// <summary>One person's collection, newest first.</summary>
    public async Task<List<KeptComicEntity>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var partitionKey = KeptComicKeys.UserPartitionFor(userId);
        var filter = TableClient.CreateQueryFilter<KeptComicEntity>(e => e.PartitionKey == partitionKey);

        var rows = new List<KeptComicEntity>();
        await foreach (var row in _table.QueryAsync<KeptComicEntity>(filter, cancellationToken: cancellationToken))
        {
            rows.Add(row);
        }

        return rows.OrderByDescending(r => r.KeptAt).ToList();
    }

    /// <summary>One entry, or null. Used by the image endpoint to prove ownership before streaming.</summary>
    public Task<KeptComicEntity?> GetAsync(UserId userId, PlaceId placeId, CancellationToken cancellationToken = default) =>
        TryGetAsync<KeptComicEntity>(
            KeptComicKeys.UserPartitionFor(userId),
            KeptComicKeys.PlaceRowKeyFor(placeId),
            cancellationToken);

    /// <summary>
    /// Adds a comic to a collection, copying its artwork into the kept container.
    /// <para>
    /// Re-keeping something already kept is a no-op that returns the existing row, so a
    /// double-tap cannot produce a second copy or consume a second slot.
    /// </para>
    /// </summary>
    public async Task<KeptComicEntity> KeepAsync(
        UserId userId,
        Comic comic,
        Stream? artwork,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, comic.PlaceId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var blobName = KeptComicKeys.BlobNameFor(userId, comic.PlaceId);
        var stored = false;

        if (artwork is not null)
        {
            stored = await blobStore.SaveAsync(blobName, artwork, cancellationToken);
        }

        var entity = new KeptComicEntity
        {
            PartitionKey = KeptComicKeys.UserPartitionFor(userId),
            RowKey = KeptComicKeys.PlaceRowKeyFor(comic.PlaceId),
            PlaceId = comic.PlaceId.Value,
            RestaurantName = comic.RestaurantName,
            StrangenessScore = comic.StrangenessScore,
            BlobName = stored ? blobName : string.Empty,
            KeptAt = now
        };

        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        // The reverse index is written second and is the only row a takedown reads. If this
        // write fails the user still has their copy and the takedown path would miss it, so the
        // failure propagates rather than being swallowed — an unreachable kept copy is exactly
        // the thing moderation exists to prevent.
        await _table.UpsertEntityAsync(new KeptComicByPlaceEntity
        {
            PartitionKey = KeptComicKeys.PlacePartitionFor(comic.PlaceId),
            RowKey = KeptComicKeys.UserRowKeyFor(userId),
            PlaceId = comic.PlaceId.Value,
            UserPartitionKey = entity.PartitionKey,
            BlobName = entity.BlobName
        }, TableUpdateMode.Replace, cancellationToken);

        logger.LogInformation("Kept comic {PlaceId} for a user (image stored: {Stored})", comic.PlaceId, stored);
        return entity;
    }

    /// <summary>Removes one comic from one person's collection, blob included.</summary>
    public async Task<bool> ForgetAsync(UserId userId, PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, placeId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await blobStore.DeleteAsync(existing.BlobName, cancellationToken);
        await DeleteRowAsync(existing.PartitionKey, existing.RowKey, cancellationToken);
        await DeleteRowAsync(
            KeptComicKeys.PlacePartitionFor(placeId),
            KeptComicKeys.UserRowKeyFor(userId),
            cancellationToken);

        return true;
    }

    /// <summary>How many comics this person is currently keeping.</summary>
    public async Task<int> CountForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        (await GetForUserAsync(userId, cancellationToken)).Count;

    /// <inheritdoc />
    public async Task DeleteAllForPlaceAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var partitionKey = KeptComicKeys.PlacePartitionFor(placeId);
        var filter = TableClient.CreateQueryFilter<KeptComicByPlaceEntity>(e => e.PartitionKey == partitionKey);

        var removed = 0;

        await foreach (var index in _table.QueryAsync<KeptComicByPlaceEntity>(filter, cancellationToken: cancellationToken))
        {
            await blobStore.DeleteAsync(index.BlobName, cancellationToken);
            await DeleteRowAsync(index.UserPartitionKey, KeptComicKeys.PlaceRowKeyFor(placeId), cancellationToken);
            await DeleteRowAsync(index.PartitionKey, index.RowKey, cancellationToken);
            removed++;
        }

        if (removed > 0)
        {
            logger.LogInformation("Purged {Count} kept copies of {PlaceId}", removed, placeId);
        }
    }

    private async Task DeleteRowAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone. Deleting something that is not there is the desired end state.
        }
    }

    private async Task<T?> TryGetAsync<T>(string partitionKey, string rowKey, CancellationToken cancellationToken)
        where T : class, ITableEntity
    {
        try
        {
            return await _table.GetEntityAsync<T>(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
