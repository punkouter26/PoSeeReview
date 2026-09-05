using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Reaction tallies for one comic, plus whichever reaction the calling principal left.
/// Response of <c>GET /api/reactions/{placeId}</c> and <c>POST /api/reactions/{placeId}</c>.
/// </summary>
public sealed class ReactionCountsDto
{
    public string PlaceId { get; set; } = string.Empty;

    public int Laugh { get; set; }
    public int Mind { get; set; }
    public int Grim { get; set; }
    public int Love { get; set; }

    /// <summary>
    /// The caller's own reaction, or <c>null</c> if they have not reacted. Sent so the client
    /// paints the selected state on load rather than after a round trip.
    /// </summary>
    public ReactionKind? MyReaction { get; set; }

    /// <summary>Total reactions across every kind.</summary>
    public int Total => Laugh + Mind + Grim + Love;
}

/// <summary>Body of <c>POST /api/reactions/{placeId}</c>.</summary>
public sealed class ReactionRequestDto
{
    /// <summary>
    /// The reaction to record, or <c>null</c> to withdraw the caller's existing one. Reacting
    /// again with the same kind is the same withdrawal, so the button is a real toggle.
    /// </summary>
    public ReactionKind? Reaction { get; set; }
}
