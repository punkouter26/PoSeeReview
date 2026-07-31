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

        var comicsTableName = storageOptions.ComicsTableName ?? "PoSeeReviewComics";
        var leaderboardTableName = storageOptions.LeaderboardTableName ?? "PoSeeReviewLeaderboard";
        var restaurantsTableName = storageOptions.RestaurantsTableName ?? "PoSeeReviewRestaurants";
        var comicsContainerName = storageOptions.ComicsContainerName ?? "comics";

        foreach (var tableName in new[] { comicsTableName, leaderboardTableName, restaurantsTableName })
        {
            var tableClient = tableServiceClient.GetTableClient(tableName);
            await tableClient.CreateIfNotExistsAsync(cancellationToken);
            logger.LogInformation("Verified table storage table {TableName}", tableName);
        }

        var containerClient = blobServiceClient.GetBlobContainerClient(comicsContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        logger.LogInformation("Verified blob container {ContainerName}", comicsContainerName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
