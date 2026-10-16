using System.Net.Http.Json;
using System.Text.Json;
using Game.WebClient;
using Microsoft.JSInterop;

namespace Game.Web.Services;

public sealed class SettingsService(IJSRuntime js, HttpClient http)
{
    const string Key = "boardempire.settings";
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
    /// <summary>The OS prefers-reduced-motion preference (read at load, then followed live). Not persisted.</summary>
    public bool OsReduceMotion { get; private set; }
    /// <summary>Animate less: the in-app setting or the OS preference.</summary>
    public bool MotionReduced => ReduceMotion || OsReduceMotion;
    public bool View3d { get; set; } = true;
    /// <summary>True once the user (or a stored blob) chose a view; until then the play screen picks one from device capability.</summary>
    public bool HasView3d { get; set; }

    Task? _load;
    /// <summary>Loads persisted settings once per app instance; later calls await the same load.</summary>
    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    public event Action? Changed;

    static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);

    public async Task LoadAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            if (!string.IsNullOrEmpty(raw) && StoredSettings.Parse(raw) is { } s)
            {
                Theme = s.Theme ?? ThemeCatalog.Default; Quality = s.Quality ?? "auto";
                Master = Clamp(s.Master ?? 1); Sfx = Clamp(s.Sfx ?? 1); Music = Clamp(s.Music ?? 0.5);
                Muted = s.Muted ?? false; ReduceMotion = s.ReduceMotion ?? false; View3d = s.View3d ?? true; HasView3d = s.View3d.HasValue;
            }
        }
        catch { }
        await SetThemeAsync(Theme);
        try
        {
            var ui = await js.InvokeAsync<IJSObjectReference>("import", "./js/ui.js");
            OsReduceMotion = await ui.InvokeAsync<bool>("reducedMotion");
            await ui.InvokeVoidAsync("watchReducedMotion", DotNetObjectReference.Create(this));
        }
        catch { }
        Changed?.Invoke();
    }

    [JSInvokable]
    public void OsReduceMotionChanged(bool reduce)
    {
        if (OsReduceMotion == reduce) return;
        OsReduceMotion = reduce;
        Changed?.Invoke();
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new StoredSettings(Theme, Quality, Clamp(Master), Clamp(Sfx), Clamp(Music), Muted, ReduceMotion, HasView3d ? View3d : null));
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
