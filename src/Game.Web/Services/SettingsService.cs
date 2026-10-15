using System.Net.Http.Json;
using System.Text.Json;
using Game.WebClient;
using Microsoft.JSInterop;

namespace Game.Web.Services;

public sealed class SettingsService(IJSRuntime js, HttpClient http)
{
    const string Key = "boardempire.settings";
    sealed record Stored(string? Theme, string? Quality, double Master, double Sfx, double Music, bool Muted, bool ReduceMotion, bool View3d);

    string _theme = ThemeCatalog.Default;
    string _quality = "auto";
    IJSObjectReference? _mod;

    public string Theme { get => _theme; set { _theme = ThemeCatalog.Resolve(value); } }
    public string Quality { get => _quality; set { _quality = value is "high" or "low" ? value : "auto"; } }
    public double Master { get; set; } = 1;
    public double Sfx { get; set; } = 1;
    public double Music { get; set; } = 0.5;
    public bool Muted { get; set; }
    public bool ReduceMotion { get; set; }
    public bool View3d { get; set; } = true;

    public event Action? Changed;

    static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);

    public async Task LoadAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            if (!string.IsNullOrEmpty(raw) && JsonSerializer.Deserialize<Stored>(raw) is { } s)
            {
                Theme = s.Theme ?? ThemeCatalog.Default; Quality = s.Quality ?? "auto";
                Master = Clamp(s.Master); Sfx = Clamp(s.Sfx); Music = Clamp(s.Music);
                Muted = s.Muted; ReduceMotion = s.ReduceMotion; View3d = s.View3d;
            }
        }
        catch { }
        await SetThemeAsync(Theme);
        Changed?.Invoke();
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new Stored(Theme, Quality, Clamp(Master), Clamp(Sfx), Clamp(Music), Muted, ReduceMotion, View3d));
            await js.InvokeVoidAsync("localStorage.setItem", Key, json);
        }
        catch { }
        Changed?.Invoke();
    }

    public async Task SetThemeAsync(string id)
    {
        Theme = id;
        try
        {
            await ApplyAsync(Theme);
        }
        catch
        {
            _theme = ThemeCatalog.Default;
            try { await ApplyAsync(_theme); } catch { }
        }
        Changed?.Invoke();
    }

    async Task ApplyAsync(string id)
    {
        var json = await http.GetStringAsync($"themes/{id}.json");
        _mod ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await _mod.InvokeAsync<string>("apply", json);
    }
}
