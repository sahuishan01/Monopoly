using Game.Core.Board;
using Game.Core.Events;
using Game.Core.State;
using Game.Net;
using Game.Net.Transport;
using Game.Protocol;

namespace Game.WebClient;

/// <summary>One browser connection to a room: wraps RoomClient, keeps a paced display copy of the match state and a tick loop.</summary>
public sealed class GameSession : IDisposable
{
    private readonly EventPacer _pacer = new();
    private double _last = -1;

    public RoomClient Client { get; }
    public GameState? Display { get; private set; }
    public BoardDefinition? Board => Client.Board;
    public string? UpdateRequired { get; private set; }

    public event Action? Changed;
    public event Action<GameEvent, GameState>? EventPlayed;

    public GameSession(IGameTransport transport, string playerName, string authToken = "", string resumeToken = "")
    {
        Client = new RoomClient(transport, playerName) { AuthToken = authToken, ResumeToken = resumeToken };
        Client.LobbyChanged += () => Changed?.Invoke();
        Client.PeerStatusChanged += _ => Changed?.Invoke();
        Client.Disconnected += _ => Changed?.Invoke();
        Client.ReturnedToLobby += () =>
        {
            _pacer.Clear();
            Display = null;
            Changed?.Invoke();
        };
        Client.SnapshotLoaded += s =>
        {
            _pacer.Clear();
            Display = s.Clone();
            Changed?.Invoke();
        };
        Client.EventsApplied += events => _pacer.Enqueue(events);
        Client.Rejected += r =>
        {
            if (r.Code is RejectCode.VersionTooOld or RejectCode.VersionTooNew || r.MinimumVersion > ProtocolInfo.Version)
                UpdateRequired = r.Reason;
            Changed?.Invoke();
        };
    }

    public Task ConnectAsync() => Client.ConnectAsync();

    public Task DisconnectAsync() => Client.DisconnectAsync();

    public void Tick(double now)
    {
        double dt = _last < 0 ? 0 : Math.Max(0, now - _last);
        _last = now;
        Client.Poll(now);
        if (Display == null) return;
        foreach (var e in _pacer.Advance(dt))
        {
            e.Apply(Display);
            Display.Version = e.Version;
            EventPlayed?.Invoke(e, Display);
            Changed?.Invoke();
        }
    }

    public void Dispose() => _ = Client.DisconnectAsync();
}
