namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Selects the AI backend for comic generation (NET_RULES 1.5 — enums over magic values).
/// Bound from <c>Ai:ImageProvider</c>; replaces the former <c>UseHuggingFace</c> boolean.
///
/// One member per provider pair that is actually implemented. There is deliberately no
/// <c>AzureOpenAI</c> member: <see cref="PoSeeReview.Api"/> has exactly two
/// <c>IImageGenerationService</c> implementations (Gemini and HuggingFace), so a third value
/// would be a config setting that only ever throws at startup.
///
/// The choice still selects the chat provider as well as the image provider — the two are
/// paired, not independent. That pairing is a real constraint (HuggingFace's chat and image
/// endpoints share a token and a client), not an oversight.
/// </summary>
public enum AiImageProvider
{
    /// <summary>Google Imagen for images, Azure OpenAI for chat. Default.</summary>
    Gemini = 0,

    /// <summary>HuggingFace router: FLUX.1-schnell for images, Qwen2.5 for chat — cheapest tier.</summary>
    HuggingFace = 1
}
