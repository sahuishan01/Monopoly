using Godot;

namespace BoardEmpire.Core;

public enum Sfx
{
    Click,
    DiceRoll,
    DiceLand,
    Step,
    Coin,
    Purchase,
    Build,
    Card,
    Jail,
    Gavel,
    Notify,
    Error,
    Fanfare,
    Bankrupt,
    Whoosh,
}

/// <summary>Mixer buses, pooled effect players, board ambience and the adaptive music bed.</summary>
public partial class AudioDirector : Node
{
    private static readonly string[] Buses = { "Music", "SFX", "Ambient", "UI", "Voice" };

    private readonly Dictionary<Sfx, AudioStream> _sounds = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[4];
    private AudioStreamPlayer _ambience = null!;
    private Settings _settings = null!;
    private int _intensity = -1;
    private int _next;

    public void Setup(Settings settings)
    {
        _settings = settings;
        foreach (string bus in Buses)
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, bus);
            AudioServer.SetBusSend(index, "Master");
        }

        _sounds[Sfx.Click] = Synth.Click();
        _sounds[Sfx.DiceRoll] = Synth.DiceRoll();
        _sounds[Sfx.DiceLand] = Synth.DiceLand();
        _sounds[Sfx.Step] = Synth.Step();
        _sounds[Sfx.Coin] = Synth.Coin();
        _sounds[Sfx.Purchase] = Synth.Purchase();
        _sounds[Sfx.Build] = Synth.Build();
        _sounds[Sfx.Card] = Synth.Card();
        _sounds[Sfx.Jail] = Synth.Jail();
        _sounds[Sfx.Gavel] = Synth.Gavel();
        _sounds[Sfx.Notify] = Synth.Notify();
        _sounds[Sfx.Error] = Synth.Error();
        _sounds[Sfx.Fanfare] = Synth.Fanfare();
        _sounds[Sfx.Bankrupt] = Synth.Bankrupt();
        _sounds[Sfx.Whoosh] = Synth.Whoosh();

        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(p);
            _pool.Add(p);
        }
        for (int i = 0; i < _music.Length; i++)
        {
            _music[i] = new AudioStreamPlayer { Bus = "Music", Stream = Synth.Music(i), VolumeDb = -60 };
            AddChild(_music[i]);
        }
        _ambience = new AudioStreamPlayer { Bus = "Ambient", Stream = Synth.Ambience(), VolumeDb = -8 };
        AddChild(_ambience);
        ApplyVolumes();
        settings.Changed += ApplyVolumes;
    }

    private static void SetBus(string name, float linear)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0) return;
        AudioServer.SetBusMute(index, linear <= 0.001f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.001f)));
    }

    public void ApplyVolumes()
    {
        SetBus("Master", _settings.MasterVolume);
        SetBus("Music", _settings.MusicVolume);
        SetBus("SFX", _settings.SfxVolume);
        SetBus("UI", _settings.SfxVolume);
        SetBus("Ambient", _settings.AmbientVolume);
        SetBus("Voice", _settings.VoiceVolume);
    }

    public void Play(Sfx sfx, float pitch = 1f, float volumeDb = 0f, bool ui = false)
    {
        if (!_sounds.TryGetValue(sfx, out var stream)) return;
        var player = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = volumeDb;
        player.Bus = ui ? "UI" : "SFX";
        player.Play();
    }

    /// <summary>0 = menu / early game … 3 = final showdown. Layers are cross-faded.</summary>
    public void SetMusicIntensity(int intensity)
    {
        intensity = Math.Clamp(intensity, 0, _music.Length - 1);
        if (intensity == _intensity) return;
        _intensity = intensity;
        for (int i = 0; i < _music.Length; i++)
        {
            var player = _music[i];
            bool on = i == intensity;
            if (on && !player.Playing) player.Play();
            int layer = i;
            var tween = CreateTween();
            tween.TweenProperty(player, "volume_db", on ? -9f : -60f, 1.8);
            if (!on)
            {
                // A quick change back must not silence the layer that became active again.
                tween.TweenCallback(Callable.From(() =>
                {
                    if (_intensity != layer) player.Stop();
                }));
            }
        }
    }

    public void StopMusic()
    {
        _intensity = -1;
        foreach (var p in _music) p.Stop();
    }

    public void SetAmbience(bool on)
    {
        if (on && !_ambience.Playing) _ambience.Play();
        else if (!on) _ambience.Stop();
    }
}

/// <summary>Distinct vibration patterns per game moment, honouring the user's haptics switch.</summary>
public static class Haptics
{
    public static bool Enabled { get; set; } = true;

    private static void Buzz(int ms, float amplitude = -1f)
    {
        if (!Enabled) return;
        if (OS.GetName() is not ("Android" or "iOS")) return;
        Input.VibrateHandheld(ms, amplitude);
    }

    public static void Tap() => Buzz(12, 0.3f);
    public static void DiceLand() => Buzz(35, 0.6f);
    public static void Purchase() => Buzz(45, 0.5f);
    public static void BigPayment() => Buzz(120, 0.9f);
    public static void Build() => Buzz(60, 0.7f);
    public static void Card() => Buzz(25, 0.4f);
    public static void AuctionWin() => Buzz(90, 0.8f);
    public static void Bankruptcy() => Buzz(300, 1f);
    public static void YourTurn() => Buzz(55, 0.5f);
}
