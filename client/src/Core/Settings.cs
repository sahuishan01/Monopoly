using System.Text.Json;
using Game.Core.Rules;
using Godot;

namespace BoardEmpire.Core;

public enum ViewMode
{
    Flat2D,
    City3D,
}

public enum AnimationSpeed
{
    Normal,
    Fast,
    Minimal,
}

public enum Quality
{
    Low,
    Medium,
    High,
    Ultra,
}

public sealed class SavedPreset
{
    public string Name { get; set; } = "";
    public GameRules Rules { get; set; } = new();
}

/// <summary>
/// Everything that belongs to this device only. None of it is replicated: two players in the
/// same match can use different views, speeds and accessibility options.
/// </summary>
public sealed class Settings
{
    private const string Path = "user://settings.json";

    public string PlayerName { get; set; } = "Player";
    public int Token { get; set; }
    public ViewMode ViewMode { get; set; } = ViewMode.City3D;
    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Normal;
    public Quality Quality { get; set; } = Quality.Medium;
    public string Language { get; set; } = "en";

    public float MasterVolume { get; set; } = 0.9f;
    public float MusicVolume { get; set; } = 0.5f;
    public float SfxVolume { get; set; } = 0.9f;
    public float AmbientVolume { get; set; } = 0.5f;
    public float VoiceVolume { get; set; } = 0.8f;
    public bool Haptics { get; set; } = true;

    public bool ColorBlindPatterns { get; set; }
    public bool LargeText { get; set; }
    public bool ReduceMotion { get; set; }
    public bool CameraShake { get; set; } = true;
    public bool HighContrast { get; set; }
    public bool Subtitles { get; set; } = true;

    public string ServerUrl { get; set; } = "https://boardempire.example.com";
    public string AuthToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool AccountIsGuest { get; set; } = true;
    public string LastBoard { get; set; } = "neo_city";
    public string LastPreset { get; set; } = "classic";
    public List<SavedPreset> Presets { get; set; } = new();

    public float TextScale => LargeText ? 1.25f : 1f;

    public event Action? Changed;

    public static Settings Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
                var loaded = JsonSerializer.Deserialize<Settings>(file.GetAsText());
                if (loaded != null) return loaded;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("Settings could not be read: " + e.Message);
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            file?.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            GD.PushWarning("Settings could not be saved: " + e.Message);
        }
        Changed?.Invoke();
    }
}
