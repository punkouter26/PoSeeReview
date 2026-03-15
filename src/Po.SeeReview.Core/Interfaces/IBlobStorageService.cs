namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for uploading and managing comic images in Azure Blob Storage
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a comic image to Azure Blob Storage
    /// </summary>
    /// <param name="comicId">Unique comic identifier</param>
    /// <param name="imageBytes">PNG image bytes</param>
    /// <returns>Public URL of the uploaded image</returns>
    /// <exception cref="ArgumentException">If comicId is empty or imageBytes is null/empty</exception>
    Task<string> UploadComicImageAsync(string comicId, byte[] imageBytes);

    /// <summary>
    /// Deletes a comic image from Azure Blob Storage if it exists
    /// </summary>
    /// <param name="comicId">Unique comic identifier whose blob should be removed</param>
    Task DeleteComicImageAsync(string comicId);

    /// <summary>
    /// Deletes a blob by its URL
    /// </summary>
    /// <param name="blobUrl">Full URL of the blob to delete</param>
    Task DeleteBlobAsync(string blobUrl);

    /// <summary>
    /// Generates a fresh SAS URL for an existing blob whose token may have expired.
    /// </summary>
    /// <param name="existingBlobUrl">Current (potentially expired) SAS URL</param>
    /// <returns>New SAS URL valid for the full SAS token duration</returns>
    Task<string> RefreshSasUrlAsync(string existingBlobUrl);

    /// <summary>
    /// Checks whether a blob physically exists in storage.
    /// Used to detect leaderboard entries whose blobs were purged by the cleanup service.
    /// </summary>
    /// <param name="blobUrl">Full URL (with or without SAS) of the blob to check</param>
    Task<bool> BlobExistsAsync(string blobUrl);
}
