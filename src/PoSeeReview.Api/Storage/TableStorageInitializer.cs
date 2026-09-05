using Azure.Data.Tables;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Storage;

/// <summary>
/// Startup initializer that provisions the Table Storage tables and Blob container once at
/// application start, replacing the per-request <c>CreateIfNotExists</c> calls that previously
/// ran inside scoped repository constructors and on every blob upload. Exceptions propagate so
/// the host fails fast (§5.6) when storage is misconfigured or unreachable.
/// </summary>
internal sealed class TableStorageInitializer(
    TableServiceClient tableServiceClient,
    BlobServiceClient blobServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<TableStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var storageOptions = options.Value;

        string[] tableNames =
        [
            storageOptions.ComicsTableName,
            storageOptions.LeaderboardTableName,
            storageOptions.RestaurantsTableName,
            storageOptions.ReportsTableName,
            storageOptions.ReactionsTableName,
            storageOptions.HallOfFameTableName,
            storageOptions.BudgetTableName,
            storageOptions.AnalyticsTableName
        ];

        // Created concurrently: these are independent round trips, and running eight of them in
        // series put the whole set on the startup critical path. A failure in any one still
        // propagates and fails the host fast, which is the point of doing this at startup.
        await Task.WhenAll(tableNames.Select(async tableName =>
        {
            var tableClient = tableServiceClient.GetTableClient(tableName);
            await tableClient.CreateIfNotExistsAsync(cancellationToken);
            logger.LogInformation("Verified table storage table {TableName}", tableName);
        }));

        var containerClient = blobServiceClient.GetBlobContainerClient(storageOptions.ComicsContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        logger.LogInformation("Verified blob container {ContainerName}", storageOptions.ComicsContainerName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
