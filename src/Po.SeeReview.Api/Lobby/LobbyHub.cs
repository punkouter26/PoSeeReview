using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Po.SeeReview.Api.Lobby;

/// <summary>
/// SignalR hub for real-time multiplayer lobby coordination.
/// Implements a Server-Validated Host pattern: the first player to join becomes the Host,
/// and the server acts as the source of truth for connection state.
/// </summary>
public sealed class LobbyHub : Hub
{
    // Thread-safe lobby state — in production, use a distributed backplane
    private static readonly ConcurrentDictionary<string, LobbyPlayer> _players = new();
    private static string? _hostConnectionId;

    /// <summary>
    /// Player joins the lobby. First player becomes the Host.
    /// </summary>
    public async Task JoinLobby(string displayName)
    {
        var connectionId = Context.ConnectionId;
        var isHost = _players.IsEmpty;

        var player = new LobbyPlayer
        {
            ConnectionId = connectionId,
            DisplayName = displayName,
            IsHost = isHost,
            IsReady = false
        };

        _players.TryAdd(connectionId, player);

        if (isHost)
        {
            _hostConnectionId = connectionId;
        }

        // Notify all players of the updated lobby state
        await Clients.All.SendAsync("LobbyUpdated", _players.Values.ToList(), _hostConnectionId);
    }

    /// <summary>
    /// Player toggles their ready status.
    /// </summary>
    public async Task SetReady(bool isReady)
    {
        if (_players.TryGetValue(Context.ConnectionId, out var player))
        {
            player.IsReady = isReady;
            await Clients.All.SendAsync("PlayerReady", player.ConnectionId, isReady);
        }
    }

    /// <summary>
    /// Host starts the game. Server validates all players are ready.
    /// </summary>
    public async Task StartGame()
    {
        if (_hostConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.SendAsync("Error", "Only the Host can start the game.");
            return;
        }

        var allReady = _players.Values.All(p => p.IsHost || p.IsReady);
        if (!allReady)
        {
            await Clients.Caller.SendAsync("Error", "All players must be ready before starting.");
            return;
        }

        await Clients.All.SendAsync("GameStarting", _players.Values.ToList());
    }

    /// <summary>
    /// Handle player disconnection — reassign host if needed.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_players.TryRemove(Context.ConnectionId, out _))
        {
            // If the host disconnected, assign the next player as host
            if (_hostConnectionId == Context.ConnectionId && !_players.IsEmpty)
            {
                var newHost = _players.Values.First();
                newHost.IsHost = true;
                _hostConnectionId = newHost.ConnectionId;
            }

            await Clients.All.SendAsync("LobbyUpdated", _players.Values.ToList(), _hostConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Represents a player in the lobby.
/// </summary>
public sealed class LobbyPlayer
{
    public string ConnectionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsReady { get; set; }
}
