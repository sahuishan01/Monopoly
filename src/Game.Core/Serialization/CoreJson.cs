using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.State;

namespace Game.Core.Serialization;

/// <summary>
/// Canonical JSON for state, commands and events. Commands and events are polymorphic and carry
/// their type name in a "$t" discriminator.
/// </summary>
public static class CoreJson
{
    public static readonly JsonSerializerOptions Options = Build(false);
    public static readonly JsonSerializerOptions Indented = Build(true);

    private static JsonSerializerOptions Build(bool indented)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(AddPolymorphism<GameEvent>);
        resolver.Modifiers.Add(AddPolymorphism<GameCommand>);
        return new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private static void AddPolymorphism<T>(JsonTypeInfo info)
    {
        if (info.Type != typeof(T)) return;
        var poly = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$t",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var type in typeof(T).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(T).IsAssignableFrom(t))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            poly.DerivedTypes.Add(new JsonDerivedType(type, type.Name));
        }
        info.PolymorphismOptions = poly;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Unexpected null document");

    public static byte[] SerializeToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
}

public static class StateHasher
{
    /// <summary>
    /// Stable hash of the public view of a state. The authority and every replica produce the
    /// same value regardless of which private details they hold.
    /// </summary>
    public static string Hash(GameState state)
    {
        byte[] bytes = CoreJson.SerializeToBytes(state.RedactedFor(-1));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Hash of everything, including authority-only data. Used for determinism checks.</summary>
    public static string FullHash(GameState state) =>
        Convert.ToHexString(SHA256.HashData(CoreJson.SerializeToBytes(state)));
}
