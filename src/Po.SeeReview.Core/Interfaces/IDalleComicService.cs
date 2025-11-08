namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for generating comic strip images using DALL-E 3
/// Creates 1-4 panel comic strips at 1792x1024 resolution
/// </summary>
public interface IDalleComicService
{
    /// <summary>
    /// Generates a comic strip image based on the narrative
    /// </summary>
    /// <param name="narrative">Narrative describing the restaurant's strange aspects</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>PNG image bytes (1792x1024)</returns>
    /// <exception cref="ArgumentException">If narrative is empty or panelCount is invalid</exception>
    /// <exception cref="HttpRequestException">If DALL-E API call fails</exception>
    Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount);
}
