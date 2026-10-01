using BoardEmpire.Core;
using BoardEmpire.Net;
using Game.Core.Board;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Core.State;
using Game.Protocol;
using Godot;

namespace BoardEmpire.UI;

/// <summary>Standings, awards and the story of the match.</summary>
public partial class ResultsScreen : Screen
{
    private readonly GameState _state;
    private readonly BoardDefinition _board;
    private readonly ReplayFile? _replay;
    private readonly List<string> _earned;
    private readonly bool _canRematch;

    public ResultsScreen(GameState state, BoardDefinition board, ReplayFile? replay, List<string> earned, bool canRematch)
    {
        _state = state;
        _board = board;
        _replay = replay;
        _earned = earned;
        _canRematch = canRematch;
        _state.Board = board;
    }

    protected override void Build()
    {
        var s = _state;
        var standings = Ui.VBox(8, Ui.Caption("Final standings", Tokens.Accent));
        foreach (var st in s.Standings)
        {
            var p = s.Players[st.Player];
            bool winner = s.Winners.Contains(st.Player);
            var row = Ui.HBox(10,
                Ui.Label($"#{st.Rank}", 18, winner ? Tokens.Accent : Tokens.Muted, mono: true, bold: true).MinSize(44, 0),
                new ColorRect { Color = Tokens.Player(p.Id), CustomMinimumSize = new Vector2(8, 34) },
                Ui.Label(p.Name, 18, Tokens.Text, bold: winner).Expand(),
                Ui.Label(p.Bankrupt ? "bankrupt" : Ui.Money(_board.Currency, st.NetWorth), 16, p.Bankrupt ? Tokens.Muted : Tokens.Text, mono: true));
            standings.AddChild(Ui.Panel(row, winner ? Tokens.PanelHi : Tokens.Panel, 10, 12, winner ? Tokens.Accent : Tokens.Line));
        }
        string reason = s.EndReason switch
        {
            GameEndReason.RoundLimit => "The round limit was reached; the richest player wins.",
            GameEndReason.FirstBankruptcy => "The first bankruptcy ended the match.",
            _ => "Everyone else went bankrupt.",
        };
        standings.AddChild(Ui.Wrapped(reason, 13, Tokens.Muted));

        var awards = Ui.VBox(6, Ui.Caption("Awards", Tokens.Accent));
        foreach (var award in MatchStory.Awards(s))
            awards.AddChild(Ui.HBox(8, Ui.Label(award.Title, 15, Tokens.Text, bold: true).Expand(),
                Ui.Label($"{s.Players[award.Player].Name} · {award.Detail}", 13, Tokens.Player(award.Player), mono: true)));
        foreach (string id in _earned)
        {
            var def = LocalProfile.Catalog.FirstOrDefault(c => c.Id == id);
            awards.AddChild(Ui.Label($"★ Achievement unlocked: {def.Title}", 15, Tokens.Accent, bold: true));
        }

        var story = Ui.VBox(6, Ui.Caption("Match story", Tokens.Accent));
        if (_replay != null)
        {
            foreach (var entry in MatchStory.Build(_replay))
                story.AddChild(Ui.HBox(10, Ui.Label($"TURN {entry.Turn}", 12, Tokens.Muted, mono: true).MinSize(Ui.Px(78), 0), Ui.Wrapped(entry.Text, 14)));
        }

        var buttons = Ui.HBox(10, Ui.Button(Loc.T("Main menu"), async () =>
        {
            await App.Session.LeaveAsync();
            App.Home();
        }, ButtonKind.Ghost));
        if (_replay != null) buttons.AddChild(Ui.Button("Watch replay", () => App.Go(new BoardScreen(_replay))).Expand());
        if (_canRematch && App.Session.Client != null)
        {
            buttons.AddChild(Ui.Button(Loc.T("Rematch"), async () =>
            {
                if (App.Session.Kind == SessionKind.Local)
                {
                    // Same seats and rules, fresh match.
                    var seats = s.Players.Select(p => new LocalSeat { Name = p.Name, IsBot = p.IsBot, BotLevel = BotLevel.Medium }).ToList();
                    if (await App.Session.StartLocalAsync(seats, s.Rules.Clone(), s.BoardId)) App.Go(new BoardScreen(), clear: true);
                    return;
                }
                App.Session.Client?.Send(new RequestRematch());
                App.Back();
            }, ButtonKind.Primary).Expand());
        }

        var left = Ui.VBox(14, Ui.Panel(standings), Ui.Panel(awards)).Expand(true, true);
        var right = Ui.Panel(Ui.Scroll(story)).Expand(true, true);
        Page("Results", Ui.VBox(14, Ui.HBox(16, left, right).Expand(true, true), buttons));
    }
}

