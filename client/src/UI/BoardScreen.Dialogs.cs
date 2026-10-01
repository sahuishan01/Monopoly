using BoardEmpire.Core;
using BoardEmpire.Net;
using Game.Core.Board;
using Game.Core.Commands;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;
using Game.Protocol;
using Godot;

namespace BoardEmpire.UI;

public partial class BoardScreen
{
    private Modal Show(Control content, int width = 560, bool dismissable = true, Action? onClose = null)
    {
        var modal = new Modal(content, onClose, dismissable, width);
        _dialogs.AddChild(modal);
        return modal;
    }

    private void CloseDialogs()
    {
        foreach (var child in _dialogs.GetChildren())
            if (child is Modal modal) modal.Close();
    }

    private void Confirm(string question, Action yes)
    {
        Modal? modal = null;
        var content = Ui.VBox(16, Ui.Wrapped(question, 18),
            Ui.HBox(10,
                Ui.Button("Cancel", () => modal!.Close(), ButtonKind.Ghost).Expand(),
                Ui.Button("Confirm", () =>
                {
                    modal!.Close();
                    yes();
                }, ButtonKind.Danger).Expand()));
        modal = Show(content, 440);
    }

    // ------------------------------------------------------------------ banners and cards

    private void ShowBanner(string title, string text, Color color)
    {
        var box = Ui.VBox(2, Ui.Label(title.ToUpperInvariant(), 20, color, mono: true, bold: true), Ui.Label(text, 15, Tokens.Text));
        var panel = Ui.Panel(box, Tokens.BgDeep with { A = 0.94f }, 18, 14, color);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        panel.GrowHorizontal = GrowDirection.Both;
        panel.OffsetTop = 70;
        _banner.AddChild(panel);
        var tween = panel.CreateTween();
        if (!Settings.ReduceMotion)
        {
            panel.Modulate = panel.Modulate with { A = 0 };
            tween.TweenProperty(panel, "modulate:a", 1f, 0.2);
        }
        tween.TweenInterval(2.6);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.4);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }

    private void ShowCard(CardDrawn drawn, GameState s)
    {
        var card = (drawn.Deck == TileType.Fortune ? _board.FortuneCards : _board.CivicCards)[drawn.CardIndex];
        var color = drawn.Deck == TileType.Fortune ? Tokens.Accent : Tokens.Info;
        string deck = _board.Tiles.First(t => t.Type == drawn.Deck).Name;
        var box = Ui.VBox(10,
            Ui.Caption(deck, color),
            Ui.Wrapped(card.Text, 19),
            Ui.Label(s.Players[drawn.Player].Name, 13, Tokens.Player(drawn.Player), mono: true));
        box.CustomMinimumSize = new Vector2(Ui.Px(320), 0);
        var panel = Ui.Panel(box, Tokens.Panel, 22, 18, color);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }.Full();
        center.AddChild(panel);
        _banner.AddChild(center);
        var tween = center.CreateTween();
        if (!Settings.ReduceMotion)
        {
            panel.Scale = new Vector2(0.8f, 0.8f);
            tween.TweenProperty(panel, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        tween.TweenInterval(Settings.AnimationSpeed == AnimationSpeed.Normal ? 1.7 : 0.9);
        tween.TweenProperty(center, "modulate:a", 0f, 0.25);
        tween.TweenCallback(Callable.From(center.QueueFree));
    }

    // ------------------------------------------------------------------ menu

    private void OpenMenu()
    {
        Modal? modal = null;
        var content = Ui.VBox(12, Ui.Caption("Match menu", Tokens.Accent));
        content.AddChild(Ui.Button(Settings.ViewMode == ViewMode.City3D ? "Switch to 2D board" : "Switch to 3D city", () =>
        {
            modal!.Close();
            SwitchView();
        }));
        content.AddChild(Row("Animation speed", Ui.Options(new[] { "Normal", "Fast", "Minimal" }, (int)Settings.AnimationSpeed, i =>
        {
            Settings.AnimationSpeed = (AnimationSpeed)i;
            Settings.Save();
        })));
        content.AddChild(Ui.Button(Loc.T("Settings"), () =>
        {
            modal!.Close();
            App.Go(new SettingsScreen());
        }));
        if (!IsReplay && _client is { Seats.Length: > 0 } && App.Session.Kind != SessionKind.Local)
        {
            content.AddChild(Ui.Caption("Quick chat"));
            var grid = new GridContainer { Columns = 4 };
            for (int i = 0; i < ChatPresets.Lines.Length; i++)
            {
                int id = i;
                grid.AddChild(Ui.Button(ChatPresets.Lines[i], () =>
                {
                    _client?.Chat(id);
                    modal!.Close();
                }, ButtonKind.Ghost));
            }
            content.AddChild(grid);
        }
        content.AddChild(Ui.Button(IsReplay ? "Close replay" : "Leave match", () =>
        {
            modal!.Close();
            if (IsReplay)
            {
                App.Back();
                return;
            }
            Confirm(App.Session.Kind == SessionKind.Local ? "Leave? The match is saved and can be continued later." : "Leave the match?", Leave);
        }, ButtonKind.Danger));
        content.AddChild(Ui.Button("Resume", () => modal!.Close(), ButtonKind.Primary));
        modal = Show(content, 460);
    }

    private async void Leave()
    {
        await App.Session.LeaveAsync();
        App.Home();
    }

    // ------------------------------------------------------------------ players

    private void OpenPlayer(int id)
    {
        var s = D;
        if (s == null) return;
        var p = s.Players[id];
        Modal? modal = null;
        var content = Ui.VBox(10,
            Ui.HBox(10, Ui.Label(p.Name, 22, Tokens.Player(id), bold: true), Ui.Spacer(),
                Ui.Label(Ui.Money(Currency, p.Money), 20, Tokens.Text, mono: true, bold: true)));
        var facts = new GridContainer { Columns = 2 };
        void Fact(string label, string value)
        {
            facts.AddChild(Ui.Caption(label));
            facts.AddChild(Ui.Label(value, 15, Tokens.Text, mono: true));
        }
        Fact("Net worth", Ui.Money(Currency, Calc.NetWorth(s, id)));
        Fact("Properties", s.OwnedBy(id).Count().ToString());
        Fact("Rent earned", Ui.Money(Currency, p.Stats.RentEarned));
        Fact("Rent paid", Ui.Money(Currency, p.Stats.RentPaid));
        if (p.JailCards > 0) Fact("Release cards", p.JailCards.ToString());
        if (s.Rules.AbilitiesEnabled) Fact("Ability", p.Ability + (p.AbilityUsed ? " (used)" : ""));
        if (s.Rules.TeamsEnabled) Fact("Team", (p.Team + 1).ToString());
        content.AddChild(facts);

        if (s.Rules.SecretObjectivesEnabled && p.Objectives.Count > 0)
        {
            content.AddChild(Ui.Caption("Secret objectives", Tokens.Accent));
            bool mine = IsLocal(id) && _handoverSeat < 0 && id == _activeSeat;
            foreach (var o in p.Objectives)
            {
                string text = o.Id >= 0 && (mine || o.Completed) ? ObjectiveCatalog.Get(o.Id).Text : "Hidden objective";
                content.AddChild(Ui.Wrapped((o.Completed ? "✓ " : "· ") + text, 14, o.Completed ? Tokens.Good : Tokens.Text));
            }
        }

        var contracts = s.Contracts.Where(c => c.Grantor == id || c.Beneficiary == id).ToList();
        if (contracts.Count > 0)
        {
            content.AddChild(Ui.Caption("Contracts", Tokens.Accent));
            foreach (var c in contracts) content.AddChild(Ui.Wrapped("· " + DescribeContract(s, c), 14));
        }

        var buttons = Ui.HBox(10, Ui.Button("Close", () => modal!.Close(), ButtonKind.Ghost).Expand());
        int seat = _activeSeat;
        if (!IsReplay && seat >= 0 && seat != id && !p.Bankrupt && s.Rules.TradingEnabled)
            buttons.AddChild(Ui.Button(Loc.T("Trade"), () =>
            {
                modal!.Close();
                OpenTradeComposer(id, null);
            }, ButtonKind.Primary).Expand());
        if (!IsReplay && seat >= 0 && seat != id && s.Rules.TeamsEnabled && s.SameTeam(seat, id) && !p.Bankrupt)
            buttons.AddChild(Ui.Button("Send 100", () =>
            {
                modal!.Close();
                Send(new TransferMoneyCommand(seat, id, Math.Min(100, s.Players[seat].Money)));
            }).Expand());
        content.AddChild(buttons);
        modal = Show(content, 480);
    }

    private string DescribeContract(GameState s, Contract c)
    {
        string from = s.Players[c.Grantor].Name, to = s.Players[c.Beneficiary].Name;
        string tile = c.Tile >= 0 ? _board.Tiles[c.Tile].Name : "any property";
        return c.Kind switch
        {
            ContractKind.RentImmunity => $"{to} pays no rent at {tile} ({from}) for {c.Remaining} more visit(s)",
            ContractKind.RevenueShare => $"{to} receives {c.Percent}% of the rent of {tile} for {c.Remaining} round(s)",
            ContractKind.Installment => $"{from} pays {to} {Ui.Money(Currency, c.Amount)} for {c.Remaining} more round(s)",
            _ => $"{to} may buy {tile} from {from} for {Ui.Money(Currency, c.Amount)} ({c.Remaining} round(s) left)",
        };
    }

    // ------------------------------------------------------------------ property

    private void OnTileTapped(int tile)
    {
        var s = D;
        if (s == null) return;
        var def = _board.Tiles[tile];
        _view?.Focus(tile);
        Modal? modal = null;
        var content = Ui.VBox(10);
        var header = Ui.HBox(10, Ui.Label(def.Name, 22, Tokens.Text, bold: true).Expand());
        if (def.District != null)
        {
            var district = _board.Districts[_board.DistrictIndex(def.District)];
            header.AddChild(Ui.Chip(district.Name.ToUpperInvariant(), new Color(district.Color)));
        }
        content.AddChild(header);

        var prop = s.Property(tile);
        if (prop == null)
        {
            string about = def.Type switch
            {
                TileType.Start => $"Collect {Ui.Money(Currency, s.Rules.Salary)} every time you pass.",
                TileType.Tax => $"Pay {Ui.Money(Currency, Calc.TaxAmount(s, tile))} to the bank.",
                TileType.Jail => "Just visiting — unless you were sent here.",
                TileType.GoToJail => "Go directly to detention.",
                TileType.Plaza => s.Rules.FreeParkingJackpot ? $"Collect the jackpot: {Ui.Money(Currency, s.Bank.Jackpot)}." : "A quiet place to rest.",
                _ => "Draw a card and do what it says.",
            };
            content.AddChild(Ui.Wrapped(about, 16, Tokens.Muted));
        }
        else
        {
            var facts = new GridContainer { Columns = 2 };
            void Fact(string label, string value, Color? color = null)
            {
                facts.AddChild(Ui.Caption(label));
                facts.AddChild(Ui.Label(value, 15, color ?? Tokens.Text, mono: true));
            }
            Fact(Loc.T("Owner"), prop.Owner >= 0 ? s.Players[prop.Owner].Name : Loc.T("Bank"), prop.Owner >= 0 ? Tokens.Player(prop.Owner) : Tokens.Muted);
            Fact(Loc.T("Price"), Ui.Money(Currency, Calc.PurchasePrice(s, tile)));
            Fact(Loc.T("Mortgage"), Ui.Money(Currency, Calc.MortgageValue(s, tile)) + (prop.Mortgaged ? " (active)" : ""));
            if (prop.Shares.Count > 1)
                Fact("Shares", string.Join(", ", prop.Shares.Select(x => $"{s.Players[x.Player].Name} {x.Percent}%")));
            if (prop.RentCollected > 0) Fact("Rent collected", Ui.Money(Currency, prop.RentCollected));
            content.AddChild(facts);

            if (def.Type == TileType.Street)
            {
                content.AddChild(Ui.Caption($"{Loc.T("Rent")} · building costs {Ui.Money(Currency, def.HouseCost * s.Rules.HouseCostPercent / 100)}"));
                var rents = new GridContainer { Columns = 6 };
                string[] heads = { "LAND", "1", "2", "3", "4", "★" };
                for (int level = 0; level < 6; level++)
                {
                    bool now = prop.Owner >= 0 && prop.Level == level;
                    var cell = Ui.VBox(0, Ui.Label(heads[level], 11, Tokens.Muted, mono: true),
                        Ui.Label(def.Rents[level].ToString(), 15, now ? Tokens.Accent : Tokens.Text, mono: true, bold: now));
                    rents.AddChild(Ui.Panel(cell, now ? Tokens.PanelHi : Tokens.BgDeep, 8, 8, now ? Tokens.Accent : Tokens.Line).Expand());
                }
                content.AddChild(rents);
                if (s.Rules.AdvancedDevelopment && prop.Level > 0) content.AddChild(Ui.Label($"Development: {prop.DevType}", 14, Tokens.Info));
            }
            else if (def.Type == TileType.Transit)
            {
                content.AddChild(Ui.Wrapped("Rent by lines owned: " + string.Join(" / ", _board.TransitRents.Select(r => Ui.Money(Currency, r))), 14, Tokens.Muted));
            }
            else
            {
                content.AddChild(Ui.Wrapped("Rent is the dice roll times " + string.Join(" or ", _board.UtilityMultipliers) + ", depending on utilities owned.", 14, Tokens.Muted));
            }
            int now7 = Calc.Rent(s, tile, 7);
            if (prop.Owner >= 0 && !prop.Mortgaged) content.AddChild(Ui.Label($"Current rent: {Ui.Money(Currency, now7)}", 16, Tokens.Accent, mono: true, bold: true));
            foreach (var effect in s.Effects.Where(e => Calc.EffectApplies(s, e, tile)))
                content.AddChild(Ui.Wrapped($"◆ {effect.Name}: {effect.Description}", 13, Tokens.Info));

            int seat = _activeSeat;
            if (!IsReplay && seat >= 0 && prop.Owner == seat && _handoverSeat < 0)
            {
                var actions = Ui.HBox(8);
                var type = DevelopmentType.Residential;
                if (def.Type == TileType.Street)
                {
                    if (s.Rules.AdvancedDevelopment && prop.Level == 0)
                    {
                        content.AddChild(Row("Development type", Ui.Options(
                            new[] { "Residential", "Commercial (+cost, +rent)", "Luxury (volatile)", "Industrial (cheap)" }, 0, i => type = (DevelopmentType)i)));
                    }
                    string? buildError = Calc.BuildError(s, seat, tile);
                    int cost = Calc.BuildCost(s, tile, seat, prop.Level > 0 ? prop.DevType : DevelopmentType.Residential);
                    var build = Ui.Button($"{Loc.T("Build")} {Ui.Money(Currency, cost)}", () =>
                    {
                        modal!.Close();
                        Send(new BuildHouseCommand(seat, tile, type));
                    }, ButtonKind.Primary);
                    build.Disabled = buildError != null;
                    build.TooltipText = buildError ?? "";
                    actions.AddChild(build.Expand());
                    if (prop.Level > 0)
                    {
                        var sell = Ui.Button($"{Loc.T("Sell")} +{Ui.Money(Currency, Calc.SellRefund(s, tile))}", () =>
                        {
                            modal!.Close();
                            Send(new SellHouseCommand(seat, tile));
                        });
                        sell.Disabled = Calc.SellError(s, seat, tile) != null;
                        actions.AddChild(sell.Expand());
                    }
                    if (buildError != null && prop.Level < 5) content.AddChild(Ui.Wrapped(buildError, 13, Tokens.Muted));
                }
                if (prop.Mortgaged)
                {
                    int cost = Calc.UnmortgageCost(s, tile, seat);
                    var lift = Ui.Button($"{Loc.T("Unmortgage")} {Ui.Money(Currency, cost)}", () =>
                    {
                        modal!.Close();
                        Send(new UnmortgageCommand(seat, tile));
                    });
                    lift.Disabled = s.Players[seat].Money < cost;
                    actions.AddChild(lift.Expand());
                }
                else
                {
                    var mortgage = Ui.Button($"{Loc.T("Mortgage")} +{Ui.Money(Currency, Calc.MortgageValue(s, tile))}", () =>
                    {
                        modal!.Close();
                        Send(new MortgageCommand(seat, tile));
                    });
                    mortgage.Disabled = Calc.MortgageError(s, seat, tile) != null;
                    actions.AddChild(mortgage.Expand());
                }
                content.AddChild(actions);
            }
            foreach (var c in s.Contracts.Where(c => c.Kind == ContractKind.BuyOption && c.Tile == tile && c.Beneficiary == _activeSeat))
            {
                int id = c.Id;
                content.AddChild(Ui.Button($"Exercise option for {Ui.Money(Currency, c.Amount)}", () =>
                {
                    modal!.Close();
                    Send(new ExerciseOptionCommand(_activeSeat, id));
                }, ButtonKind.Primary));
            }
        }
        content.AddChild(Ui.Button("Close", () => modal!.Close(), ButtonKind.Ghost));
        modal = Show(content, 520, onClose: () => _view?.Focus(-1));
    }

    // ------------------------------------------------------------------ portfolio

    private void OpenPortfolio()
    {
        var s = D;
        int seat = _activeSeat;
        if (s == null || seat < 0) return;
        Modal? modal = null;
        var list = Ui.VBox(6);
        foreach (var prop in s.OwnedBy(seat).OrderBy(p => p.Tile))
        {
            var def = _board.Tiles[prop.Tile];
            int tile = prop.Tile;
            var swatch = new ColorRect
            {
                Color = def.District != null ? new Color(_board.Districts[_board.DistrictIndex(def.District)].Color) : Tokens.Muted,
                CustomMinimumSize = new Vector2(8, 34),
            };
            string state = prop.Mortgaged ? "mortgaged" : prop.Level == 5 ? "landmark" : prop.Level > 0 ? $"{prop.Level} building(s)" : "";
            var row = Ui.HBox(8, swatch,
                Ui.VBox(0, Ui.Label(def.Name, 15, prop.Mortgaged ? Tokens.Muted : Tokens.Text), Ui.Label(state, 11, Tokens.Muted, mono: true)).Expand(),
                Ui.Label(Ui.Money(Currency, Calc.Rent(s, tile, 7)), 14, Tokens.Accent, mono: true),
                Ui.Button("Open", () =>
                {
                    modal!.Close();
                    OnTileTapped(tile);
                }, ButtonKind.Ghost));
            list.AddChild(row);
        }
        if (list.GetChildCount() == 0) list.AddChild(Ui.Label("You do not own any property yet.", 15, Tokens.Muted));

        var me = s.Players[seat];
        var content = Ui.VBox(12,
            Ui.HBox(10, Ui.Caption(Loc.T("Portfolio"), Tokens.Accent), Ui.Spacer(),
                Ui.Label($"Net worth {Ui.Money(Currency, Calc.NetWorth(s, seat))}", 14, Tokens.Text, mono: true)),
            Ui.Scroll(list).MinSize(0, 300));
        if (s.Rules.SecretObjectivesEnabled && me.Objectives.Count > 0)
        {
            content.AddChild(Ui.Caption("Secret objectives", Tokens.Accent));
            foreach (var o in me.Objectives.Where(o => o.Id >= 0))
            {
                var def = ObjectiveCatalog.Get(o.Id);
                content.AddChild(Ui.Wrapped($"{(o.Completed ? "✓" : "·")} {def.Text}  (+{Ui.Money(Currency, def.Reward)})", 14, o.Completed ? Tokens.Good : Tokens.Text));
            }
        }
        content.AddChild(Ui.Button("Close", () => modal!.Close(), ButtonKind.Ghost));
        modal = Show(content, 560);
    }

    // ------------------------------------------------------------------ public project

    private void OpenProject()
    {
        var s = D;
        int seat = _activeSeat;
        if (s?.Project == null || seat < 0) return;
        var project = s.Project;
        Modal? modal = null;
        int missing = project.Cost - project.Funded;
        var spin = new SpinBox { MinValue = 10, MaxValue = Math.Max(10, Math.Min(missing, s.Players[seat].Money)), Step = 10, Value = Math.Min(100, missing) };
        var bar = new ProgressBar { MinValue = 0, MaxValue = project.Cost, Value = project.Funded, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) };
        string side = new[] { "bottom", "left", "top", "right" }[Math.Clamp(project.Side, 0, 3)];
        var content = Ui.VBox(12,
            Ui.Caption("Public project", Tokens.Good),
            Ui.Label(project.Name, 22, Tokens.Text, bold: true),
            Ui.Wrapped($"When funded, properties on the {side} side earn +{project.RentPercent}% rent for {project.EffectRounds} rounds. " +
                       $"The biggest contributor receives {Ui.Money(Currency, project.Cost / 10)}. {project.RoundsLeft} round(s) left.", 15, Tokens.Muted),
            bar,
            Ui.Label($"{Ui.Money(Currency, project.Funded)} of {Ui.Money(Currency, project.Cost)}", 14, Tokens.Text, mono: true));
        foreach (var c in project.Contributions)
            content.AddChild(Ui.Label($"{s.Players[c.Player].Name}: {Ui.Money(Currency, c.Amount)}", 13, Tokens.Player(c.Player), mono: true));
        content.AddChild(Ui.HBox(10, spin,
            Ui.Button("Contribute", () =>
            {
                modal!.Close();
                Send(new ContributeToProjectCommand(seat, (int)spin.Value));
            }, ButtonKind.Primary).Expand(),
            Ui.Button("Close", () => modal!.Close(), ButtonKind.Ghost)));
        modal = Show(content, 500);
    }

    // ------------------------------------------------------------------ trades

    private string DescribeSide(GameState s, TradeSide side)
    {
        var parts = new List<string>();
        if (side.Money > 0) parts.Add(Ui.Money(Currency, side.Money));
        parts.AddRange(side.Tiles.Select(t => _board.Tiles[t].Name));
        if (side.JailCards > 0) parts.Add($"{side.JailCards} release card(s)");
        parts.AddRange(side.Shares.Select(x => $"{x.Percent}% of {_board.Tiles[x.Tile].Name}"));
        foreach (var t in side.Terms)
        {
            string tile = t.Tile >= 0 ? _board.Tiles[t.Tile].Name : "all properties";
            parts.Add(t.Kind switch
            {
                ContractKind.RentImmunity => $"rent immunity at {tile} for {t.Count} visit(s)",
                ContractKind.RevenueShare => $"{t.Percent}% of the rent from {tile} for {t.Count} round(s)",
                ContractKind.Installment => $"{Ui.Money(Currency, t.Amount)} per round for {t.Count} round(s)",
                _ => $"option to buy {tile} for {Ui.Money(Currency, t.Amount)} within {t.Count} round(s)",
            });
        }
        return parts.Count > 0 ? string.Join("\n", parts.Select(p => "· " + p)) : "· nothing";
    }

    private void OpenTradeReview(TradeOffer offer)
    {
        var s = D;
        if (s == null) return;
        Modal? modal = null;
        int me = offer.To;
        var content = Ui.VBox(12,
            Ui.Caption("Trade offer", Tokens.Accent),
            Ui.Label($"{s.Players[offer.From].Name} → {s.Players[me].Name}", 20, Tokens.Player(offer.From), bold: true),
            Ui.HBox(14,
                Ui.Panel(Ui.VBox(6, Ui.Caption("You receive", Tokens.Good), Ui.Wrapped(DescribeSide(s, offer.Give), 15)), Tokens.BgDeep, 12).Expand(),
                Ui.Panel(Ui.VBox(6, Ui.Caption("You give", Tokens.Bad), Ui.Wrapped(DescribeSide(s, offer.Receive), 15)), Tokens.BgDeep, 12).Expand()),
            Ui.HBox(10,
                Ui.Button(Loc.T("Reject"), () =>
                {
                    modal!.Close();
                    Send(new RejectTradeCommand(me, offer.Id));
                }, ButtonKind.Danger).Expand(),
                Ui.Button(Loc.T("Counter"), () =>
                {
                    modal!.Close();
                    OpenTradeComposer(offer.From, offer);
                }).Expand(),
                Ui.Button(Loc.T("Accept"), () =>
                {
                    modal!.Close();
                    Send(new AcceptTradeCommand(me, offer.Id));
                }, ButtonKind.Primary).Expand()));
        modal = Show(content, 620, dismissable: false);
    }

    /// <summary>
    /// Compose a new offer, or a counter-offer when <paramref name="answering"/> is set (the two
    /// sides start out swapped so the recipient edits from their own point of view).
    /// </summary>
    private void OpenTradeComposer(int partner, TradeOffer? answering)
    {
        var s = D;
        int seat = answering?.To ?? _activeSeat;
        if (s == null || seat < 0) return;
        var others = s.Players.Where(p => !p.Bankrupt && p.Id != seat).ToList();
        if (others.Count == 0) return;
        if (partner < 0 || partner == seat || partner >= s.Players.Count || s.Players[partner].Bankrupt)
            partner = others[0].Id;

        var give = answering?.Receive.Clone() ?? new TradeSide();
        var receive = answering?.Give.Clone() ?? new TradeSide();
        Modal? modal = null;
        var body = Ui.VBox(10);
        Label? error = null;
        Button? propose = null;

        void Validate()
        {
            string? problem = TradeRules.Validate(D!, seat, partner, give, receive);
            if (error != null) error.Text = problem ?? "";
            if (propose != null) propose.Disabled = problem != null;
        }

        Control SideEditor(string caption, Color color, int owner, TradeSide side)
        {
            var box = Ui.VBox(8, Ui.Caption(caption, color));
            var player = s.Players[owner];
            var money = new SpinBox
            {
                MinValue = 0,
                MaxValue = player.Money,
                Step = 10,
                Value = side.Money,
                Prefix = Currency + " ",
                UpdateOnTextChanged = true,
                CustomMinimumSize = new Vector2(Ui.Px(100), Ui.Px(42)),
            };
            money.ValueChanged += v =>
            {
                side.Money = (int)v;
                Validate();
            };
            var le = money.GetLineEdit();
            le.TextChanged += text =>
            {
                string digits = new string(text.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int amt))
                {
                    side.Money = Math.Clamp(amt, 0, player.Money);
                    Validate();
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    side.Money = 0;
                    Validate();
                }
            };

            void QuickAdd(int delta)
            {
                int next = delta < 0 ? 0 : Math.Clamp(side.Money + delta, 0, player.Money);
                side.Money = next;
                money.Value = next;
                Validate();
            }

            var moneyRow = Ui.HBox(4,
                money.Expand(),
                Ui.Button("+10", () => QuickAdd(10), ButtonKind.Secondary, 36),
                Ui.Button("+50", () => QuickAdd(50), ButtonKind.Secondary, 36),
                Ui.Button("+100", () => QuickAdd(100), ButtonKind.Secondary, 40),
                Ui.Button("0", () => QuickAdd(-1), ButtonKind.Ghost, 32));
            box.AddChild(moneyRow);

            var list = Ui.VBox(2);
            foreach (var prop in s.OwnedBy(owner).OrderBy(p => p.Tile))
            {
                int tile = prop.Tile;
                var check = new CheckBox { Text = _board.Tiles[tile].Name + (prop.Mortgaged ? " (mortgaged)" : ""), ButtonPressed = side.Tiles.Contains(tile) };
                check.AddThemeFontSizeOverride("font_size", Ui.Px(14));
                check.Toggled += on =>
                {
                    if (on) side.Tiles.Add(tile);
                    else side.Tiles.Remove(tile);
                    Validate();
                };
                list.AddChild(check);
            }
            if (player.JailCards > 0)
            {
                var card = new CheckBox { Text = "Release card", ButtonPressed = side.JailCards > 0 };
                card.Toggled += on =>
                {
                    side.JailCards = on ? 1 : 0;
                    Validate();
                };
                list.AddChild(card);
            }
            box.AddChild(Ui.Scroll(list).MinSize(0, 170));

            if (s.Rules.ContractsEnabled || s.Rules.PropertySharesEnabled)
            {
                var terms = Ui.VBox(2);
                void RedrawTerms()
                {
                    foreach (var child in terms.GetChildren())
                    {
                        terms.RemoveChild(child);
                        child.QueueFree();
                    }
                    var preview = new TradeSide { Shares = side.Shares, Terms = side.Terms };
                    if (side.Shares.Count + side.Terms.Count > 0) terms.AddChild(Ui.Wrapped(DescribeSide(s, preview), 13, Tokens.Info));
                }
                var owned = s.OwnedBy(owner).Select(p => p.Tile).ToList();
                var kinds = new List<string>();
                if (s.Rules.PropertySharesEnabled) kinds.Add("Share of property");
                if (s.Rules.ContractsEnabled) kinds.AddRange(new[] { "Rent immunity", "Revenue share", "Instalments", "Option to buy" });
                int kind = 0, tileIndex = 0;
                var tilePick = Ui.Options(owned.Select(t => _board.Tiles[t].Name).DefaultIfEmpty("—"), 0, i => tileIndex = i);
                var amount = new SpinBox { MinValue = 1, MaxValue = 2000, Step = 5, Value = 25, TooltipText = "Percent or amount" };
                var count = new SpinBox { MinValue = 1, MaxValue = 12, Step = 1, Value = 3, TooltipText = "Visits or rounds" };
                var add = Ui.Button("Add", () =>
                {
                    string chosen = kinds[kind];
                    int tile = owned.Count > 0 ? owned[Math.Min(tileIndex, owned.Count - 1)] : -1;
                    switch (chosen)
                    {
                        case "Share of property":
                            if (tile >= 0) side.Shares.Add(new ShareTransfer { Tile = tile, Percent = (int)amount.Value });
                            break;
                        case "Rent immunity":
                            side.Terms.Add(new ContractTerm { Kind = ContractKind.RentImmunity, Tile = tile, Count = (int)count.Value });
                            break;
                        case "Revenue share":
                            side.Terms.Add(new ContractTerm { Kind = ContractKind.RevenueShare, Tile = tile, Percent = (int)amount.Value, Count = (int)count.Value });
                            break;
                        case "Instalments":
                            side.Terms.Add(new ContractTerm { Kind = ContractKind.Installment, Amount = (int)amount.Value, Count = (int)count.Value });
                            break;
                        default:
                            side.Terms.Add(new ContractTerm { Kind = ContractKind.BuyOption, Tile = tile, Amount = (int)amount.Value, Count = (int)count.Value });
                            break;
                    }
                    RedrawTerms();
                    Validate();
                }, ButtonKind.Secondary);
                var clear = Ui.Button("Clear", () =>
                {
                    side.Shares.Clear();
                    side.Terms.Clear();
                    RedrawTerms();
                    Validate();
                }, ButtonKind.Ghost);
                box.AddChild(Ui.Caption("Advanced terms"));
                box.AddChild(Ui.Options(kinds, 0, i => kind = i));
                box.AddChild(tilePick);
                box.AddChild(Ui.HBox(6, amount, count));
                box.AddChild(Ui.HBox(6, add.Expand(), clear));
                box.AddChild(terms);
                RedrawTerms();
            }
            return Ui.Panel(box, Tokens.BgDeep, 12).Expand();
        }

        void Rebuild()
        {
            foreach (var child in body.GetChildren())
            {
                body.RemoveChild(child);
                child.QueueFree();
            }
            var header = Ui.HBox(10, Ui.Caption(answering != null ? "Counter-offer" : "Propose a trade", Tokens.Accent), Ui.Spacer());
            if (answering == null)
            {
                header.AddChild(Ui.Options(others.Select(p => p.Name), others.FindIndex(p => p.Id == partner), i =>
                {
                    partner = others[i].Id;
                    give = new TradeSide();
                    receive = new TradeSide();
                    Rebuild();
                }));
            }
            else
            {
                header.AddChild(Ui.Label(s.Players[partner].Name, 16, Tokens.Player(partner), bold: true));
            }
            body.AddChild(header);
            body.AddChild(Ui.HBox(12,
                SideEditor($"{s.Players[seat].Name} gives", Tokens.Bad, seat, give),
                SideEditor($"{s.Players[partner].Name} gives", Tokens.Good, partner, receive)));
            error = Ui.Wrapped("", 13, Tokens.Bad);
            body.AddChild(error);
            propose = Ui.Button(answering != null ? "Send counter-offer" : "Propose", () =>
            {
                modal!.Close();
                Send(answering != null
                    ? new CounterTradeCommand(seat, answering.Id, give, receive)
                    : new CreateTradeCommand(seat, partner, give, receive));
            }, ButtonKind.Primary);
            body.AddChild(Ui.HBox(10, Ui.Button("Cancel", () => modal!.Close(), ButtonKind.Ghost).Expand(), propose.Expand()));
            Validate();
        }

        Rebuild();
        modal = Show(body, 760);
    }
}
