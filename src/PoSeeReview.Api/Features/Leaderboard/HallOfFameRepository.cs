using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Leaderboard;

/// <summary>
/// Persistence for the permanent weekly archive. Owned by the Leaderboard slice (NET_RULES 2.2).
/// </summary>
public sealed class HallOfFameRepository(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<HallOfFameRepository> logger) : IHallOfFameArchive
{
    private readonly TableClient _table =
        tableServiceClient.GetTableClient(options.Value.HallOfFameTableName);

    /// <summary>
    /// Files an entry under its week, keeping only the highest score that place reached during
    /// that week.
    /// <para>
    /// The RowKey embeds the score, so a better score is a different row: the old one has to be
    /// pruned or the same restaurant appears twice in one week. Written before the delete, so a
    /// crash between the two loses nothing.
    /// </para>
    /// </summary>
    public async Task ArchiveAsync(LeaderboardEntry entry, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var weekKey = HallOfFameEntity.WeekKeyFor(now);
        var partitionKey = HallOfFameEntity.PartitionKeyFor(entry.Region, weekKey);
        var placeKey = entry.PlaceId.Value;

        string? staleRowKey = null;
        var existingFilter = TableClient.CreateQueryFilter<HallOfFameEntity>(
            e => e.PartitionKey == partitionKey && e.PlaceId == placeKey);

        await foreach (var existing in _table.QueryAsync<HallOfFameEntity>(existingFilter, cancellationToken: cancellationToken))
        {
            if (existing.StrangenessScore >= entry.StrangenessScore)
            {
                // The week already holds a better or equal run for this place. The archive is a
                // record of peaks, so a later, weaker comic must not overwrite it.
                return;
            }

            staleRowKey = existing.RowKey;
            break;
        }

        var entity = HallOfFameEntity.FromEntry(entry, weekKey, now);
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        if (staleRowKey is not null && staleRowKey != entity.RowKey)
        {
            try
            {
                await _table.DeleteEntityAsync(partitionKey, staleRowKey, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already pruned.
            }
        }

        logger.LogInformation(
            "Archived {PlaceId} into the {WeekKey} hall of fame for {Region} at score {Score}",
            entry.PlaceId, weekKey, entry.Region, entry.StrangenessScore);
    }

    /// <summary>
    /// Reads one week of archived entries, highest score first.
    /// </summary>
    public async Task<List<HallOfFameEntity>> GetWeekAsync(
        RegionCode region,
        string weekKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = HallOfFameEntity.PartitionKeyFor(region, weekKey);
        var filter = TableClient.CreateQueryFilter<HallOfFameEntity>(e => e.PartitionKey == partitionKey);

        var entries = new List<HallOfFameEntity>();

        // The inverted RowKey means Table Storage already returns these in descending score
        // order, so this is a prefix read rather than a sort.
        await foreach (var entity in _table.QueryAsync<HallOfFameEntity>(filter, maxPerPage: limit, cancellationToken: cancellationToken))
        {
            entries.Add(entity);
            if (entries.Count >= limit)
            {
                break;
            }
        }

        return entries;
    }

    /// <summary>Deletes an archived entry across every week — used when a comic is taken down.</summary>
    public async Task DeleteAllForPlaceAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var placeKey = placeId.Value;
        var filter = TableClient.CreateQueryFilter<HallOfFameEntity>(e => e.PlaceId == placeKey);

        await foreach (var entity in _table.QueryAsync<HallOfFameEntity>(filter, cancellationToken: cancellationToken))
        {
            try
            {
                await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Concurrent takedown already removed it.
            }
        }

        logger.LogInformation("Purged hall of fame entries for {PlaceId}", placeId);
    }
}