public partial class SettingsScreen : Screen
{
    public override Screen Recreate() => new SettingsScreen();

    private void Apply(Action change, bool rebuild = false)
    {
        change();
        Settings.Save();
        if (rebuild) App.RefreshTheme();
    }

    protected override void Build()
    {
        var s = Settings;
        var player = Section("Player",
            Row("Name", Ui.Input(s.PlayerName, "Name", t => Apply(() => s.PlayerName = t.Trim().Length > 0 ? t.Trim() : "Player"), 16)),
            Row("Token", Ui.Options(Tokens.TokenNames, s.Token, i => Apply(() => s.Token = i))),
            Row("Language", Ui.Options(Loc.Languages.Select(l => l.Name), Math.Max(0, Array.FindIndex(Loc.Languages, l => l.Code == s.Language)),
                i => Apply(() => s.Language = Loc.Languages[i].Code, rebuild: true))));

        var display = Section("Display",
            Row("View", Ui.Options(new[] { "2D board", "3D city" }, (int)s.ViewMode, i => Apply(() => s.ViewMode = (ViewMode)i))),
            Row("Animation speed", Ui.Options(new[] { "Normal", "Fast", "Minimal" }, (int)s.AnimationSpeed, i => Apply(() => s.AnimationSpeed = (AnimationSpeed)i))),
            Row("3D quality", Ui.Options(new[] { "Low", "Medium", "High", "Ultra" }, (int)s.Quality, i => Apply(() => s.Quality = (Quality)i))));

        var audio = Section("Audio",
            Row("Master", Ui.Slider(s.MasterVolume, v => Apply(() => s.MasterVolume = v))),
            Row("Music", Ui.Slider(s.MusicVolume, v => Apply(() => s.MusicVolume = v))),
            Row("Sound effects", Ui.Slider(s.SfxVolume, v => Apply(() => s.SfxVolume = v))),
            Row("Ambient", Ui.Slider(s.AmbientVolume, v => Apply(() => s.AmbientVolume = v))),
            Row("Voice", Ui.Slider(s.VoiceVolume, v => Apply(() => s.VoiceVolume = v))),
            Ui.Toggle(Loc.T("Haptics"), s.Haptics, on => Apply(() => s.Haptics = on)));

        var access = Section("Accessibility",
            Ui.Toggle("Patterns for districts (colour-blind friendly)", s.ColorBlindPatterns, on => Apply(() => s.ColorBlindPatterns = on)),
            Ui.Toggle("Large text", s.LargeText, on => Apply(() => s.LargeText = on, rebuild: true)),
            Ui.Toggle("High contrast", s.HighContrast, on => Apply(() => s.HighContrast = on, rebuild: true)),
            Ui.Toggle("Reduce motion", s.ReduceMotion, on => Apply(() => s.ReduceMotion = on)),
            Ui.Toggle("Camera shake", s.CameraShake, on => Apply(() => s.CameraShake = on)),
            Ui.Toggle("Subtitles for events", s.Subtitles, on => Apply(() => s.Subtitles = on)));

        var grid = new GridContainer { Columns = GetViewportRect().Size.X > 900 ? 2 : 1 };
        foreach (var section in new[] { player, display, audio, access })
        {
            section.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(section);
        }
        Page("Settings", Ui.Scroll(grid));
    }
}

public partial class ProfileScreen : Screen
{
    public override Screen Recreate() => new ProfileScreen();

