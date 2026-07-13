using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Core.Interfaces;
using PoSeeReview.Infrastructure.Configuration;

namespace PoSeeReview.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage service for uploading and managing comic images.
/// Images are stored in the 'comics' container. Access is provided via SAS URLs
/// since the storage account has public blob access disabled.
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _containerName;
    // 8 days > 7-day comic cache: SAS token always outlives the cached comic record
    private static readonly TimeSpan SasTokenDuration = TimeSpan.FromDays(8);

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<AzureStorageOptions> options,
        ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _containerName = options.Value.ComicsContainerName ?? "comics";
    }

    /// <summary>
    /// Uploads a comic image to Azure Blob Storage and returns the public URL.
    /// </summary>
    /// <param name="comicId">Unique identifier for the comic</param>
    /// <param name="imageBytes">PNG image bytes (1792x1024 recommended)</param>
    /// <returns>Public HTTPS URL to the uploaded blob</returns>
    /// <exception cref="ArgumentNullException">If comicId or imageBytes is null/empty</exception>
    /// <exception cref="RequestFailedException">If blob upload fails</exception>
    public async Task<string> UploadComicImageAsync(string comicId, byte[] imageBytes)
    {
        if (string.IsNullOrWhiteSpace(comicId))
            throw new ArgumentNullException(nameof(comicId));

        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentNullException(nameof(imageBytes));

        // Container creation is handled once at startup by TableStorageInitializer; the upload
        // hot path must not incur a blocking existence check on every request.
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

        // Blob name format: {comicId}.png
        var blobName = $"{comicId}.png";
        var blobClient = containerClient.GetBlobClient(blobName);

        // Upload with overwrite and PNG content type
        var blobHttpHeaders = new BlobHttpHeaders
        {
            ContentType = "image/png"
        };

        using var stream = new MemoryStream(imageBytes);
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders,
                Conditions = null // Allow overwrite
            }
        );

        // Return SAS URL for read access (valid for 25 hours, matching comic 24h expiry + buffer)
        return GenerateSasUrl(blobClient);
    }

    /// <summary>
    /// Deletes a comic image from Azure Blob Storage.
    /// </summary>
    /// <param name="comicId">Unique identifier for the comic to delete</param>
    /// <exception cref="ArgumentNullException">If comicId is null/empty</exception>
    public async Task DeleteComicImageAsync(string comicId)
    {
        if (string.IsNullOrWhiteSpace(comicId))
            throw new ArgumentNullException(nameof(comicId));

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        var blobName = $"{comicId}.png";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.DeleteIfExistsAsync();
    }

    /// <summary>
    /// Deletes a blob by its full URL
    /// </summary>
    /// <param name="blobUrl">Full URL of the blob to delete</param>
    public async Task DeleteBlobAsync(string blobUrl)
    {
        if (string.IsNullOrWhiteSpace(blobUrl))
            throw new ArgumentNullException(nameof(blobUrl));

        try
        {
            var uri = new Uri(blobUrl);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            // Extract blob name from URL path (skip container name)
            var pathParts = uri.AbsolutePath.TrimStart('/').Split('/');
            if (pathParts.Length >= 2)
            {
                var blobName = string.Join("/", pathParts.Skip(1));
                var blobClientFromUrl = containerClient.GetBlobClient(blobName);
                await blobClientFromUrl.DeleteIfExistsAsync();
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - deletion is best-effort for takedown
            _logger.LogWarning(ex, "Failed to delete blob for takedown: {BlobUrl}", blobUrl);
        }
    }

    /// <inheritdoc />
    public async Task<bool> BlobExistsAsync(string blobUrl)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)) return false;
        try
        {
            var uri = new Uri(blobUrl);
            var pathParts = uri.AbsolutePath.TrimStart('/').Split('/');
            var blobName = pathParts.Length >= 2
                ? string.Join("/", pathParts.Skip(1))
                : pathParts[0];

            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            return (await blobClient.ExistsAsync()).Value;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<string> RefreshSasUrlAsync(string existingBlobUrl)
    {
        if (string.IsNullOrWhiteSpace(existingBlobUrl))
            throw new ArgumentNullException(nameof(existingBlobUrl));

        // Strip query string to get the bare blob URL, then extract blob name from path
        var uri = new Uri(existingBlobUrl);
        var pathParts = uri.AbsolutePath.TrimStart('/').Split('/');
        // pathParts[0] = container name, rest = blob name (may include virtual dirs)
        var blobName = pathParts.Length >= 2
            ? string.Join("/", pathParts.Skip(1))
            : pathParts[0];

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        return Task.FromResult(GenerateSasUrl(blobClient));
    }

    /// <summary>
    /// Generates a SAS URL with read-only access for the specified blob.
    /// Falls back to the plain blob URI if SAS generation is not supported (e.g., Azurite without shared key).
    /// </summary>
    private string GenerateSasUrl(BlobClient blobClient)
    {
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(SasTokenDuration)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        // Fallback for Azurite or Managed Identity (no account key available)
        return blobClient.Uri.ToString();
    }
}
