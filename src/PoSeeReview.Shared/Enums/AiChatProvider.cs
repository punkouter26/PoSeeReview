namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Selects the chat backend for strangeness analysis and panel captions, bound from
/// <c>Ai:ChatProvider</c> (NET_RULES 1.5 — enums over magic values).
/// <para>
/// <b>Why this is separate from <see cref="AiImageProvider"/>.</b> The two used to be one
/// setting, so the only way to try a different image model was to swap the scorer at the same
/// time — which turned every image-model comparison into a scorer comparison as well, and made
/// it impossible to answer "is this comic worse, or is this scorer stricter".
/// </para>
/// <para>
/// The scorer and the painter have nothing in common: one is a small JSON extraction over a few
/// hundred words of review text, the other is a diffusion model. They have different costs,
/// different latencies, different failure modes, and they belong in different tiers.
/// </para>
/// <para>
/// When <c>Ai:ChatProvider</c> is absent the value is derived from <c>Ai:ImageProvider</c>, so
/// every existing deployment keeps the exact pairing it had before this type existed.
/// </para>
/// </summary>
public enum AiChatProvider
{
    /// <summary>Azure AI Foundry deployment. The default, and the only paid chat backend.</summary>
    AzureOpenAI = 0,

    /// <summary>HuggingFace router (OpenAI-wire compatible), e.g. Qwen2.5-7B-Instruct.</summary>
    HuggingFace = 1,

    /// <summary>
    /// Local Ollama runtime. Zero marginal token cost, no key, no network egress.
    /// <para>
    /// This is the development, test and outage-fallback tier. A 3B instruct model scores five
    /// short reviews well enough to keep the whole pipeline exercisable with the paid endpoint
    /// switched off, which is the difference between "we can test the comic pipeline" and "we
    /// can test it once a day against a real bill".
    /// </para>
    /// </summary>
    Ollama = 2
}
