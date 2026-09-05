using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Reports;

/// <summary>
/// Persistence for viewer reports. Owned by the Reports slice — no other slice reads them
/// (NET_RULES 2.2), so this stays out of Shared/Contracts.
/// </summary>
public sealed class ComicReportRepository(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<ComicReportRepository> logger)
{
    private readonly TableClient _table =
        tableServiceClient.GetTableClient(options.Value.ReportsTableName);

    /// <summary>
    /// Records a report, or reports that this principal had already flagged this comic.
    /// </summary>
    /// <returns>True when a new row was written; false when the reporter was already on record.</returns>
    public async Task<bool> TryAddAsync(ComicReportEntity report, CancellationToken cancellationToken = default)
    {
        try
        {
            // AddEntity rather than Upsert: the 409 IS the duplicate check. Reading first and
            // then writing would race two taps from the same person into two rows.
            await _table.AddEntityAsync(report, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            logger.LogInformation("Duplicate report ignored for {PlaceId}", report.PlaceId);
            return false;
        }
    }

    /// <summary>
    /// How many distinct people have reported this comic. Used by <c>/diag</c> so a comic
    /// accumulating reports is visible to whoever is on call, rather than sitting unread in a
    /// table nobody queries.
    /// </summary>
    public async Task<int> CountForPlaceAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var partitionKey = ComicReportEntity.PartitionKeyFor(placeId);
        var filter = TableClient.CreateQueryFilter<ComicReportEntity>(e => e.PartitionKey == partitionKey);

        var count = 0;
        await foreach (var _ in _table.QueryAsync<ComicReportEntity>(filter, cancellationToken: cancellationToken))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Reports received since <paramref name="since"/>, newest first, capped at
    /// <paramref name="limit"/>. A cross-partition scan, so it is an operator tool rather than
    /// anything on a user-facing path.
    /// </summary>
    public async Task<IReadOnlyList<ComicReportEntity>> GetRecentAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var filter = TableClient.CreateQueryFilter<ComicReportEntity>(e => e.ReportedAt >= since);

        var results = new List<ComicReportEntity>();
        await foreach (var entity in _table.QueryAsync<ComicReportEntity>(filter, cancellationToken: cancellationToken))
        {
            results.Add(entity);

            // Bounded even when the filter matches a lot: this runs on an operator page, not a
            // background job, and an unbounded scan there is a self-inflicted timeout.
            if (results.Count >= limit * 4)
            {
                break;
            }
        }

        return results
            .OrderByDescending(r => r.ReportedAt)
            .Take(limit)
            .ToList();
    }
}
