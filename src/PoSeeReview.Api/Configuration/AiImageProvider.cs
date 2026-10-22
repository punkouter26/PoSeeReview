namespace PoSeeReview.Api.Configuration;

/// <summary>
/// Selects the image backend for comic generation (NET_RULES 1.5 — enums over magic values).
/// Bound from <c>Ai:ImageProvider</c> (the draft painter) and <c>Ai:RemasterProvider</c>. One
/// member per implemented <c>IImageGenerationService</c>, so no value can only throw at startup.
/// </summary>
public enum AiImageProvider
{
    /// <summary>Gemini image (generateContent). The remaster painter.</summary>
    Gemini = 0,

    /// <summary>gpt-image on the Azure AI Foundry resource the scorer already uses. Cheapest per strip; the default draft painter.</summary>
    AzureOpenAI = 2,

    /// <summary>
    /// A local OpenAI-compatible image server (LocalAI, stable-diffusion.cpp, a ComfyUI wrapper).
    /// Development only: free iterations on the image prompt. Refused at startup elsewhere.
    /// </summary>
    Local = 3
}
