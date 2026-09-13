using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;

namespace PoSeeReview.Api.Features.Insights;

/// <summary>
/// Cross-partition reads of the two tables the app already writes. Owned by the Insights slice
/// (NET_RULES 2.2) — it reads the tables by name from <see cref="AzureStorageOptions"/> rather
/// than through another slice's repository.
/// </summary>
public sealed class InsightsRepository(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> storageOptions,
    IOptions<InsightsOptions> insightsOptions,
    ILogger<InsightsRepository> logger)
{
    private readonly TableClient _hallOfFame =
        tableServiceClient.GetTableClient(storageOptions.Value.HallOfFameTableName);

    private readonly TableClient _restaurants =
        tableServiceClient.GetTableClient(storageOptions.Value.RestaurantsTableName);

    /// <summary>
    /// Reads every archived score and every cached restaurant, both capped at
    /// <see cref="InsightsOptions.MaxRowsScanned"/>.
    /// <para>
    /// The two scans are independent round trips and run concurrently — this endpoint's whole
    /// latency is the slower of the two, not their sum.
    /// </para>
    /// </summary>
    public async Task<InsightsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var cap = Math.Max(1, insightsOptions.Value.MaxRowsScanned);

        var scoresTask = ScanAsync<ArchivedScoreRow>(_hallOfFame, cap, cancellationToken);
        var restaurantsTask = ScanAsync<RestaurantFactsRow>(_restaurants, cap, cancellationToken);

        await Task.WhenAll(scoresTask, restaurantsTask);

        var (scores, scoresTruncated) = await scoresTask;
        var (restaurants, restaurantsTruncated) = await restaurantsTask;

        // Last row wins on a duplicate place id. Restaurant rows are keyed by region+place, so
        // the same place recorded under two region spellings would otherwise throw here.
        var byPlace = new Dictionary<string, RestaurantFactsRow>(StringComparer.Ordinal);
        foreach (var row in restaurants)
        {
            if (!string.IsNullOrWhiteSpace(row.PlaceId))
            {
                byPlace[row.PlaceId] = row;
            }
        }

        if (scoresTruncated || restaurantsTruncated)
        {
            logger.LogInformation(
                "Insights read hit the {Cap}-row cap (scores: {ScoresTruncated}, restaurants: {RestaurantsTruncated})",
                cap, scoresTruncated, restaurantsTruncated);
        }

        return new InsightsSnapshot(scores, byPlace, scoresTruncated || restaurantsTruncated);
    }

    private static async Task<(List<T> Rows, bool Truncated)> ScanAsync<T>(
        TableClient table,
        int cap,
        CancellationToken cancellationToken)
        where T : class, ITableEntity
    {
        var rows = new List<T>();

        await foreach (var row in table.QueryAsync<T>(cancellationToken: cancellationToken))
        {
            rows.Add(row);

            if (rows.Count >= cap)
            {
                return (rows, true);
            }
        }

        return (rows, false);
    }
}
