using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Storage;

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
    // 8 days > 7-day comic cache: SAS token always outlives the cached comic record.
    // Used for account-key SAS (local/Azurite).
    private static readonly TimeSpan SasTokenDuration = TimeSpan.FromDays(8);
    // User Delegation SAS is capped by Azure at a 7-day key lifetime, so stay just under it.
    // Reads refresh the SAS on demand (IsSasExpiringSoon), so this comfortably covers a comic.
    private static readonly TimeSpan UserDelegationSasDuration = TimeSpan.FromDays(7) - TimeSpan.FromHours(1);

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

        // Return a read-only SAS URL so the browser can load the image from a private container.
        return await GenerateReadSasUrlAsync(blobClient);
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
        return GenerateReadSasUrlAsync(blobClient);
    }

    /// <summary>
    /// Generates a read-only SAS URL for the blob so a browser can load it from the private
    /// 'comics' container. Two signing paths:
    ///   1. Account-key credential (local / Azurite): the client signs the SAS itself.
    ///   2. Managed Identity / any token credential (production): there is no account key, so
    ///      sign with a User Delegation Key obtained via the token credential. This is the only
    ///      correct way to mint a SAS under Managed Identity — without it the caller previously
    ///      got a bare, unsigned URL that a private container rejects (broken images in prod).
    /// Only if both paths are unavailable does it fall back to the unsigned URI.
    /// </summary>
    private async Task<string> GenerateReadSasUrlAsync(BlobClient blobClient)
    {
        // Path 1 — account key present (local dev / Azurite): sign locally, keep the 8-day window.
        if (blobClient.CanGenerateSasUri)
        {
            var keyedSas = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(SasTokenDuration)
            };
            keyedSas.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(keyedSas).ToString();
        }

        // Path 2 — Managed Identity: sign with a User Delegation Key (capped under the 7-day limit).
        try
        {
            var startsOn = DateTimeOffset.UtcNow.AddMinutes(-15); // clock-skew buffer
            var expiresOn = DateTimeOffset.UtcNow.Add(UserDelegationSasDuration);

            var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
            {
                Sas = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, _blobServiceClient.AccountName)
            };
            return blobUriBuilder.ToUri().ToString();
        }
        catch (Exception ex)
        {
            // Last resort (e.g., the identity lacks the Storage Blob Data role that grants
            // generateUserDelegationKey). Return the bare URL and surface the cause loudly —
            // the image will fail to load, but the log points straight at the RBAC gap.
            _logger.LogError(ex,
                "Failed to generate User Delegation SAS for blob {BlobName}; returning unsigned URL. " +
                "Ensure the managed identity holds a Storage Blob Data role on the account.",
                blobClient.Name);
            return blobClient.Uri.ToString();
        }
    }
}
