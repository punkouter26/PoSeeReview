namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for generating comic strip images using an AI image model.
/// Currently implemented by GeminiComicService (Imagen 4).
/// </summary>
public interface IImageGenerationService
{
    /// <summary>
    /// Generates a comic strip image based on the narrative.
    /// </summary>
    /// <param name="narrative">Narrative describing the restaurant's strange aspects</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>PNG image bytes</returns>
    /// <exception cref="ArgumentException">If narrative is empty or panelCount is invalid</exception>
    /// <exception cref="HttpRequestException">If the image generation API call fails</exception>
    Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount);
}
