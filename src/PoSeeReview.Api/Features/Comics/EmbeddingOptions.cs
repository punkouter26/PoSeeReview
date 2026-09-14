namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Configuration for the embedding backend.
/// <para>
/// Deliberately its own section rather than a third value on <c>Ai:ChatProvider</c>, because an
/// embedding is not a chat completion: it is a background enrichment with no user waiting on it,
/// no prompt, and no JSON contract. It can point somewhere else, be off, or be absent without
/// changing anything a user sees.
/// </para>
/// <para>
/// Off unless configured. An embedding backend that is merely <em>reachable by default</em>
/// would have every integration test attempt a localhost connection on every generation.
/// </para>
/// </summary>
public class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    /// <summary>
    /// Opt-in switch. When false, <see cref="IEmbeddingService.EmbedAsync"/> returns immediately
    /// and the similar-comics feature has nothing to say.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OpenAI-compatible base URL. Defaults to a local Ollama, which is the cheapest place to
    /// run this: nothing about the app's own behaviour depends on the vectors being good, so the
    /// quality bar for paying for them is high and the bar for a few hundred free local vectors
    /// is met immediately.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    /// <summary>Embedding model id as the backend knows it.</summary>
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Per-request timeout. Short on purpose: this runs inside a user's generation request, and
    /// a slow embedding server must cost a related link rather than the whole comic.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// How many live comics a similar-comics query will pull before ranking. The read is a
    /// single-partition scan, so it is cheap per row but unbounded in principle — the same reason
    /// <c>InsightsOptions.MaxRowsScanned</c> exists.
    /// </summary>
    public int MaxCandidates { get; set; } = 500;
}
