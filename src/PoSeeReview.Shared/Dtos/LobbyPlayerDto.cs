namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Represents a player in the lobby.
/// </summary>
public sealed class LobbyPlayerDto
{
    public string ConnectionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsReady { get; set; }
}
