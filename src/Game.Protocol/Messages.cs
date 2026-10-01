using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;

namespace Game.Protocol;

public static class ProtocolInfo
{
    /// <summary>Bump whenever a message, command or event changes shape.</summary>
    public const int Version = 1;
    /// <summary>Oldest client version this build still talks to.</summary>
    public const int MinimumSupported = 1;
    public const int MaxSeats = 8;
}

/// <summary>Base type of everything that travels inside a <see cref="NetworkEnvelope"/>.</summary>
public abstract record NetMessage;

public enum RejectCode
{
    VersionTooOld,
    VersionTooNew,
    RoomFull,
    MatchInProgress,
    BadToken,
    Kicked,
    RoomClosed,
}

public enum PeerRole
{
    Player,
    Spectator,
}

public sealed class SeatInfo
{
    public int Seat { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Empty for bot seats.</summary>
    public string PeerId { get; set; } = "";
    public bool IsBot { get; set; }
    public BotLevel BotLevel { get; set; } = BotLevel.Medium;
    public bool Ready { get; set; }
    public bool Connected { get; set; } = true;
    public int Token { get; set; }
    public int Team { get; set; } = -1;
    public Ability Ability { get; set; }
    /// <summary>Account id for online play; empty for guests and local seats.</summary>
    public string UserId { get; set; } = "";

    public SeatInfo Clone() => (SeatInfo)MemberwiseClone();
}

public sealed class LobbyInfo
{
    public string RoomCode { get; set; } = "";
    public string RoomName { get; set; } = "";
    public int Revision { get; set; }
    public string HostPeerId { get; set; } = "";
    public string BoardId { get; set; } = BoardLibrary.DefaultBoardId;
    public GameRules Rules { get; set; } = new();
    public List<SeatInfo> Seats { get; set; } = new();
    public int Spectators { get; set; }
    public bool InMatch { get; set; }
    public bool Ranked { get; set; }
}

// ------------------------------------------------------------------ client -> host

public sealed record Hello(
    int ProtocolVersion,
    string PlayerName,
    PeerRole Role = PeerRole.Player,
    string AuthToken = "",
    string ResumeToken = "",
    string ClientSeed = "",
    int LastVersion = 0,
    int Token = 0) : NetMessage;

public sealed record SetReady(bool Ready) : NetMessage;

/// <summary>Host only. Null fields are left unchanged.</summary>
public sealed record UpdateSettings(GameRules? Rules, string? BoardId, string? RoomName) : NetMessage;

/// <summary>Host only.</summary>
public sealed record AddBot(BotLevel Level) : NetMessage;

/// <summary>Adds another seat controlled by the same device (pass and play).</summary>
public sealed record AddLocalPlayer(string Name) : NetMessage;

/// <summary>Host removes any seat; other peers may only remove their own extra seats.</summary>
public sealed record KickPlayer(int Seat) : NetMessage;

public sealed record UpdateSeat(int Seat, string? Name, int? Token, int? Team, Ability? Ability, BotLevel? BotLevel) : NetMessage;

public sealed record StartMatch : NetMessage;

public sealed record LeaveRoom : NetMessage;

public sealed record SubmitCommand(GameCommand Command) : NetMessage;

public sealed record ResyncRequest(int LastVersion) : NetMessage;

/// <summary>Preset chat line or emote; free text is intentionally not supported.</summary>
public sealed record SendChat(int PresetId) : NetMessage;

public sealed record Ping(long ClientTime) : NetMessage;

/// <summary>After a match: the host returns everyone to the lobby for another game.</summary>
public sealed record RequestRematch : NetMessage;

/// <summary>Sent to a newly elected host so it can restore objectives only their owner knows.</summary>
public sealed record ReportPrivateState(int Seat, int[] ObjectiveIds) : NetMessage;

// ------------------------------------------------------------------ host -> client

public sealed record Welcome(int ProtocolVersion, string PeerId, string ResumeToken, int[] Seats, PeerRole Role) : NetMessage;

public sealed record Reject(RejectCode Code, string Reason, int MinimumVersion) : NetMessage;

public sealed record LobbyState(LobbyInfo Lobby) : NetMessage;

/// <summary>Full replica for the receiving viewer. Board is included when the client may not have it.</summary>
public sealed record Snapshot(GameState State, BoardDefinition? Board, string StateHash, int[] Seats) : NetMessage;

/// <summary>Events <c>FromVersion..FromVersion+Events.Count-1</c> and the public state hash after them.</summary>
public sealed record EventBatch(int FromVersion, List<GameEvent> Events, string StateHash, string CommandId) : NetMessage;

public sealed record CommandRejected(string CommandId, string Reason) : NetMessage;

public sealed record Heartbeat(int StateVersion, string StateHash, int LobbyRevision, int TimerSeat, int TimerSeconds) : NetMessage;

public sealed record PeerStatus(int Seat, bool Connected, int ReconnectSeconds) : NetMessage;

public sealed record ChatMessage(int Seat, int PresetId) : NetMessage;

public sealed record Pong(long ClientTime) : NetMessage;

public sealed record RoomClosed(string Reason) : NetMessage;

public static class ChatPresets
{
    public static readonly string[] Lines =
    {
        "Good game!", "Nice trade.", "No chance!", "Deal?", "Well played.", "Hurry up!", "Thanks!", "Oops.",
        "😂", "😱", "🔥", "👍", "😎", "💸",
    };

    public static bool IsValid(int id) => id >= 0 && id < Lines.Length;
}
