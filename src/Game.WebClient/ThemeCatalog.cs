using System.Text.Json;

namespace Game.WebClient;

public static class ThemeCatalog
{
    public const string Default = "neon-noir";
    public static IReadOnlyList<string> Ids { get; } = ["neon-noir", "classic-gold", "frost", "sakura"];

    static readonly string[] CssKeys = ["--bg", "--panel", "--text", "--muted", "--accent", "--bad", "--line", "--glow"];
    static readonly string[] SceneKeys = ["sky", "felt", "fog", "ambient", "sun", "bloom"];

    /// <summary>A theme's name and picker colours.</summary>
    public sealed record Swatch(string Id, string Name, string Bg, string Panel, string Accent, string Glow);

    /// <summary>
    /// Picker colours embedded statically, so theme chips show real colours on a cold load before any theme JSON
    /// is fetched. Must match wwwroot/themes/*.json (ThemeCatalogTests checks this).
    /// </summary>
    public static IReadOnlyList<Swatch> Swatches { get; } =
    [
        new("neon-noir", "Neon Noir", "#07080f", "#111420", "#22f5c4", "#7c3aed"),
        new("classic-gold", "Classic Gold", "#14100a", "#1f1810", "#e6b04a", "#f5d58a"),
        new("frost", "Frost", "#0a141c", "#112230", "#7dd3fc", "#bae6fd"),
        new("sakura", "Sakura", "#1a0f16", "#26161f", "#f9a8d4", "#fbcfe8"),
    ];

    public static string Resolve(string? id) => id is not null && Ids.Contains(id) ? id : Default;

    public static IReadOnlyList<string> Validate(string json)
    {
        var missing = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return ["json"];
            foreach (var k in new[] { "id", "name" }) if (!r.TryGetProperty(k, out _)) missing.Add(k);
            Check(r, "css", CssKeys, missing);
            Check(r, "scene", SceneKeys, missing);
            Check(r, "token", ["metal", "rough"], missing);
            Check(r, "sound", ["wave", "root"], missing);
        }
        catch (JsonException) { return ["json"]; }
        return missing;
    }

    static void Check(JsonElement r, string name, string[] keys, List<string> missing)
    {
        if (!r.TryGetProperty(name, out var o) || o.ValueKind != JsonValueKind.Object) { missing.Add(name); return; }
        foreach (var k in keys) if (!o.TryGetProperty(k, out _)) missing.Add(name + "." + k);
    }
}
