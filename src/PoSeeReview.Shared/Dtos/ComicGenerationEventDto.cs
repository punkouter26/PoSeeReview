using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// One server-sent event from the streaming generation endpoint. A single envelope type keeps
/// the client parser to "one <c>data:</c> line is one JSON object" — multi-line SSE frames with
/// separate <c>event:</c> names would need a state machine on a trimmed WASM client for no gain.
/// </summary>
public class ComicGenerationEventDto
{
    /// <summary>One of <see cref="PhaseKind"/>, <see cref="CompleteKind"/>, <see cref="ErrorKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public const string PhaseKind = "phase";
    public const string CompleteKind = "complete";
    public const string ErrorKind = "error";

    /// <summary>Set when <see cref="Kind"/> is <see cref="PhaseKind"/>.</summary>
    public ComicGenerationPhase Phase { get; set; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="CompleteKind"/>.</summary>
    public ComicDto? Comic { get; set; }

    /// <summary>
    /// HTTP-equivalent status for <see cref="ErrorKind"/>. The stream itself is already a 200 by
    /// the time generation fails, so the real status has to travel in the payload for the client
    /// to keep showing the same tailored messages it shows for a plain POST.
    /// </summary>
    public int ErrorStatus { get; set; }

    public string? ErrorTitle { get; set; }

    public string? ErrorDetail { get; set; }
}
