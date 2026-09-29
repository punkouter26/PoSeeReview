namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Service for generating comic strip images using an AI image model: Gemini, FLUX via
/// HuggingFace, or gpt-image via Azure, selected by <c>Ai:ImageProvider</c>.
/// </summary>
public interface IImageGenerationService
{
    /// <summary>
    /// Generates a comic strip image based on the narrative.
    /// </summary>
    /// <param name="narrative">Narrative describing the restaurant's strange aspects</param>
    /// <param name="panelScenes">One illustrator brief per panel (1-4); the count is the panel count</param>
    /// <param name="cancellationToken">Cancels the (potentially slow) image generation call</param>
    /// <returns>Encoded image bytes (PNG, JPEG or WebP, whatever the provider returned)</returns>
    /// <exception cref="ArgumentException">If narrative is empty or the panel count is invalid</exception>
    /// <exception cref="HttpRequestException">If the image generation API call fails</exception>
    Task<byte[]> GenerateComicImageAsync(string narrative, IReadOnlyList<string> panelScenes, CancellationToken cancellationToken = default);
}
