using BoardEmpire.Core;
using BoardEmpire.Net;
using BoardEmpire.Presentation;
using BoardEmpire.View2D;
using BoardEmpire.View3D;
using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Engine;
using Game.Core.Events;
using Game.Core.Replay;
using Game.Core.Rules;
using Game.Core.State;
using Game.Net;
using Game.Protocol;
using Godot;

namespace BoardEmpire.UI;

/// <summary>
/// The match itself: a board view (2D or 3D, chosen per device) under a HUD that is identical for
/// both. The HUD only ever sends commands; everything it shows comes from the display state that
/// the presenter advances event by event.
/// </summary>
public partial class BoardScreen : Screen
{
    private readonly ReplayFile? _replayFile;
    private ReplayPlayer? _replay;
    private bool _replayPlaying = true;

    private RoomClient? _client;
    private BoardPresenter _presenter = null!;
    private IGameView? _view;
    private BoardDefinition _board = null!;
    private Control _viewHost = null!;
    private BoxContainer _root = null!;
    private Control _side = null!;
    private VBoxContainer _players = null!;
    private VBoxContainer _feed = null!;
    private VBoxContainer _actions = null!;
    private Label _turnLabel = null!;
    private Label _roundLabel = null!;
    private Label _timerLabel = null!;
    private Control _dialogs = null!;
    private Control _banner = null!;
    private bool _landscape = true;
    private bool _awaiting;
    private double _awaitingSince;
    private int _activeSeat = -1;
    private int _handoverSeat = -1;
    private int _shownTrade = -1;
    private bool _resultsShown;
    private double _enteredAt;
    private int _lastTurnChime = -1;
    private readonly Game.Core.AI.BotBrain _autopilot = new(BotLevel.Medium, new Game.Core.Rng.Pcg32(4)) { Rollouts = 0 };

    public BoardScreen() { }

    public BoardScreen(ReplayFile replay) => _replayFile = replay;

    private bool IsReplay => _replayFile != null;
    private GameState? D => _presenter.Display;
    private string Currency => _board.Currency;

    protected override void Build()
    {
        _presenter = new BoardPresenter();
        _presenter.Setup(Settings);
        AddChild(_presenter);
        _presenter.EventStarting += OnEventStarting;
        _presenter.EventFinished += OnEventFinished;
        _presenter.Idle += Refresh;

        _root = new BoxContainer { MouseFilter = MouseFilterEnum.Ignore }.Full();
        _root.AddThemeConstantOverride("separation", 0);
        AddChild(_root);

        _viewHost = new Control { MouseFilter = MouseFilterEnum.Pass, ClipContents = true };
        _viewHost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _viewHost.SizeFlagsVertical = SizeFlags.ExpandFill;
        _root.AddChild(_viewHost);

        _side = BuildSidePanel();
        _root.AddChild(_side);

        _banner = new Control { MouseFilter = MouseFilterEnum.Ignore }.Full();
        AddChild(_banner);
        _dialogs = new Control { MouseFilter = MouseFilterEnum.Ignore }.Full();
        AddChild(_dialogs);

        Resized += ApplyOrientation;
        ApplyOrientation();

        _enteredAt = Time.GetTicksMsec() / 1000.0;
        App.Audio.SetAmbience(true);

        if (IsReplay)
        {
            _replay = new ReplayPlayer(_replayFile!);
            _board = _replayFile!.Board;
            CreateView(Settings.ViewMode);
            _presenter.Load(_replay.State, _board);
        }
        else
        {
            App.Session.ClientChanged += Rebind;
            App.Session.Lost += OnLost;
            Rebind();
        }
        Refresh();
    }

    public override void _ExitTree()
    {
        if (!IsReplay)
        {
            App.Session.ClientChanged -= Rebind;
            App.Session.Lost -= OnLost;
            Unbind();
        }
        OS.LowProcessorUsageMode = false;
    }

    // ------------------------------------------------------------------ session binding

