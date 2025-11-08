using Azure.Data.Tables;
using Xunit;
using Xunit.Sdk;

namespace Po.SeeReview.IntegrationTests.TestFixtures;

/// <summary>
/// Test fixture for Azurite Azure Storage Emulator integration tests
/// </summary>
public class AzuriteFixture : IDisposable
{
    private const string EmulatorConnectionString = "UseDevelopmentStorage=true";

    public TableServiceClient TableServiceClient { get; }
    public bool IsAzuriteAvailable { get; private set; }
    public string ConnectionString => EmulatorConnectionString;

    public AzuriteFixture()
    {
        // Initialize Azure Table Storage client pointing to Azurite
    TableServiceClient = new TableServiceClient(EmulatorConnectionString);
        
        // Check if Azurite is available
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            TableServiceClient.Query(cancellationToken: cts.Token).GetEnumerator().MoveNext();
            IsAzuriteAvailable = true;
        }
        catch
        {
            IsAzuriteAvailable = false;
        }
    }

    /// <summary>
    /// Ensures Azurite is available
    /// </summary>
    public void EnsureAzuriteAvailable()
    {
        if (!IsAzuriteAvailable)
        {
            throw new XunitException("Azurite is not running. Start with: azurite --silent");
        }
    }

    /// <summary>
    /// Creates a test table and returns the client
    /// </summary>
    public async Task<TableClient> CreateTestTableAsync(string tableName)
    {
        EnsureAzuriteAvailable();
        var tableClient = TableServiceClient.GetTableClient(tableName);
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tableClient.CreateIfNotExistsAsync(cts.Token);
        return tableClient;
    }

    /// <summary>
    /// Deletes a test table for cleanup
    /// </summary>
    public async Task DeleteTestTableAsync(string tableName)
    {
        if (!IsAzuriteAvailable) return;
        
        var tableClient = TableServiceClient.GetTableClient(tableName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tableClient.DeleteAsync(cts.Token);
    }

    /// <summary>
    /// Clears all entities from a table
    /// </summary>
    public async Task ClearTableAsync(string tableName)
    {
        EnsureAzuriteAvailable();
        var tableClient = TableServiceClient.GetTableClient(tableName);

        await foreach (var entity in tableClient.QueryAsync<TableEntity>())
        {
            await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
    }
}
