namespace Po.SeeReview.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure Storage services
/// </summary>
public class AzureStorageOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// Connection string for Azure Table Storage
    /// </summary>
    public string AzureTableStorage { get; set; } = string.Empty;

    /// <summary>
    /// Connection string for Azure Blob Storage
    /// </summary>
    public string AzureBlobStorage { get; set; } = string.Empty;
}
