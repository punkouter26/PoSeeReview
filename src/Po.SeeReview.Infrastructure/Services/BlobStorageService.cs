using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Configuration;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage service for uploading and managing comic images.
/// Images are stored in the 'comics' container with public read access.
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<AzureStorageOptions> options)
    {
        _blobServiceClient = blobServiceClient;
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

        // Ensure container exists with public blob access
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

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

        // Return public URL
        return blobClient.Uri.ToString();
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
        catch (Exception)
        {
            // Log but don't throw - deletion is best-effort for takedown
            // Actual logging would be done through ILogger if injected
        }
    }
}
