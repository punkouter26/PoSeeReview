using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;

namespace PoSeeReview.Api.Features.Collections;

/// <summary>
/// The kept-comics blob container.
/// <para>
/// A separate container from <c>comics</c>, and that separation is the feature. Everything in
/// <c>comics</c> is on a 24-hour clock that <c>ExpiredComicCleanupService</c> enforces by
/// deleting blobs; a kept copy has to survive exactly that. Sharing the container and marking
/// individual blobs as exempt would mean the cleanup service had to understand collections,
/// which is a coupling it should not have.
/// </para>
/// <para>
/// Never served with a SAS. Kept copies are private to one person and are streamed back through
/// an authenticated endpoint, so there is no signed URL to leak or to expire.
/// </para>
/// </summary>
public sealed class KeptComicBlobStore(
    BlobServiceClient blobServiceClient,
    IOptions<AzureStorageOptions> options,
    ILogger<KeptComicBlobStore> logger)
{
    private readonly BlobContainerClient _container =
        blobServiceClient.GetBlobContainerClient(options.Value.KeptComicsContainerName);

    /// <summary>
    /// Copies a comic's bytes into the kept container. Returns false when the write failed —
    /// the caller still records the row, because a bookmark with no picture is more useful than
    /// silently doing nothing.
    /// </summary>
    public async Task<bool> SaveAsync(string blobName, Stream source, CancellationToken cancellationToken = default)
    {
        try
        {
            var blob = _container.GetBlobClient(blobName);

            await blob.UploadAsync(source, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" }
            }, cancellationToken);

            return true;
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex, "Could not store the kept copy at {BlobName}", blobName);
            return false;
        }
    }

    /// <summary>Opens a kept copy for streaming, or null when it is not there.</summary>
    public async Task<Stream?> OpenAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return null;
        }

        try
        {
            return await _container.GetBlobClient(blobName).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Deletes a kept copy. Best effort — a missing blob is the desired end state anyway.</summary>
    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            // Logged loudly rather than swallowed: this path is also how a takedown erases a
            // kept copy, and a takedown that quietly left bytes behind is the failure that
            // matters most here.
            logger.LogError(ex, "Failed to delete the kept copy at {BlobName}", blobName);
        }
    }
}
