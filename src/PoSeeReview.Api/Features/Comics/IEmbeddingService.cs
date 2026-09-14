namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Turns a comic's narrative into a vector, so two comics about the same kind of strangeness can
/// be found without sharing a single word.
/// <para>
/// <b>Why this exists at all.</b> Everything this app knows about a comic is text and is only
/// ever compared to itself. The archive is a pile of rows nobody can ask a question of except
/// "sort by score", which is why the Hall of Fame is the only thing built to outlive expiry and
/// the only thing anyone returns to. A vector is the missing primitive: it is what turns "here
/// is a funny comic" into "here is another one like it".
/// </para>
/// <para>
/// <b>Never on the critical path, never able to fail a comic.</b> The return type is an empty
/// array rather than an exception, and that is the contract, not an oversight: a similarity
/// index is an enrichment of a comic that is already finished and already paid for. Losing it
/// means one fewer related link; throwing would mean losing the comic. Callers may rely on
/// <see cref="EmbedAsync"/> never throwing.
/// </para>
/// </summary>
public interface IEmbeddingService
{
    /// <summary>False when no embedding backend is configured — callers can skip the work entirely.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Returns the vector for <paramref name="text"/>, or an empty array if embeddings are
    /// disabled or the backend could not be reached. Never throws.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
