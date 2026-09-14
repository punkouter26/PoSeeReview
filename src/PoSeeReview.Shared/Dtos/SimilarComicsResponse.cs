namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Comics that are about the same kind of strangeness as the one being viewed.
/// <para>
/// <paramref name="Similarity"/> is a cosine score in [-1, 1] and is surfaced rather than hidden
/// so the ordering is explainable: a list of comics with no visible reason to be together reads
/// as random, and a number at least says how closely the app thinks they match.
/// </para>
/// <para>
/// Only live comics appear. A similar comic that has expired is a link to a 404, and there is no
/// point recommending something that cannot be opened.
/// </para>
/// </summary>
public sealed record SimilarComicsResponse(IReadOnlyList<SimilarComicDto> Comics);

/// <summary>One related comic: enough to render a row and to open it.</summary>
public sealed record SimilarComicDto(
    string PlaceId,
    string RestaurantName,
    int StrangenessScore,
    double Similarity);
