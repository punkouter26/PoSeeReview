namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Selects the image backend for comic generation (NET_RULES 1.5 — enums over magic values).
/// Bound from <c>Ai:ImageProvider</c>; replaces the former <c>UseHuggingFace</c> boolean.
///
/// One member per implemented <c>IImageGenerationService</c>, so no value can only throw at
/// startup. When <c>Ai:ChatProvider</c> is absent this also derives the chat provider:
/// HuggingFace pairs with HuggingFace chat (shared token), everything else with Azure.
/// </summary>
public enum AiImageProvider
{
    /// <summary>Gemini image (generateContent). Default.</summary>
    Gemini = 0,

    /// <summary>HuggingFace router: FLUX.1-schnell.</summary>
    HuggingFace = 1,

    /// <summary>gpt-image on the Azure AI Foundry resource the scorer already uses. Cheapest per strip.</summary>
    AzureOpenAI = 2
}
