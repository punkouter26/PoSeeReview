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
}
