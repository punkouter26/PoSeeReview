namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Configuration for the HuggingFace Inference Providers backend, activated by the
/// top-level <c>UseHuggingFace</c> flag. When on, HuggingFace replaces BOTH AI providers:
/// chat (Azure OpenAI → Qwen via the OpenAI-compatible router) and image generation
/// (Google Imagen → FLUX). FLUX is chosen partly because it supports a negative prompt,
/// which reliably suppresses the garbled text/speech-bubbles that Imagen bakes into the art.
/// The token is a HF user access token with the "Inference Providers" permission, stored in
/// Key Vault as <c>PoSeeReview--HuggingFace--ApiKey</c> (or user-secrets for local dev).
/// </summary>
public class HuggingFaceOptions
{
    public const string SectionName = "HuggingFace";

    /// <summary>HF user access token (<c>hf_...</c>) with Inference Providers permission.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI-compatible chat router base URL.</summary>
    public string ChatBaseUrl { get; set; } = "https://router.huggingface.co/v1";

    /// <summary>Chat model id for strangeness analysis + panel captions.</summary>
    public string ChatModel { get; set; } = "Qwen/Qwen2.5-7B-Instruct";

    /// <summary>Base URL for the text-to-image task endpoint; the model id is appended.</summary>
    public string ImageBaseUrl { get; set; } = "https://router.huggingface.co/hf-inference/models";

    /// <summary>Text-to-image model id. FLUX.1-schnell: fast, cheap, supports a negative prompt.</summary>
    public string ImageModel { get; set; } = "black-forest-labs/FLUX.1-schnell";

    /// <summary>Denoising steps. FLUX.1-schnell is distilled for ~4 steps.</summary>
    public int ImageSteps { get; set; } = 4;
}
