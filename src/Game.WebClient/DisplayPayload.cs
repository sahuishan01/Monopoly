using System.Text.Json;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Serialization;
using Game.Core.State;

namespace Game.WebClient;

/// <summary>JSON handed to the three.js module. States are already redacted for the viewer by the server.</summary>
public static class DisplayPayload
{
    public static string State(GameState s) => JsonSerializer.Serialize(s, CoreJson.Options);
    public static string Event(GameEvent e) => JsonSerializer.Serialize<GameEvent>(e, CoreJson.Options);
    public static string Board(BoardDefinition b) => JsonSerializer.Serialize(b, CoreJson.Options);
}
