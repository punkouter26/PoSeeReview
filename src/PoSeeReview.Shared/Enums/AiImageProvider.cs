namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Selects the image backend for comic generation (NET_RULES 1.5 — enums over magic values).
/// Bound from <c>Ai:ImageProvider</c>. One member per implemented <c>IImageGenerationService</c>,
/// so no value can only throw at startup.
/// </summary>
public enum AiImageProvider
{
    /// <summary>Gemini image (generateContent). Default.</summary>
    Gemini = 0,

    /// <summary>gpt-image on the Azure AI Foundry resource the scorer already uses. Cheapest per strip.</summary>
    AzureOpenAI = 2
}