    protected override void Build()
    {
        var p = App.Profile;
        var stats = new GridContainer { Columns = 2 };
        void Stat(string label, string value)
        {
            stats.AddChild(Ui.Caption(label));
            stats.AddChild(Ui.Label(value, 17, Tokens.Text, mono: true, bold: true));
        }
        Stat("Games played", p.GamesPlayed.ToString());
        Stat("Wins", p.GamesPlayed > 0 ? $"{p.Wins} ({100.0 * p.Wins / p.GamesPlayed:0}%)" : "0");
        Stat("Average position", p.GamesPlayed > 0 ? p.AveragePosition.ToString("0.00") : "—");
        Stat("Properties bought", p.PropertiesBought.ToString());
        Stat("Auctions won", p.AuctionsWon.ToString());
        Stat("Trade success", p.TradesProposed > 0 ? $"{100 * p.TradeSuccessRate:0}%" : "—");
        Stat("Rent earned", p.RentEarned.ToString("N0"));
        Stat("Rent paid", p.RentPaid.ToString("N0"));
        Stat("Largest payment", p.LargestPayment.ToString("N0"));
        Stat("Most profitable", p.MostProfitableProperty.Length > 0 ? $"{p.MostProfitableProperty} ({p.MostProfitableRent:N0})" : "—");
        Stat("Bankruptcies", p.Bankruptcies.ToString());
        Stat("Time per turn", p.TurnsTaken > 0 ? $"{p.SecondsPerTurn:0} s" : "—");

        var achievements = Ui.VBox(8);
        foreach (var (id, title, description) in LocalProfile.Catalog)
        {
            bool has = p.Achievements.Contains(id);
            achievements.AddChild(Ui.Panel(Ui.HBox(10,
                Ui.Label(has ? "★" : "☆", 22, has ? Tokens.Accent : Tokens.Muted),
                Ui.VBox(0, Ui.Label(title, 16, has ? Tokens.Text : Tokens.Muted, bold: true), Ui.Label(description, 13, Tokens.Muted)).Expand()),
                has ? Tokens.PanelHi : Tokens.Panel, 10, 12, has ? Tokens.Accent : Tokens.Line));
        }

        var left = Section(Settings.PlayerName, stats).Expand(true, true);
        var right = Ui.Panel(Ui.VBox(10, Ui.Caption($"Achievements · {p.Achievements.Count}/{LocalProfile.Catalog.Length}", Tokens.Accent), Ui.Scroll(achievements))).Expand(true, true);
        Page("Profile", Ui.HBox(16, left, right));
    }
}

/// <summary>Build a ruleset from individual modules and save it as a named preset.</summary>
public partial class RulesScreen : Screen
{
    private GameRules _rules = RulePresets.Get("classic");
    private string _name = "My rules";
    private VBoxContainer _saved = null!;

    public override Screen Recreate() => new RulesScreen();

