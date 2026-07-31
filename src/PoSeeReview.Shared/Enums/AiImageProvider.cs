namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Selects the AI backend for comic generation (NET_RULES 1.5 — enums over magic values).
/// Replaces the former <c>UseHuggingFace</c> boolean, which could only express two of the
/// three providers and silently coupled the chat and image paths together.
/// </summary>
public enum AiImageProvider
{
    /// <summary>Google Gemini/Imagen for images, Azure OpenAI for chat. Default.</summary>
    Gemini = 0,

    /// <summary>HuggingFace router: FLUX.1-schnell for images, Qwen2.5 for chat — cheapest tier.</summary>
    HuggingFace = 1,

    /// <summary>Azure OpenAI for both chat and image generation.</summary>
    AzureOpenAI = 2
}
