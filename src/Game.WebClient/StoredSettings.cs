using System.Text.Json;

namespace Game.WebClient;

/// <summary>Persisted settings blob. Every field is optional so a partial blob never zeroes a volume.</summary>
public sealed record StoredSettings(string? Theme = null, string? Quality = null, double? Master = null, double? Sfx = null, double? Music = null, bool? Muted = null, bool? ReduceMotion = null, bool? View3d = null)
{
    public static StoredSettings? Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<StoredSettings>(raw); } catch (JsonException) { return null; }
    }
}
