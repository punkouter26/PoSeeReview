using Azure.Data.Tables;
using Xunit;
using Xunit.Sdk;

namespace Po.SeeReview.IntegrationTests.TestFixtures;

/// <summary>
/// Test fixture for Azurite Azure Storage Emulator integration tests
/// </summary>
public class AzuriteFixture : IDisposable
{
    // Try multiple connection strings - first Aspire-managed with dynamic ports, then standard
    private static readonly string[] ConnectionStrings =
    [
        // Aspire-managed Azurite with dynamic ports (check docker ps for actual ports)
        GetAspireAzuriteConnectionString(),
        // Standard development storage
        "UseDevelopmentStorage=true",
        // Explicit localhost connection with standard ports
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"
    ];

    public TableServiceClient TableServiceClient { get; private set; } = null!;
    public bool IsAzuriteAvailable { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;

    private static string GetAspireAzuriteConnectionString()
    {
        // Try to get connection string from environment (set by Aspire AppHost)
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AzureTableStorage") 
            ?? Environment.GetEnvironmentVariable("AZURE_TABLE_STORAGE_CONNECTION_STRING");
        
        return connectionString ?? "UseDevelopmentStorage=true";
    }

    public AzuriteFixture()
    {
        // Try each connection string until one works
        foreach (var connStr in ConnectionStrings)
        {
            try
            {
                var client = new TableServiceClient(connStr);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                client.Query(cancellationToken: cts.Token).GetEnumerator().MoveNext();
                
                // Success - use this connection
                TableServiceClient = client;
                ConnectionString = connStr;
                IsAzuriteAvailable = true;
                break;
            }
            catch
            {
                // Try next connection string
            }
        }

        if (!IsAzuriteAvailable)
        {
            // Create a dummy client for test skipping purposes
            TableServiceClient = new TableServiceClient("UseDevelopmentStorage=true");
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