    private void Unbind()
    {
        if (_client == null) return;
        _client.SnapshotLoaded -= OnSnapshot;
        _client.EventsApplied -= OnEvents;
        _client.CommandFailed -= OnCommandFailed;
        _client.ChatReceived -= OnChat;
        _client.PeerStatusChanged -= OnPeerStatus;
        _client.ReturnedToLobby -= OnReturnedToLobby;
        _client = null;
    }

    private void Rebind()
    {
        Unbind();
        _client = App.Session.Client;
        if (_client == null) return;
        _client.SnapshotLoaded += OnSnapshot;
        _client.EventsApplied += OnEvents;
        _client.CommandFailed += OnCommandFailed;
        _client.ChatReceived += OnChat;
        _client.PeerStatusChanged += OnPeerStatus;
        _client.ReturnedToLobby += OnReturnedToLobby;
        if (_client.State != null) OnSnapshot(_client.State);
    }

    private void OnSnapshot(GameState state)
    {
        _board = _client!.Board!;
        if (_view == null) CreateView(Settings.ViewMode);
        _presenter.Load(state, _board);
        _awaiting = false;
        Refresh();
    }

    private void OnEvents(IReadOnlyList<GameEvent> events)
    {
        _presenter.Enqueue(events);
        _awaiting = false;
    }

    private void OnCommandFailed(string id, string reason)
    {
        _awaiting = false;
        if (reason.Length > 0) App.Toast(reason, error: true);
        Refresh();
    }

    private void OnChat(int seat, int preset)
    {
        if (D == null || seat < 0 || seat >= D.Players.Count || !ChatPresets.IsValid(preset)) return;
        AddFeed($"{D.Players[seat].Name}: {ChatPresets.Lines[preset]}", Tokens.Player(seat));
        App.Audio.Play(Sfx.Notify, 1.3f, -10f);
    }

    private void OnPeerStatus(PeerStatus status)
    {
        if (D == null || status.Seat >= D.Players.Count) return;
        string name = D.Players[status.Seat].Name;
        App.Toast(status.Connected ? $"{name} reconnected" : $"{name} disconnected — waiting {status.ReconnectSeconds}s");
        Refresh();
    }

    private void OnReturnedToLobby() => App.Go(new LobbyScreen(), replace: true);

    private void OnLost(string reason)
    {
        App.Toast(reason, error: true);
        App.Home();
    }

    // ------------------------------------------------------------------ view

    private void CreateView(ViewMode mode)
    {
        if (_view != null)
        {
            _view.TileTapped -= OnTileTapped;
            _view.Node.QueueFree();
        }
        _view = mode == ViewMode.City3D ? new Board3DView() : new Board2DView();
        _view.Build(_board, Settings);
        _view.Node.Full();
        _viewHost.AddChild(_view.Node);
        _view.TileTapped += OnTileTapped;
        _presenter.View = _view;
        if (D != null) _view.Sync(D);
        // The flat board only repaints when something moves, so let the device idle in between.
        OS.LowProcessorUsageMode = mode == ViewMode.Flat2D;
    }

    public void SwitchView()
    {
        Settings.ViewMode = Settings.ViewMode == ViewMode.City3D ? ViewMode.Flat2D : ViewMode.City3D;
        Settings.Save();
        CreateView(Settings.ViewMode);
    }

