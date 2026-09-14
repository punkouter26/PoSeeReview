namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Configuration for the local Ollama chat backend, activated by setting <c>Ai:ChatProvider</c>
/// to <c>Ollama</c>.
/// <para>
/// Ollama exposes an OpenAI-compatible surface at <c>/v1</c>, so this reuses the OpenAI SDK's
/// <c>ChatClient</c> exactly as <see cref="HuggingFaceChatService"/> does. No new client, no new
/// wire format, no new parser — the only difference is the base URL and the absence of a key.
/// </para>
/// <para>
/// Defaults to a 3B instruct model on purpose. The job is extracting three fields from a few
/// short reviews; a larger model is slower on the same machine and scores this task no better.
/// </para>
/// </summary>
public class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>OpenAI-compatible base URL. Ollama serves this path natively.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    /// <summary>Model id as Ollama knows it (<c>ollama list</c>).</summary>
    public string ChatModel { get; set; } = "qwen2.5:3b-instruct";

    /// <summary>
    /// Completion cap for the analysis call. Safe to set here — a local instruct model has no
    /// hidden reasoning budget sharing the allowance, which is the whole reason the Azure path
    /// leaves its cap unset. There is deliberately no second cap for captions: they come back
    /// from this same call now, so a caption-only budget would have nothing to govern.
    /// </summary>
    public int MaxCompletionTokens { get; set; } = 900;

    /// <summary>
    /// Per-request network timeout. Generous because a CPU-only machine may take a while to
    /// produce the first token, and there is no bill attached to waiting.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
