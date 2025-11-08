namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for overlaying readable text onto comic images.
/// Addresses DALL-E's inability to generate readable text.
/// </summary>
public interface IComicTextOverlayService
{
    /// <summary>
    /// Adds text overlay to comic image based on narrative
    /// </summary>
    /// <param name="imageBytes">Original comic image</param>
    /// <param name="narrative">Story narrative to extract dialogue from</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>Modified image with text overlay</returns>
    Task<byte[]> AddTextOverlayAsync(byte[] imageBytes, string narrative, int panelCount);
}