    private void ApplyOrientation()
    {
        var size = GetViewportRect().Size;
        _landscape = size.X >= size.Y * 1.15f;
        _root.Vertical = !_landscape;
        if (_landscape)
        {
            _side.CustomMinimumSize = new Vector2(Mathf.Clamp(size.X * 0.34f, 340, 520), 0);
            _side.SizeFlagsVertical = SizeFlags.ExpandFill;
        }
        else
        {
            _side.CustomMinimumSize = new Vector2(0, Mathf.Clamp(size.Y * 0.42f, 300, 620));
            _side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
    }

    // ------------------------------------------------------------------ side panel

    private Control BuildSidePanel()
    {
        _turnLabel = Ui.Label("", 19, Tokens.Text, bold: true);
        _turnLabel.ClipText = true;
        _turnLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _roundLabel = Ui.Label("", 12, Tokens.Muted, mono: true);
        _timerLabel = Ui.Label("", 14, Tokens.Accent, mono: true, bold: true);
        var top = Ui.HBox(8, Ui.VBox(0, _roundLabel, _turnLabel).Expand(), _timerLabel,
            Ui.Button("≡", OpenMenu, ButtonKind.Secondary));

        _players = Ui.VBox(4);
        _feed = Ui.VBox(2);
        _feed.SizeFlagsVertical = SizeFlags.ExpandFill;
        var feedScroll = Ui.Scroll(_feed);
        feedScroll.CustomMinimumSize = new Vector2(0, 60);
        _actions = Ui.VBox(8);

        var column = Ui.VBox(12, top, _players, Ui.Caption("Activity"), feedScroll, _actions);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Tokens.Panel, 0, Tokens.Line, 1, 16, 14));
        panel.AddChild(column);
        return panel;
    }

    private void AddFeed(string text, Color? color = null)
    {
        var label = Ui.Wrapped(text, 13, color ?? Tokens.Muted);
        _feed.AddChild(label);
        while (_feed.GetChildCount() > 30) _feed.GetChild(0).Free();
        if (_feed.GetParent() is ScrollContainer scroll)
            Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    // ------------------------------------------------------------------ event reactions

    private void OnEventStarting(GameEvent e, GameState s)
    {
        string? line = EventText.Describe(e, s);
        if (line != null && e is not (BidPlaced or AuctionPassed or SealedBidPlaced or RoundStarted or DebtPaid))
            AddFeed(line, e is GameEnded or PlayerBankrupt or DistrictCompleted ? Tokens.Accent : null);

        var audio = App.Audio;
        switch (e)
        {
            case DiceRolled:
                audio.Play(Sfx.DiceRoll);
                break;
            case PlayerMoved move:
                audio.Play(Sfx.Step, 1f + (move.To % 5) * 0.04f, -8f);
                break;
            case PropertyPurchased:
                audio.Play(Sfx.Purchase);
                Haptics.Purchase();
                break;
            case BuildingConstructed:
                audio.Play(Sfx.Build);
                Haptics.Build();
                break;
            case RentPaid rent:
                audio.Play(Sfx.Coin, rent.Amount >= 300 ? 0.8f : 1f);
                if (rent.Amount >= 300 && IsLocal(rent.Payer)) Haptics.BigPayment();
                break;
            case MoneyTransferred or SalaryPaid or DebtPaid:
                audio.Play(Sfx.Coin, 1.1f, -5f);
                break;
            case CardDrawn card:
                audio.Play(Sfx.Card);
                Haptics.Card();
                ShowCard(card, s);
                break;
            case PlayerSentToJail:
                audio.Play(Sfx.Jail);
                break;
            case AuctionStarted:
                audio.Play(Sfx.Notify, 0.9f);
                break;
            case AuctionCompleted auction:
                audio.Play(Sfx.Gavel);
                if (IsLocal(auction.Winner)) Haptics.AuctionWin();
                break;
            case TradeProposed trade when IsLocal(trade.Offer.To):
                audio.Play(Sfx.Notify);
                break;
            case TradeAccepted:
                audio.Play(Sfx.Purchase, 1.15f);
                break;
            case EconomyChanged economy:
                ShowBanner(economy.Effect?.Name ?? "Stable Economy", economy.Effect?.Description ?? "Prices and rents return to normal", Tokens.Info);
                break;
            case CityEventOccurred city:
                ShowBanner(city.Effect.Name, city.Effect.Description, Tokens.Accent);
                break;
            case ProjectProposed project:
                ShowBanner(project.Project.Name, $"Public project seeking {Ui.Money(Currency, project.Project.Cost)}", Tokens.Good);
                break;
            case ProjectCompleted done:
                ShowBanner(done.Name, done.Effect.Description, Tokens.Good);
                audio.Play(Sfx.Fanfare, 1.2f, -6f);
                break;
            case ObjectiveCompleted:
                audio.Play(Sfx.Fanfare, 1.4f, -8f);
                break;
            case DistrictCompleted district:
                ShowBanner(s.Board.Districts[s.Board.DistrictIndex(district.District)].Name,
                    $"{s.Players[district.Player].Name} controls the whole district", Tokens.Player(district.Player));
                break;
            case PlayerBankrupt bankrupt:
                audio.Play(Sfx.Bankrupt);
                if (IsLocal(bankrupt.Player)) Haptics.Bankruptcy();
                break;
            case GameEnded:
                audio.Play(Sfx.Fanfare);
                break;
        }
    }

    private void OnEventFinished(GameEvent e, GameState s)
    {
        if (e is DiceRolled)
        {
            App.Audio.Play(Sfx.DiceLand);
            Haptics.DiceLand();
        }
        if (e is TurnStarted or PlayerBankrupt or GameEnded) UpdateMusic(s);
        if (e is TurnStarted or PhaseChanged or AuctionStarted or TradeProposed or TradeCountered or DebtIncurred or GameEnded or
            BidPlaced or AuctionPassed or SealedBidPlaced)
            Refresh();
        else RefreshPlayers();
    }

    private void UpdateMusic(GameState s)
    {
        int active = s.ActivePlayers.Count();
        int intensity = active <= 2 && s.Players.Count > 2 ? 3 : s.Players.Any(p => p.Bankrupt) || s.Round > 18 ? 2 : s.Round > 5 ? 1 : 0;
        App.Audio.SetMusicIntensity(intensity);
    }

    private bool IsLocal(int seat) => _client != null && seat >= 0 && _client.Controls(seat);

    // ------------------------------------------------------------------ refresh

    public override void _Process(double delta)
    {
        if (IsReplay)
        {
            FeedReplay();
            return;
        }
        if (_client == null) return;
        _timerLabel.Text = _client.TimerSeat >= 0 && _client.TimerSeconds > 0 ? $"⏱ {_client.TimerSeconds}s" : "";
        if (_awaiting && Time.GetTicksMsec() / 1000.0 - _awaitingSince > 6)
        {
            _awaiting = false;
            Refresh();
        }
        if (DevAutomation.Autoplay && D is { IsOver: false } && _presenter.IsIdle && !_awaiting)
        {
            CloseDialogs();
            foreach (int actor in TurnInfo.PendingActors(D))
            {
                if (!_client.Controls(actor) || D.Players[actor].IsBot) continue;
                _handoverSeat = -1;
                var command = _autopilot.Decide(D, actor) ?? Game.Core.AI.BotBrain.TimeoutAction(D, actor);
                if (command != null) Send(command);
                break;
            }
        }
        if (D is { IsOver: true } && _presenter.IsIdle && !_resultsShown)
        {
            _resultsShown = true;
            FinishMatch();
        }
    }

    private async void FinishMatch()
    {
        var s = D!;
        double seconds = Time.GetTicksMsec() / 1000.0 - _enteredAt;
        var mine = _client!.Seats.Where(seat => !s.Players[seat].IsBot).ToList();
        var earned = mine.Count > 0 ? App.Profile.Record(s, mine[0], seconds) : new List<string>();
        await ToSignal(GetTree().CreateTimer(Settings.ReduceMotion ? 0.6 : 2.4), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree() || App.Current != this) return;
        var replay = _client.InitialState != null
            ? new ReplayFile { Board = _board, Initial = _client.InitialState, Events = _client.EventLog.ToList() }
            : null;
        App.Go(new ResultsScreen(s.Clone(), _board, replay, earned, _client.IsHost || App.Session.Kind == SessionKind.Local));
    }

    private int ActiveSeat(GameState s)
    {
        if (_client == null || _client.Seats.Length == 0) return -1;
        foreach (int actor in TurnInfo.PendingActors(s))
            if (_client.Controls(actor) && !s.Players[actor].IsBot) return actor;
        return _client.Controls(_activeSeat) ? _activeSeat : _client.Seats[0];
    }

    private void RefreshPlayers()
    {
        var s = D;
        if (s == null) return;
        foreach (var child in _players.GetChildren()) child.QueueFree();
        var lobby = _client?.Lobby;
        foreach (var p in s.Players)
        {
            var color = Tokens.Player(p.Id);
            var dot = new ColorRect { Color = p.Bankrupt ? Tokens.Muted : color, CustomMinimumSize = new Vector2(6, 30) };
            var name = Ui.Label(p.Name, 15, p.Bankrupt ? Tokens.Muted : Tokens.Text, bold: p.Id == s.CurrentPlayer);
            name.ClipText = true;
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var row = Ui.HBox(6, dot, name);
            if (p.Bankrupt) row.AddChild(Ui.Chip("OUT", Tokens.Muted));
            else
            {
                if (p.IsBot) row.AddChild(Ui.Chip("BOT", Tokens.Muted));
                if (p.InJail) row.AddChild(Ui.Chip("HELD", Tokens.Bad));
                if (s.Rules.TeamsEnabled) row.AddChild(Ui.Chip("T" + (p.Team + 1), p.Team == 0 ? Tokens.Info : Tokens.Accent));
                if (lobby != null && p.Id < lobby.Seats.Count && !lobby.Seats[p.Id].Connected && !p.IsBot)
                    row.AddChild(Ui.Chip("OFFLINE", Tokens.Bad));
                int owned = s.OwnedBy(p.Id).Count();
                row.AddChild(Ui.Label($"⌂{owned}", 13, Tokens.Muted, mono: true));
                row.AddChild(Ui.Label(Ui.Money(Currency, p.Money), 15, Tokens.Text, mono: true, bold: true).MinSize(Ui.Px(74), 0));
            }
            var panel = new PanelContainer();
            bool current = p.Id == s.CurrentPlayer && !s.IsOver;
            panel.AddThemeStyleboxOverride("panel", Ui.Box(current ? Tokens.PanelHi : Colors.Transparent, 8, current ? color : Colors.Transparent, current ? 1 : 0, 6, 3));
            panel.AddChild(row);
            int id = p.Id;
            panel.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OpenPlayer(id);
            };
            _players.AddChild(panel);
        }
    }

    public void Refresh()
    {
        var s = D;
        if (s == null || !IsInsideTree()) return;
        RefreshPlayers();
        _roundLabel.Text = $"{Loc.T("Round").ToUpperInvariant()} {s.Round}" +
                           (s.Rules.MaximumRounds > 0 ? $" / {s.Rules.MaximumRounds}" : "") + $" · {s.Rules.PresetName.ToUpperInvariant()}";
        var current = s.Players[s.CurrentPlayer];
        _turnLabel.Text = s.IsOver
            ? string.Join(" & ", s.Winners.Select(w => s.Players[w].Name)) + " wins"
            : current.Name;
        _turnLabel.AddThemeColorOverride("font_color", s.IsOver ? Tokens.Accent : Tokens.Player(s.CurrentPlayer));

        foreach (var child in _actions.GetChildren()) child.QueueFree();
        if (IsReplay)
        {
            BuildReplayControls();
            return;
        }
        if (_client == null) return;

        int seat = ActiveSeat(s);
        if (seat != _activeSeat)
        {
            // Another human on this device is up: hide private information until they confirm.
            bool otherHuman = _activeSeat >= 0 && seat >= 0 && _client.Seats.Count(x => !s.Players[x].IsBot) > 1;
            _activeSeat = seat;
            if (otherHuman && TurnInfo.PendingActors(s).Contains(seat)) _handoverSeat = seat;
        }
        if (seat >= 0 && s.CurrentPlayer == seat && s.TurnNumber != _lastTurnChime && !s.IsOver)
        {
            _lastTurnChime = s.TurnNumber;
            Haptics.YourTurn();
        }

        if (s.IsOver)
        {
            _actions.AddChild(Ui.Button(Loc.T("Results"), () => _resultsShown = false, ButtonKind.Primary));
            return;
        }
        if (seat < 0)
        {
            _actions.AddChild(Ui.Label("Spectating", 15, Tokens.Muted));
            return;
        }
        if (_handoverSeat == seat)
        {
            _actions.AddChild(Ui.Label($"Pass the device to {s.Players[seat].Name}", 16, Tokens.Text, bold: true));
            _actions.AddChild(Ui.Button($"I am {s.Players[seat].Name}", () =>
            {
                _handoverSeat = -1;
                Refresh();
            }, ButtonKind.Primary));
            return;
        }

        bool ready = _presenter.IsIdle && !_awaiting;
        var incoming = s.Trades.FirstOrDefault(t => t.To == seat);
        if (incoming != null && incoming.Id != _shownTrade && ready)
        {
            _shownTrade = incoming.Id;
            if (!DevAutomation.Autoplay) OpenTradeReview(incoming);
        }
        if (incoming != null)
        {
            var offer = incoming;
            _actions.AddChild(Ui.Button($"Review offer from {s.Players[offer.From].Name}", () => OpenTradeReview(offer), ButtonKind.Primary));
        }
        BuildActions(s, seat, ready);
    }

    private void Send(GameCommand command)
    {
        if (_client == null) return;
        _client.SendCommand(command);
        _awaiting = true;
        _awaitingSince = Time.GetTicksMsec() / 1000.0;
        Refresh();
    }

    private Button Act(string text, GameCommand command, ButtonKind kind = ButtonKind.Secondary, bool enabled = true)
    {
        var button = Ui.Button(text, () => Send(command), kind);
        button.Disabled = !enabled;
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return button;
    }

    private void BuildActions(GameState s, int seat, bool ready)
    {
        var me = s.Players[seat];
        var primary = Ui.HBox(8);
        var secondary = Ui.HBox(8);
        string status = "";

        if (s.Phase == TurnPhase.Auction && s.Auction != null)
        {
            BuildAuction(s, seat, ready);
            return;
        }

        bool myTurn = s.CurrentPlayer == seat;
        var debt = s.Debts.Count > 0 ? s.Debts[0] : null;
        if (s.Phase == TurnPhase.DebtResolution && debt != null)
        {
            if (debt.Debtor == seat)
            {
                string creditor = debt.Creditor >= 0 ? s.Players[debt.Creditor].Name : Loc.T("Bank");
                status = $"You owe {Ui.Money(Currency, debt.Amount)} to {creditor}. Raise {Ui.Money(Currency, debt.Amount - me.Money)} more.";
                primary.AddChild(Ui.Button(Loc.T("Portfolio"), OpenPortfolio, ButtonKind.Primary).Expand());
                if (s.Rules.TradingEnabled) primary.AddChild(Ui.Button(Loc.T("Trade"), () => OpenTradeComposer(-1, null), ButtonKind.Secondary).Expand());
                secondary.AddChild(Ui.Button(Loc.T("Declare bankruptcy"), () => Confirm("Give up everything and leave the match?",
                    () => Send(new DeclareBankruptcyCommand(seat))), ButtonKind.Danger).Expand());
            }
            else
            {
                status = $"{s.Players[debt.Debtor].Name} is raising {Ui.Money(Currency, debt.Amount)}…";
            }
        }
        else if (!myTurn)
        {
            status = $"{Loc.T("Waiting for other players")} — {s.Players[s.CurrentPlayer].Name}";
            if (s.Rules.TradingEnabled) secondary.AddChild(Ui.Button(Loc.T("Trade"), () => OpenTradeComposer(-1, null)).Expand());
            secondary.AddChild(Ui.Button(Loc.T("Portfolio"), OpenPortfolio).Expand());
        }
        else
        {
            switch (s.Phase)
            {
                case TurnPhase.PreRoll:
                    status = me.InJail ? $"In detention — attempt {me.JailTurns + 1} of {s.Rules.MaxJailTurns}" : Loc.T("Your turn");
                    primary.AddChild(Act(Loc.T("Roll"), new RollDiceCommand(seat), ButtonKind.Primary, ready));
                    if (me.InJail)
                    {
                        primary.AddChild(Act($"{Loc.T("Pay fine")} {Ui.Money(Currency, _board.JailFine)}", new PayJailFineCommand(seat),
                            ButtonKind.Secondary, ready && me.Money >= _board.JailFine));
                        if (me.JailCards > 0) primary.AddChild(Act(Loc.T("Use card"), new UseJailCardCommand(seat), ButtonKind.Secondary, ready));
                    }
                    break;
                case TurnPhase.BuyDecision:
                    int price = Calc.PurchasePrice(s, me.Position);
                    status = $"{_board.Tiles[me.Position].Name} is for sale";
                    primary.AddChild(Act($"{Loc.T("Buy")} {Ui.Money(Currency, price)}", new BuyPropertyCommand(seat), ButtonKind.Primary, ready && me.Money >= price));
                    primary.AddChild(Act(s.Rules.AuctionsEnabled ? Loc.T("Auction") : Loc.T("Pass"), new DeclinePropertyCommand(seat), ButtonKind.Secondary, ready));
                    break;
                case TurnPhase.PostRoll:
                    status = Loc.T("Your turn");
                    primary.AddChild(Act(Loc.T("End turn"), new EndTurnCommand(seat), ButtonKind.Primary, ready));
                    break;
            }
            secondary.AddChild(Ui.Button(Loc.T("Portfolio"), OpenPortfolio).Expand());
            if (s.Rules.TradingEnabled) secondary.AddChild(Ui.Button(Loc.T("Trade"), () => OpenTradeComposer(-1, null)).Expand());
            if (s.Phase != TurnPhase.BuyDecision)
            {
                if (s.Rules.AbilitiesEnabled && me.Ability is Ability.Investor or Ability.Negotiator && !me.AbilityUsed)
                    secondary.AddChild(Act(me.Ability.ToString(), new UseAbilityCommand(seat), ButtonKind.Secondary, ready));
                if (s.Project != null) secondary.AddChild(Ui.Button("Project", OpenProject).Expand());
            }
        }

        if (status.Length > 0) _actions.AddChild(Ui.Wrapped(status, 15, Tokens.Muted));
        if (primary.GetChildCount() > 0) _actions.AddChild(primary);
        else primary.QueueFree();
        if (secondary.GetChildCount() > 0) _actions.AddChild(secondary);
        else secondary.QueueFree();
    }

    private void BuildAuction(GameState s, int seat, bool ready)
    {
        var a = s.Auction!;
        var me = s.Players[seat];
        _actions.AddChild(Ui.Caption($"Auction · {_board.Tiles[a.Tile].Name}", Tokens.Accent));
        if (a.Mode == AuctionMode.Rapid)
        {
            var who = string.Join(", ", a.Participants.Select(id => s.Players[id].Name + (a.HasSubmitted(id) ? " ✓" : " …")));
            _actions.AddChild(Ui.Wrapped(who, 13, Tokens.Muted));
            // On a shared device every local participant gets a turn to bid in secret.
            int bidder = _client!.Seats.FirstOrDefault(x => a.Participants.Contains(x) && !a.HasSubmitted(x) && !s.Players[x].IsBot, -1);
            if (bidder < 0)
            {
                _actions.AddChild(Ui.Label("Bid sealed. Waiting for the reveal…", 15, Tokens.Muted));
                return;
            }
            var bidderState = s.Players[bidder];
            var spin = new SpinBox
            {
                MinValue = 0, MaxValue = bidderState.Money, Step = 5, Value = Math.Min(bidderState.Money, _board.Tiles[a.Tile].Price / 2),
                CustomMinimumSize = new Vector2(Ui.Px(150), Ui.Px(46)),
            };
            _actions.AddChild(Ui.Label($"{bidderState.Name}: secret bid (0 = no bid)", 15, Tokens.Player(bidder), bold: true));
            var submit = Ui.Button("Seal bid", () => Send(new SubmitSealedBidCommand(bidder, (int)spin.Value)), ButtonKind.Primary);
            submit.Disabled = !ready;
            _actions.AddChild(Ui.HBox(8, spin, submit.Expand()));
            return;
        }

        string leader = a.HighBidder >= 0 ? $"{s.Players[a.HighBidder].Name} leads with {Ui.Money(Currency, a.HighBid)}" : "No bids yet";
        _actions.AddChild(Ui.Label(leader, 16, a.HighBidder >= 0 ? Tokens.Player(a.HighBidder) : Tokens.Muted, bold: true));
        int actor = _client!.Seats.FirstOrDefault(x => a.Participants.Contains(x) && !a.Passed.Contains(x) && a.HighBidder != x && !s.Players[x].IsBot, -1);
        if (actor < 0)
        {
            _actions.AddChild(Ui.Label(a.HighBidder == seat ? "You hold the high bid" : "You are out of this auction", 14, Tokens.Muted));
            return;
        }
        var actorState = s.Players[actor];
        int min = a.HighBidder < 0 ? s.Rules.AuctionMinIncrement : a.HighBid + s.Rules.AuctionMinIncrement;
        if (_client.Seats.Length > 1) _actions.AddChild(Ui.Label($"{actorState.Name}'s bid", 14, Tokens.Player(actor), bold: true));
        var row = Ui.HBox(6);
        foreach (int raise in new[] { 0, 40, 90 })
        {
            int amount = min + raise;
            row.AddChild(Act(Ui.Money(Currency, amount), new PlaceBidCommand(actor, amount), raise == 0 ? ButtonKind.Primary : ButtonKind.Secondary,
                ready && amount <= actorState.Money));
        }
        _actions.AddChild(row);
        _actions.AddChild(Act(Loc.T("Pass"), new PassAuctionCommand(actor), ButtonKind.Ghost, ready));
    }

    // ------------------------------------------------------------------ replay controls

    private void FeedReplay()
    {
        if (_replay == null || !_replayPlaying || _presenter.Backlog > 1) return;
        var e = _replay.Step();
        if (e != null) _presenter.Enqueue(new[] { e });
    }

    private void BuildReplayControls()
    {
        var replay = _replay!;
        _actions.AddChild(Ui.Caption($"Replay · event {replay.Position} / {replay.Length}", Tokens.Accent));
        var transport = Ui.HBox(6,
            Ui.Button("⏮", () => ReplayJump(1)),
            Ui.Button(_replayPlaying ? "⏸" : "▶", () =>
            {
                _replayPlaying = !_replayPlaying;
                Refresh();
            }, ButtonKind.Primary).Expand(),
            Ui.Button("⏭ turn", () => ReplayJump((D?.TurnNumber ?? 0) + 1)));
        _actions.AddChild(transport);
        var speeds = Ui.HBox(6);
        foreach (float speed in new[] { 0.5f, 1f, 2f, 4f })
        {
            bool on = Mathf.IsEqualApprox(_presenter.SpeedOverride <= 0 ? 1f : _presenter.SpeedOverride, speed);
            speeds.AddChild(Ui.Button($"{speed:0.#}×", () =>
            {
                _presenter.SpeedOverride = speed;
                Refresh();
            }, on ? ButtonKind.Primary : ButtonKind.Secondary).Expand());
        }
        _actions.AddChild(speeds);
        int lastTurn = _replayFile!.Events.OfType<TurnStarted>().LastOrDefault()?.TurnNumber ?? 1;
        var slider = Ui.Slider(D?.TurnNumber ?? 1, _ => { }, 1, Math.Max(2, lastTurn), 1);
        slider.DragEnded += changed =>
        {
            if (changed) ReplayJump((int)slider.Value);
        };
        _actions.AddChild(Row("Jump to turn", slider));
    }

    private void ReplayJump(int turn)
    {
        var replay = _replay!;
        _presenter.Clear();
        replay.JumpToTurn(Math.Max(1, turn));
        _presenter.Load(replay.State, _board);
        Refresh();
    }

    /// <summary>Returning from the settings screen: pick up view mode, quality and accessibility changes.</summary>
    public override void OnResumed()
    {
        if (_view == null) return;
        bool is3D = _view is Board3DView;
        if (is3D != (Settings.ViewMode == ViewMode.City3D)) CreateView(Settings.ViewMode);
        else _view.ApplySettings(Settings);
        Refresh();
    }

    public override bool HandleBack()
    {
        if (_dialogs.GetChildCount() > 0)
        {
            foreach (var child in _dialogs.GetChildren())
                if (child is Modal modal) modal.Close();
            return true;
        }
        if (IsReplay) return false;
        OpenMenu();
        return true;
    }
}