    private SpinBox Number(int value, int min, int max, int step, Action<int> set)
    {
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(Ui.Px(130), Ui.Px(42)) };
        spin.ValueChanged += v => set((int)v);
        return spin;
    }

    protected override void Build()
    {
        var r = _rules;
        var basics = Section("Economy",
            Row("Starting money", Number(r.StartingMoney, 200, 5000, 100, v => r.StartingMoney = v)),
            Row("Salary", Number(r.Salary, 0, 1000, 50, v => r.Salary = v)),
            Row("Building cost %", Number(r.HouseCostPercent, 25, 200, 25, v => r.HouseCostPercent = v)),
            Row("Starting properties", Number(r.StartingProperties, 0, 4, 1, v => r.StartingProperties = v)),
            Ui.Toggle("Double salary for landing on start", r.DoubleSalaryOnStart, on => r.DoubleSalaryOnStart = on),
            Ui.Toggle("Free parking jackpot", r.FreeParkingJackpot, on => r.FreeParkingJackpot = on),
            Ui.Toggle("Build evenly across a district", r.EvenBuild, on => r.EvenBuild = on));

        var flow = Section("Match flow",
            Row("Turn timer (seconds, 0 = off)", Number(r.TurnTimeSeconds, 0, 180, 5, v => r.TurnTimeSeconds = v)),
            Row("Round limit (0 = none)", Number(r.MaximumRounds, 0, 200, 1, v => r.MaximumRounds = v)),
            Row("Detention turns", Number(r.MaxJailTurns, 1, 5, 1, v => r.MaxJailTurns = v)),
            Ui.Toggle("End at the first bankruptcy", r.EndOnFirstBankruptcy, on => r.EndOnFirstBankruptcy = on),
            Ui.Toggle("Collect rent while detained", r.CollectRentInJail, on => r.CollectRentInJail = on),
            Ui.Toggle("Trading", r.TradingEnabled, on => r.TradingEnabled = on),
            Ui.Toggle("Auctions", r.AuctionsEnabled, on => r.AuctionsEnabled = on),
            Row("Auction type", Ui.Options(new[] { "Classic (open bids)", "Rapid (sealed bids)" }, (int)r.AuctionMode, i => r.AuctionMode = (AuctionMode)i)));

        var modules = Section("Modules",
            Ui.Toggle("Market & city events", r.MarketEventsEnabled, on => r.MarketEventsEnabled = on),
            Ui.Toggle("Secret objectives", r.SecretObjectivesEnabled, on => r.SecretObjectivesEnabled = on),
            Ui.Toggle("Contracts", r.ContractsEnabled, on => r.ContractsEnabled = on),
            Ui.Toggle("Property shares (syndicates)", r.PropertySharesEnabled, on => r.PropertySharesEnabled = on),
            Ui.Toggle("Public projects", r.PublicProjectsEnabled, on => r.PublicProjectsEnabled = on),
            Ui.Toggle("Player abilities", r.AbilitiesEnabled, on => r.AbilitiesEnabled = on),
            Ui.Toggle("Development types", r.AdvancedDevelopment, on => r.AdvancedDevelopment = on),
            Ui.Toggle("Teams", r.TeamsEnabled, on => r.TeamsEnabled = on));

        _saved = Ui.VBox(6);
        var presets = RulePresets.Ids.Select(RulePresets.Get).ToList();
        var library = Section("Presets",
            Row("Start from", Ui.Options(presets.Select(p => p.PresetName), Math.Max(0, presets.FindIndex(p => p.PresetId == r.PresetId)), i =>
            {
                _rules = presets[i].Clone();
                App.Go(new RulesScreen { _rules = _rules, _name = _name }, replace: true, instant: true);
            })),
            Row("Save as", Ui.Input(_name, "Preset name", t => _name = t, 24)),
            Ui.Button("SAVE PRESET", Save, ButtonKind.Primary),
            Ui.Caption("Your presets"),
            _saved);

        var grid = new GridContainer { Columns = GetViewportRect().Size.X > 900 ? 2 : 1 };
        foreach (var section in new[] { basics, flow, modules, library })
        {
            section.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(section);
        }
        Page("Rules", Ui.Scroll(grid));
        RedrawSaved();
    }

    private void Save()
    {
        string name = _name.Trim();
        if (name.Length == 0) return;
        var rules = _rules.Clone();
        rules.PresetId = "custom";
        rules.PresetName = name;
        Settings.Presets.RemoveAll(p => p.Name == name);
        Settings.Presets.Add(new SavedPreset { Name = name, Rules = rules });
        Settings.LastPreset = "custom:" + name;
        Settings.Save();
        App.Toast($"Saved \"{name}\"");
        RedrawSaved();
    }

    private void RedrawSaved()
    {
        foreach (var child in _saved.GetChildren()) child.QueueFree();
        if (Settings.Presets.Count == 0) _saved.AddChild(Ui.Label("None yet.", 14, Tokens.Muted));
        foreach (var preset in Settings.Presets.ToList())
        {
            var p = preset;
            _saved.AddChild(Ui.HBox(8, Ui.Label(p.Name, 15).Expand(),
                Ui.Button("Edit", () => App.Go(new RulesScreen { _rules = p.Rules.Clone(), _name = p.Name }, replace: true, instant: true), ButtonKind.Ghost),
                Ui.Button("✕", () =>
                {
                    Settings.Presets.Remove(p);
                    Settings.Save();
                    RedrawSaved();
                }, ButtonKind.Ghost)));
        }
    }
}

public partial class ReplayListScreen : Screen
{
    public override Screen Recreate() => new ReplayListScreen();

    protected override void Build()
    {
        var list = Ui.VBox(8);
        var files = GameSession.ListReplays();
        if (files.Count == 0) list.AddChild(Ui.Label("Finished matches are recorded here automatically.", 15, Tokens.Muted));
        foreach (string file in files)
        {
            string name = file;
            string label = name.Length > 15 ? $"{name[..4]}-{name[4..6]}-{name[6..8]}  {name[9..11]}:{name[11..13]}" : name;
            list.AddChild(Ui.Panel(Ui.HBox(12, Ui.Label(label, 16, Tokens.Text, mono: true).Expand(),
                Ui.Button("Watch", () =>
                {
                    var replay = GameSession.LoadReplay(name);
                    if (replay == null) App.Toast("This replay could not be opened", error: true);
                    else App.Go(new BoardScreen(replay));
                }, ButtonKind.Primary)), Tokens.PanelHi, 12, 12));
        }
        Page("Replays", Ui.Scroll(list), 800);
    }
}
