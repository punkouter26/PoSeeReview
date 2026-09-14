namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Configuration for the HuggingFace Inference Providers backend, selected by
/// <c>Ai:ImageProvider</c> (images) and <c>Ai:ChatProvider</c> (chat) independently.
/// <para>
/// These used to be one switch, and the doc comment here used to say the pairing was a real
/// constraint because the chat and image endpoints share a token. That is true of the *token*
/// and not of the *choice*: nothing stops the router from serving FLUX while Azure scores the
/// reviews, and keeping them welded meant an image-model experiment could not be run without
/// changing the scorer underneath it.
/// </para>
/// <para>
/// The token is a HF user access token with the "Inference Providers" permission, stored in
/// Key Vault as <c>PoSeeReview--HuggingFace--ApiKey</c> (or user-secrets for local dev).
/// </para>
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

    /// <summary>
    /// Completion cap for the analysis call. Safe to set on this provider: an open instruct model
    /// served through the router has no hidden reasoning budget sharing the allowance, unlike the
    /// Azure reasoning deployment where the same cap can be consumed thinking.
    /// </summary>
    public int? AnalysisMaxTokens { get; set; } = 900;
}
