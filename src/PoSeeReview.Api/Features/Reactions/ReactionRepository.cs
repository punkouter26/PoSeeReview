using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Reactions;

/// <summary>
/// Reads and writes comic reactions. Owned by the Reactions slice (NET_RULES 2.2).
/// </summary>
public sealed class ReactionRepository(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<ReactionRepository> logger)
{
    /// <summary>Attempts at the tally's optimistic-concurrency update before giving up on the increment.</summary>
    private const int MaxConcurrencyRetries = 5;

    private readonly TableClient _table =
        tableServiceClient.GetTableClient(options.Value.ReactionsTableName);

    /// <summary>Reads the tally for a comic together with the caller's own reaction.</summary>
    public async Task<ReactionCountsDto> GetAsync(
        PlaceId placeId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = ReactionKeys.PartitionKeyFor(placeId);

        var tally = await TryGetAsync<ReactionTallyEntity>(partitionKey, ReactionKeys.TallyRowKey, cancellationToken);
        var vote = await TryGetAsync<ReactionVoteEntity>(partitionKey, ReactionKeys.RowKeyFor(userId), cancellationToken);

        return Project(placeId, tally, vote?.ToKind());
    }

    /// <summary>
    /// Sets, changes or withdraws the caller's reaction and returns the updated tally.
    /// <para>
    /// Passing the reaction the caller already holds withdraws it, so the same button is a real
    /// toggle rather than a one-way commitment.
    /// </para>
    /// </summary>
    public async Task<ReactionCountsDto> SetAsync(
        PlaceId placeId,
        UserId userId,
        ReactionKind? requested,
        CancellationToken cancellationToken = default)
    {
        var partitionKey = ReactionKeys.PartitionKeyFor(placeId);
        var voteRowKey = ReactionKeys.RowKeyFor(userId);

        var existingVote = await TryGetAsync<ReactionVoteEntity>(partitionKey, voteRowKey, cancellationToken);
        var previous = existingVote?.ToKind();

        // Re-picking the current reaction means "undo", which is what a toggle should do.
        var next = requested is not null && requested == previous ? null : requested;

        if (next == previous)
        {
            var unchanged = await TryGetAsync<ReactionTallyEntity>(partitionKey, ReactionKeys.TallyRowKey, cancellationToken);
            return Project(placeId, unchanged, previous);
        }

        // The vote row is written first. If the tally update then loses every retry the counter
        // drifts by one, which is recoverable; the reverse order would let a viewer double-count
        // themselves by retrying, which is not.
        if (next is null)
        {
            await DeleteVoteAsync(partitionKey, voteRowKey, cancellationToken);
        }
        else
        {
            await _table.UpsertEntityAsync(new ReactionVoteEntity
            {
                PartitionKey = partitionKey,
                RowKey = voteRowKey,
                Reaction = next.Value.ToString()
            }, TableUpdateMode.Replace, cancellationToken);
        }

        var tally = await ApplyToTallyAsync(partitionKey, previous, next, cancellationToken);
        return Project(placeId, tally, next);
    }

    /// <summary>
    /// Moves one reaction off <paramref name="previous"/> and onto <paramref name="next"/> in a
    /// single ETag-guarded update, so a change of mind cannot briefly count as two reactions.
    /// </summary>
    private async Task<ReactionTallyEntity?> ApplyToTallyAsync(
        string partitionKey,
        ReactionKind? previous,
        ReactionKind? next,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            try
            {
                var tally = await TryGetAsync<ReactionTallyEntity>(partitionKey, ReactionKeys.TallyRowKey, cancellationToken);

                if (tally is null)
                {
                    var created = new ReactionTallyEntity
                    {
                        PartitionKey = partitionKey,
                        RowKey = ReactionKeys.TallyRowKey
                    };

                    if (next is not null)
                    {
                        created.Apply(next.Value, +1);
                    }

                    await _table.AddEntityAsync(created, cancellationToken);
                    return created;
                }

                if (previous is not null)
                {
                    tally.Apply(previous.Value, -1);
                }

                if (next is not null)
                {
                    tally.Apply(next.Value, +1);
                }

                await _table.UpdateEntityAsync(tally, tally.ETag, TableUpdateMode.Replace, cancellationToken);
                return tally;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Another voter got there first; re-read and reapply.
            }
        }

        logger.LogWarning(
            "Reaction tally {PartitionKey} stayed contended after {Attempts} attempts; the count may be one low",
            partitionKey, MaxConcurrencyRetries);

        return await TryGetAsync<ReactionTallyEntity>(partitionKey, ReactionKeys.TallyRowKey, cancellationToken);
    }

    private async Task DeleteVoteAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone: withdrawing a reaction nobody holds is a no-op, not an error.
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

    private static ReactionCountsDto Project(PlaceId placeId, ReactionTallyEntity? tally, ReactionKind? mine) => new()
    {
        PlaceId = placeId.Value,
        Laugh = tally?.Laugh ?? 0,
        Mind = tally?.Mind ?? 0,
        Grim = tally?.Grim ?? 0,
        Love = tally?.Love ?? 0,
        MyReaction = mine
    };
}
