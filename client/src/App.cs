using BoardEmpire.Core;
using BoardEmpire.Net;
using BoardEmpire.UI;
using Godot;

namespace BoardEmpire;

/// <summary>Application root: services, theme, screen stack, toasts and developer automation.</summary>
public partial class App : Control
{
    private readonly List<Screen> _stack = new();
    private Control _screens = null!;
    private VBoxContainer _toasts = null!;
    private ColorRect _fade = null!;

    public static App I { get; private set; } = null!;

    public Settings Settings { get; private set; } = null!;
    public AudioDirector Audio { get; private set; } = null!;
    public GameSession Session { get; private set; } = null!;
    public LocalProfile Profile { get; private set; } = null!;
    public Screen? Current => _stack.Count > 0 ? _stack[^1] : null;

    public override void _Ready()
    {
        I = this;
        Settings = Settings.Load();
        Profile = LocalProfile.Load();
        Loc.Language = Settings.Language;
        Haptics.Enabled = Settings.Haptics;
        Ui.LoadFonts();
        Theme = Ui.BuildTheme(Settings);
        GetTree().AutoAcceptQuit = false;
        GetTree().QuitOnGoBack = false;

        var background = new ColorRect { Color = Tokens.Bg, MouseFilter = MouseFilterEnum.Ignore }.Full();
        AddChild(background);
        _screens = new Control { MouseFilter = MouseFilterEnum.Ignore }.Full();
        AddChild(_screens);

        Audio = new AudioDirector();
        AddChild(Audio);
        Audio.Setup(Settings);
        Ui.ClickSound = () =>
        {
            Audio.Play(Sfx.Click, 1f, -6f, ui: true);
            Haptics.Tap();
        };

        Session = new GameSession { PlayerName = Settings.PlayerName };
        AddChild(Session);
        Session.Api.Configure(Settings.ServerUrl, Settings.AuthToken);
        Session.Notice += text => Toast(text);

        _toasts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _toasts.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _toasts.GrowHorizontal = GrowDirection.Both;
        _toasts.OffsetTop = 18;
        AddChild(_toasts);

        _fade = new ColorRect { Color = Tokens.Bg with { A = 0 }, MouseFilter = MouseFilterEnum.Ignore }.Full();
        AddChild(_fade);

        Settings.Changed += () =>
        {
            Loc.Language = Settings.Language;
            Haptics.Enabled = Settings.Haptics;
            Session.PlayerName = Settings.PlayerName;
        };

        Go(new MainMenuScreen(), clear: true);
        Audio.SetMusicIntensity(0);
        DevAutomation.Run(this);
    }

    /// <summary>Rebuilds the theme (contrast, text size, language) and the screen on top.</summary>
    public void RefreshTheme()
    {
        Theme = Ui.BuildTheme(Settings);
        if (Current is { } top)
        {
            var fresh = top.Recreate();
            if (fresh != null) Go(fresh, replace: true, instant: true);
        }
    }

    // ------------------------------------------------------------------ navigation

    public void Go(Screen screen, bool replace = false, bool clear = false, bool instant = false)
    {
        if (clear)
        {
            foreach (var s in _stack) s.QueueFree();
            _stack.Clear();
        }
        else if (replace && _stack.Count > 0)
        {
            _stack[^1].QueueFree();
            _stack.RemoveAt(_stack.Count - 1);
        }
        else if (_stack.Count > 0)
        {
            _stack[^1].Visible = false;
        }
        screen.Full();
        _stack.Add(screen);
        _screens.AddChild(screen);
        if (!instant) Transition();
    }

    public void Back()
    {
        if (_stack.Count == 0) return;
        if (_stack[^1].HandleBack()) return;
        if (_stack.Count == 1)
        {
            Quit();
            return;
        }
        _stack[^1].QueueFree();
        _stack.RemoveAt(_stack.Count - 1);
        _stack[^1].Visible = true;
        _stack[^1].OnResumed();
        Transition();
    }

    /// <summary>Drops every screen above the main menu.</summary>
    public void Home()
    {
        Go(new MainMenuScreen(), clear: true);
        Audio.SetAmbience(false);
        Audio.SetMusicIntensity(0);
    }

    private void Transition()
    {
        if (Settings.ReduceMotion) return;
        Audio.Play(Sfx.Whoosh, 1.2f, -16f, ui: true);
        _fade.Color = Tokens.Bg with { A = 0.85f };
        CreateTween().TweenProperty(_fade, "color:a", 0f, 0.22);
    }

    public void Quit()
    {
        Settings.Save();
        _ = Session.LeaveAsync();
        GetTree().Quit();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) Back();
        else if (what == NotificationWMCloseRequest) Quit();
        else if (what == NotificationApplicationPaused || what == NotificationWMWindowFocusOut) Settings?.Save();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            Back();
            AcceptEvent();
        }
    }

    // ------------------------------------------------------------------ toasts

    public void Toast(string text, bool error = false)
    {
        var label = Ui.Label(text, 15, error ? Tokens.Bad : Tokens.Text);
        var panel = Ui.Panel(label, Tokens.PanelHi, 12, 12, error ? Tokens.Bad : Tokens.Line);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _toasts.AddChild(panel);
        if (error) Audio.Play(Sfx.Error, 1f, -6f, ui: true);
        var tween = panel.CreateTween();
        tween.TweenInterval(2.6);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.4);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }
}
