namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// A short invented conversation between the characters in a comic, returned by
/// <c>POST /api/comics/{placeId}/audio</c> for playback on the client.
/// <para>
/// Two or three speakers, eight to twelve lines, each line a single short sentence the
/// client's speechSynthesis can read in one breath. Stored cached on the comic row so
/// generation does not pay for the same skit twice.
/// </para>
/// </summary>
public sealed class ComicAudioSkit
{
    public string Title { get; set; } = string.Empty;

    /// <summary>One line per turn. Order is dialog order.</summary>
    public List<ComicAudioSkitLine> Lines { get; set; } = new();

    /// <summary>Sanity copy for the client — duplicates Lines.Count for cheap checks.</summary>
    public int LineCount => Lines.Count;
}

/// <summary>One piece of dialogue, ready for speech synthesis.</summary>
public sealed class ComicAudioSkitLine
{
    /// <summary>The character saying the line. The client picks the voice; this is just a label.</summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>The spoken line. Kept short on purpose — see ChatPrompts.BuildSkitPrompt.</summary>
    public string Text { get; set; } = string.Empty;
}
