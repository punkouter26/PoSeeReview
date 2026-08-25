namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Wire form of a strangeness receipt. Kept as a separate mutable class rather than reusing
/// the domain record so the client's source-generated <c>AppJsonContext</c> stays trim-safe.
/// </summary>
public class StrangenessReceiptDto
{
    /// <summary>Verbatim fragment of a real public review.</summary>
    public string Quote { get; set; } = string.Empty;

    /// <summary>Points this fragment contributed to the 0-100 strangeness score.</summary>
    public int Points { get; set; }
}
