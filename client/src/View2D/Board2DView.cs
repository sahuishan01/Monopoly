using BoardEmpire.Core;
using BoardEmpire.Presentation;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;
using Godot;

namespace BoardEmpire.View2D;

/// <summary>
/// The flat board: everything is drawn in <see cref="_Draw"/> from the display state, so it
/// only repaints while something is moving. It is a first-class view, not a fallback, and it
/// is deliberately cheap on battery.
/// </summary>
public partial class Board2DView : Control, IGameView
{
    private struct Particle
    {
        public Vector2 From, To, Control;
        public float Age, Life, Size;
        public Color Color;
    }

    private struct FloatText
    {
        public Vector2 At;
        public string Text;
        public Color Color;
        public float Age;
    }

    private readonly List<Particle> _particles = new();
    private readonly List<FloatText> _floats = new();
    private readonly Dictionary<int, (float Left, Color Color)> _flash = new();
    private readonly Random _random = new();
    private readonly StyleBoxFlat _dieBox = new();

    private BoardDefinition _board = null!;
    private BoardLayout _layout = null!;
    private Settings _settings = null!;
    private GameState? _state;
    private Vector2[] _tokenPos = Array.Empty<Vector2>();
    private float[] _tokenHop = Array.Empty<float>();
    private int _d1 = 1, _d2 = 1;
    private float _diceShow, _diceSpin;
    private float _zoom = 1, _zoomTarget = 1;
    private Vector2 _focus = new(0.5f, 0.5f), _focusTarget = new(0.5f, 0.5f);
    private Vector2 _dragStart;
    private bool _dragging, _moved;
    private float _celebrate;
    private float _time;
    private int _selected = -1;

    public Control Node => this;

    public event Action<int>? TileTapped;

    public void Build(BoardDefinition board, Settings settings)
    {
        _board = board;
        _layout = new BoardLayout(board.Count);
        _settings = settings;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Linear;
        Resized += QueueRedraw;
    }

    public void ApplySettings(Settings settings)
    {
        _settings = settings;
        QueueRedraw();
    }

    // ------------------------------------------------------------------ geometry

    private float BoardPixels => Mathf.Min(Size.X, Size.Y) * 0.97f;

    private Vector2 ToScreen(Vector2 unit) => Size / 2 + (unit - _focus) * BoardPixels * _zoom;

    private Vector2 ToUnit(Vector2 screen) => (screen - Size / 2) / (BoardPixels * _zoom) + _focus;

    private Rect2 ToScreen(Rect2 unit) => new(ToScreen(unit.Position), unit.Size * BoardPixels * _zoom);

    private Vector2 Spot(GameState s, int player)
    {
        int tile = s.Players[player].Position;
        int total = 0, slot = 0;
        foreach (var p in s.Players)
        {
            if (p.Bankrupt || p.Position != tile) continue;
            if (p.Id == player) slot = total;
            total++;
        }
        return _layout.TokenSpot(tile, slot, total);
    }

    // ------------------------------------------------------------------ IGameView

    public void Sync(GameState state)
    {
        _state = state;
        if (_tokenPos.Length != state.Players.Count)
        {
            _tokenPos = new Vector2[state.Players.Count];
            _tokenHop = new float[state.Players.Count];
        }
        for (int i = 0; i < state.Players.Count; i++) _tokenPos[i] = Spot(state, i);
        if (state.Die1 > 0 && (state.Die1 != _d1 || state.Die2 != _d2 || _diceShow <= 0) && _diceSpin <= 0) _diceShow = 2.5f;
        _d1 = Math.Max(1, state.Die1);
        _d2 = Math.Max(1, state.Die2);
        QueueRedraw();
    }

    public void Focus(int tile)
    {
        _selected = tile;
        if (tile < 0)
        {
            _zoomTarget = 1;
            _focusTarget = new Vector2(0.5f, 0.5f);
        }
        else
        {
            _zoomTarget = 1.9f;
            _focusTarget = _layout.Center(tile).Lerp(new Vector2(0.5f, 0.5f), 0.25f);
        }
    }

    public async Task Play(GameEvent e, GameState after, AnimContext ctx)
    {
        _state = after;
        if (_tokenPos.Length != after.Players.Count) Sync(after);
        switch (e)
        {
            case DiceRolled roll:
                _diceShow = 100;
                _diceSpin = ctx.Time(0.55f);
                await Anim.Wait(this, ctx.Time(0.55f));
                _d1 = roll.D1;
                _d2 = roll.D2;
                _diceSpin = 0;
                _diceShow = 2.2f;
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case PlayerMoved move:
                var path = move.Kind switch
                {
                    MoveKind.Walk => _layout.PathForward(move.From, move.To),
                    MoveKind.Backward => _layout.PathBackward(move.From, move.To),
                    _ => new List<int> { move.To },
                };
                float step = move.Kind == MoveKind.Jump ? 0.55f : path.Count > 8 ? 0.09f : 0.13f;
                foreach (int tile in path)
                    await Hop(move.Player, _layout.TokenSpot(tile, 0, 1), ctx.Time(step), move.Kind == MoveKind.Jump ? 0.09f : 0.03f);
                Settle(after);
                break;

            case PlayerSentToJail jail:
                Flash(jail.JailTile, Tokens.Bad, 0.8f);
                await Hop(jail.Player, _layout.TokenSpot(jail.JailTile, 0, 1), ctx.Time(0.6f), 0.1f);
                Settle(after);
                break;

            case PropertyPurchased buy:
                Flash(buy.Tile, Tokens.Player(buy.Player), 0.9f);
                Coins(_tokenPos[buy.Player], _layout.Center(buy.Tile), 6, Tokens.Accent);
                await Anim.Wait(this, ctx.Time(0.35f));
                break;

            case PropertyGranted grant:
                Flash(grant.Tile, Tokens.Player(grant.Player), 0.6f);
                await Anim.Wait(this, ctx.Time(0.08f));
                break;

            case AuctionCompleted auction when auction.Winner >= 0:
                Flash(auction.Tile, Tokens.Player(auction.Winner), 1.0f);
                await Anim.Wait(this, ctx.Time(0.4f));
                break;

            case BuildingConstructed build:
                Flash(build.Tile, Tokens.Good, 0.7f);
                Burst(_layout.Center(build.Tile), 10, Tokens.Good);
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case BuildingSold sold:
                Flash(sold.Tile, Tokens.Bad, 0.5f);
                await Anim.Wait(this, ctx.Time(0.2f));
                break;

            case PropertyMortgaged or PropertyUnmortgaged:
                await Anim.Wait(this, ctx.Time(0.15f));
                break;

            case RentPaid rent:
                Flash(rent.Tile, Tokens.Player(rent.Payee), 0.8f);
                Pay(rent.Payer, rent.Payee, rent.Amount);
                await Anim.Wait(this, ctx.Time(0.55f));
                break;

            case MoneyTransferred money:
                Pay(money.From, money.To, money.Amount);
                await Anim.Wait(this, ctx.Time(0.45f));
                break;

            case DebtPaid debt:
                Pay(debt.Debt.Debtor, debt.Debt.Creditor, debt.Debt.Amount);
                await Anim.Wait(this, ctx.Time(0.45f));
                break;

            case SalaryPaid salary:
                Pay(-1, salary.Player, salary.Amount);
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case ObjectiveCompleted objective:
                Burst(_tokenPos[objective.Player], 16, Tokens.Accent);
                Float(_tokenPos[objective.Player], "+" + objective.Reward, Tokens.Good);
                await Anim.Wait(this, ctx.Time(0.5f));
                break;

            case TradeAccepted trade:
                foreach (int t in trade.Offer.Give.Tiles) Flash(t, Tokens.Player(trade.Offer.To), 0.9f);
                foreach (int t in trade.Offer.Receive.Tiles) Flash(t, Tokens.Player(trade.Offer.From), 0.9f);
                await Anim.Wait(this, ctx.Time(0.5f));
                break;

            case PlayerBankrupt bankrupt:
                Burst(_tokenPos[bankrupt.Player], 22, Tokens.Bad);
                await Anim.Wait(this, ctx.Time(0.8f));
                break;

            case CityEventOccurred or EconomyChanged or ProjectCompleted:
                for (int i = 0; i < _board.Count; i++)
                    if (after.Effects.Count > 0 && Calc.EffectApplies(after, after.Effects[^1], i) && _board.Tiles[i].IsOwnable)
                        Flash(i, Tokens.Info, 1.2f);
                await Anim.Wait(this, ctx.Time(0.5f));
                break;

            case TurnStarted:
                Settle(after);
                await Anim.Wait(this, ctx.Time(0.12f));
                break;

            case GameEnded:
                _celebrate = 4;
                await Anim.Wait(this, ctx.Time(1.2f));
                break;
        }
        QueueRedraw();
    }

    private void Settle(GameState s)
    {
        for (int i = 0; i < s.Players.Count; i++)
        {
            _tokenPos[i] = Spot(s, i);
            _tokenHop[i] = 0;
        }
    }

    private async Task Hop(int player, Vector2 to, float seconds, float height)
    {
        var from = _tokenPos[player];
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            _tokenPos[player] = from.Lerp(to, t);
            _tokenHop[player] = Mathf.Sin(t * Mathf.Pi) * height;
            QueueRedraw();
        }), 0f, 1f, Mathf.Max(0.02f, seconds)).SetTrans(Tween.TransitionType.Sine);
        await Anim.Finished(this, tween);
        _tokenHop[player] = 0;
    }

    // ------------------------------------------------------------------ effects

    private void Flash(int tile, Color color, float seconds) => _flash[tile] = (seconds, color);

    private Vector2 AnchorOf(int player) => player >= 0 && player < _tokenPos.Length ? _tokenPos[player] : new Vector2(0.5f, 0.5f);

    private void Pay(int from, int to, int amount)
    {
        if (amount <= 0) return;
        Coins(AnchorOf(from), AnchorOf(to), Math.Clamp(3 + amount / 60, 3, 14), Tokens.Accent);
        if (from >= 0) Float(AnchorOf(from), "-" + amount, Tokens.Bad);
        if (to >= 0) Float(AnchorOf(to) + new Vector2(0, -0.02f), "+" + amount, Tokens.Good);
    }

    private void Coins(Vector2 from, Vector2 to, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            var mid = (from + to) / 2 + new Vector2((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * 0.25f;
            _particles.Add(new Particle
            {
                From = from, To = to, Control = mid, Age = -i * 0.03f, Life = 0.55f, Size = 0.008f, Color = color,
            });
        }
    }

    private void Burst(Vector2 at, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(_random.NextDouble() * Mathf.Tau);
            var to = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (0.03f + (float)_random.NextDouble() * 0.06f);
            _particles.Add(new Particle { From = at, To = to, Control = (at + to) / 2, Age = 0, Life = 0.6f, Size = 0.006f, Color = color });
        }
    }

    private void Float(Vector2 at, string text, Color color) =>
        _floats.Add(new FloatText { At = at, Text = text, Color = color, Age = 0 });

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        bool animating = false;

        if (!Mathf.IsEqualApprox(_zoom, _zoomTarget, 0.002f) || _focus.DistanceTo(_focusTarget) > 0.001f)
        {
            float k = _settings.ReduceMotion ? 1f : 1f - Mathf.Exp(-dt * 9f);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, k);
            _focus = _focus.Lerp(_focusTarget, k);
            animating = true;
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) _particles.RemoveAt(i);
            else _particles[i] = p;
            animating = true;
        }
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var f = _floats[i];
            f.Age += dt;
            if (f.Age >= 1.3f) _floats.RemoveAt(i);
            else _floats[i] = f;
            animating = true;
        }
        foreach (int tile in _flash.Keys.ToList())
        {
            var f = _flash[tile];
            f.Left -= dt;
            if (f.Left <= 0) _flash.Remove(tile);
            else _flash[tile] = f;
            animating = true;
        }
        if (_diceSpin > 0)
        {
            _diceSpin -= dt;
            _d1 = _random.Next(1, 7);
            _d2 = _random.Next(1, 7);
            animating = true;
        }
        else if (_diceShow > 0 && _diceShow < 50)
        {
            _diceShow -= dt;
            animating = true;
        }
        if (_celebrate > 0)
        {
            _celebrate -= dt;
            if (_random.Next(3) == 0)
                Burst(new Vector2((float)_random.NextDouble(), (float)_random.NextDouble()), 8, Tokens.Players[_random.Next(Tokens.Players.Length)]);
            animating = true;
        }
        if (animating) QueueRedraw();
    }

    // ------------------------------------------------------------------ input

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoomTarget = Mathf.Min(3f, _zoomTarget * 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoomTarget = Mathf.Max(1f, _zoomTarget / 1.15f);
                if (_zoomTarget <= 1.01f) _focusTarget = new Vector2(0.5f, 0.5f);
                break;
            case InputEventMagnifyGesture magnify:
                _zoomTarget = Mathf.Clamp(_zoomTarget * magnify.Factor, 1f, 3f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _dragging = true;
                    _moved = false;
                    _dragStart = button.Position;
                }
                else
                {
                    _dragging = false;
                    if (!_moved)
                    {
                        int tile = _layout.TileAt(ToUnit(button.Position));
                        if (tile >= 0) TileTapped?.Invoke(tile);
                        else if (button.DoubleClick) Focus(-1);
                    }
                }
                break;
            case InputEventMouseMotion motion when _dragging:
                if (motion.Position.DistanceTo(_dragStart) > 8) _moved = true;
                if (_moved && _zoom > 1.02f)
                {
                    _focusTarget -= motion.Relative / (BoardPixels * _zoom);
                    _focusTarget = _focusTarget.Clamp(Vector2.Zero, Vector2.One);
                    _focus = _focusTarget;
                    QueueRedraw();
                }
                break;
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        if (_board == null) return;
        var boardRect = ToScreen(new Rect2(0, 0, 1, 1));
        DrawRect(boardRect.Grow(6), Tokens.BgDeep);
        DrawRect(boardRect, Tokens.Panel.Darkened(0.25f));
        DrawCenter();
        for (int i = 0; i < _board.Count; i++) DrawTile(i);
        DrawTokens();
        DrawDice();
        DrawParticles();
    }

    private Color DistrictColor(string? id)
    {
        int index = _board.DistrictIndex(id);
        return index >= 0 ? new Color(_board.Districts[index].Color) : Tokens.Muted;
    }

    private void DrawTile(int i)
    {
        var def = _board.Tiles[i];
        var r = ToScreen(_layout.Rect(i));
        var prop = _state?.Property(i);
        bool corner = _layout.IsCorner(i);
        float w = _layout.TileWidth * BoardPixels * _zoom;

        DrawRect(r, corner ? Tokens.PanelHi.Lightened(0.04f) : Tokens.PanelHi);
        var text = r.Grow(-w * 0.06f);

        if (def.Type == TileType.Street)
        {
            var band = ToScreen(_layout.Band(i));
            var color = DistrictColor(def.District);
            DrawRect(band, color);
            if (_settings.ColorBlindPatterns) DrawGlyph(band, _board.Districts[_board.DistrictIndex(def.District)].Glyph);
            if (prop is { Level: > 0 }) DrawBuildings(band, prop.Level, _layout.Side(i) is 1 or 3);
            // Text lives in the part of the tile that the band leaves free.
            text = _layout.Side(i) switch
            {
                0 => new Rect2(text.Position.X, band.End.Y, text.Size.X, text.End.Y - band.End.Y),
                1 => new Rect2(text.Position.X, text.Position.Y, band.Position.X - text.Position.X, text.Size.Y),
                2 => new Rect2(text.Position.X, text.Position.Y, text.Size.X, band.Position.Y - text.Position.Y),
                _ => new Rect2(band.End.X, text.Position.Y, text.End.X - band.End.X, text.Size.Y),
            };
        }

        int nameSize = (int)Mathf.Clamp(w * (corner ? 0.2f : 0.165f), 6, 22);
        var ink = def.Type is TileType.Street or TileType.Transit or TileType.Utility ? Tokens.Text : Tokens.Muted;
        string caption = def.Type switch
        {
            TileType.Start => "START",
            TileType.Tax => _state != null ? Ui.Money(_board.Currency, Calc.TaxAmount(_state, i)) : "",
            TileType.Fortune => "?",
            TileType.Civic => "FUND",
            TileType.Transit => "TRANSIT",
            TileType.Utility => "UTILITY",
            TileType.GoToJail => "GO TO",
            _ => "",
        };
        float lineHeight = Ui.Body.GetHeight(nameSize);
        float top = text.Position.Y + Mathf.Max(0, (text.Size.Y - lineHeight * 3.1f) / 2);
        if (caption.Length > 0)
        {
            DrawString(Ui.MonoBold, new Vector2(text.Position.X, top + lineHeight * 0.8f), caption, HorizontalAlignment.Center,
                text.Size.X, Math.Max(6, nameSize - 1), def.Type == TileType.Start ? Tokens.Accent : Tokens.Muted);
            top += lineHeight;
        }
        DrawMultilineString(Ui.Body, new Vector2(text.Position.X, top + lineHeight * 0.8f), def.Name, HorizontalAlignment.Center,
            text.Size.X, nameSize, caption.Length > 0 ? 2 : 3, ink);

        if (def.IsOwnable && _state != null && prop != null)
        {
            string line = prop.Owner < 0
                ? Ui.Money(_board.Currency, Calc.PurchasePrice(_state, i))
                : prop.Mortgaged ? "MORTGAGED" : Ui.Money(_board.Currency, Calc.Rent(_state, i, 7));
            DrawString(Ui.Mono, new Vector2(text.Position.X, text.End.Y - nameSize * 0.25f), line, HorizontalAlignment.Center,
                text.Size.X, Math.Max(6, nameSize - 1), prop.Owner < 0 ? Tokens.Muted : Tokens.Accent);

            if (prop.Owner >= 0)
            {
                var owner = Tokens.Player(prop.Owner);
                DrawRect(r.Grow(-1.5f), owner, false, Mathf.Max(2f, w * 0.05f));
                if (prop.Mortgaged)
                {
                    DrawRect(r, new Color(0, 0, 0, 0.55f));
                    for (float d = -r.Size.Y; d < r.Size.X; d += w * 0.22f)
                    {
                        var a = new Vector2(Mathf.Max(r.Position.X, r.Position.X + d), r.Position.Y + Mathf.Max(0, -d));
                        var b = new Vector2(Mathf.Min(r.End.X, r.Position.X + d + r.Size.Y), r.Position.Y + Mathf.Min(r.Size.Y, r.Size.X - d));
                        DrawLine(a, b, owner with { A = 0.35f }, 1.5f);
                    }
                }
                if (prop.Shares.Count > 1)
                    DrawString(Ui.Mono, r.Position + new Vector2(3, nameSize + 2), $"{prop.ShareOf(prop.Owner)}%", HorizontalAlignment.Left, -1,
                        Math.Max(6, nameSize - 2), owner);
            }
            if (_state.Effects.Count > 0)
            {
                int pct = Calc.EffectPercent(_state, i, e => e.RentPercent);
                if (pct != 0)
                    DrawString(Ui.MonoBold, r.Position + new Vector2(r.Size.X - w * 0.32f, nameSize + 1), pct > 0 ? "▲" : "▼",
                        HorizontalAlignment.Left, -1, Math.Max(7, nameSize), pct > 0 ? Tokens.Good : Tokens.Bad);
            }
        }

        DrawRect(r, Tokens.BgDeep, false, 1.2f);
        if (_flash.TryGetValue(i, out var flash))
            DrawRect(r, flash.Color with { A = Mathf.Clamp(flash.Left, 0, 1) * 0.55f });
        if (i == _selected) DrawRect(r.Grow(1), Tokens.Accent, false, 3f);
    }

    private void DrawBuildings(Rect2 band, int level, bool vertical)
    {
        float along = vertical ? band.Size.Y : band.Size.X;
        float across = vertical ? band.Size.X : band.Size.Y;
        float size = Mathf.Min(across * 0.62f, along / 5.5f);
        if (level == 5)
        {
            var landmark = new Rect2(band.GetCenter() - new Vector2(size * 1.1f, size / 2), new Vector2(size * 2.2f, size));
            if (vertical) landmark = new Rect2(band.GetCenter() - new Vector2(size / 2, size * 1.1f), new Vector2(size, size * 2.2f));
            DrawRect(landmark, Tokens.Accent);
            DrawRect(landmark, Tokens.BgDeep, false, 1.5f);
            return;
        }
        float gap = (along - level * size) / (level + 1);
        for (int n = 0; n < level; n++)
        {
            float offset = gap + n * (size + gap);
            var pos = vertical
                ? new Vector2(band.Position.X + (across - size) / 2, band.Position.Y + offset)
                : new Vector2(band.Position.X + offset, band.Position.Y + (across - size) / 2);
            var house = new Rect2(pos, new Vector2(size, size));
            DrawRect(house, Tokens.Text);
            DrawRect(house, Tokens.BgDeep, false, 1.2f);
        }
    }

    /// <summary>Shape drawn on the district band so that districts never rely on colour alone.</summary>
    private void DrawGlyph(Rect2 band, string glyph)
    {
        var c = band.GetCenter();
        float s = Mathf.Min(band.Size.X, band.Size.Y) * 0.36f;
        var ink = new Color(0, 0, 0, 0.75f);
        switch (glyph)
        {
            case "diamond":
                DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s, 0), c + new Vector2(0, s), c + new Vector2(-s, 0) }, ink);
                break;
            case "triangle":
                DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s, s), c + new Vector2(-s, s) }, ink);
                break;
            case "square":
                DrawRect(new Rect2(c - new Vector2(s, s) * 0.8f, new Vector2(s, s) * 1.6f), ink);
                break;
            case "hexagon":
                DrawColoredPolygon(Enumerable.Range(0, 6).Select(k => c + Vector2.FromAngle(k * Mathf.Tau / 6) * s).ToArray(), ink);
                break;
            case "star":
                DrawColoredPolygon(Enumerable.Range(0, 10)
                    .Select(k => c + Vector2.FromAngle(k * Mathf.Tau / 10 - Mathf.Pi / 2) * (k % 2 == 0 ? s : s * 0.45f)).ToArray(), ink);
                break;
            case "wave":
                DrawLine(c + new Vector2(-s, 0), c + new Vector2(-s / 3, -s / 2), ink, 2);
                DrawLine(c + new Vector2(-s / 3, -s / 2), c + new Vector2(s / 3, s / 2), ink, 2);
                DrawLine(c + new Vector2(s / 3, s / 2), c + new Vector2(s, 0), ink, 2);
                break;
            case "leaf":
                DrawArc(c, s * 0.8f, 0, Mathf.Pi, 12, ink, 2.5f);
                DrawLine(c + new Vector2(-s * 0.8f, 0), c + new Vector2(s * 0.8f, 0), ink, 2);
                break;
            case "anchor":
                DrawLine(c + new Vector2(0, -s), c + new Vector2(0, s), ink, 2);
                DrawArc(c + new Vector2(0, s * 0.2f), s * 0.8f, 0.2f, Mathf.Pi - 0.2f, 10, ink, 2);
                break;
            default:
                DrawCircle(c, s * 0.8f, ink);
                break;
        }
    }

    private void DrawCenter()
    {
        float c = _layout.CornerSize;
        var inner = ToScreen(new Rect2(c, c, 1 - 2 * c, 1 - 2 * c));
        DrawRect(inner, Tokens.Bg);
        float unit = inner.Size.X;
        int title = (int)Mathf.Clamp(unit * 0.075f, 10, 64);
        var at = inner.Position + new Vector2(0, unit * 0.2f);
        DrawString(Ui.MonoBold, at, "BOARD EMPIRE", HorizontalAlignment.Center, inner.Size.X, title, Tokens.Accent);
        DrawString(Ui.Mono, at + new Vector2(0, title * 0.9f), _board.Name.ToUpperInvariant(), HorizontalAlignment.Center, inner.Size.X,
            Math.Max(8, title / 3), Tokens.Muted);
        if (_state == null) return;

        int body = (int)Mathf.Clamp(unit * 0.028f, 8, 22);
        float y = at.Y + title * 1.6f;
        var lines = new List<(string Text, Color Color)>();
        if (_state.Rules.MarketEventsEnabled) lines.Add(($"ECONOMY · {_state.Economy.ToString().ToUpperInvariant()}", Tokens.Info));
        foreach (var e in _state.Effects.Where(e => e.Source != "economy").Take(3))
            lines.Add(($"{e.Name} · {(e.RoundsLeft > 0 ? e.RoundsLeft + " rounds" : "")}", Tokens.Text));
        if (_state.Project is { } project)
            lines.Add(($"{project.Name}: {Ui.Money(_board.Currency, project.Funded)} / {Ui.Money(_board.Currency, project.Cost)}", Tokens.Accent));
        if (_state.Rules.FreeParkingJackpot) lines.Add(($"JACKPOT · {Ui.Money(_board.Currency, _state.Bank.Jackpot)}", Tokens.Good));
        foreach (var (text, color) in lines)
        {
            DrawString(Ui.Mono, new Vector2(inner.Position.X, y), text, HorizontalAlignment.Center, inner.Size.X, body, color);
            y += body * 1.5f;
        }
    }

    private void DrawTokens()
    {
        if (_state == null) return;
        float radius = _layout.TileWidth * BoardPixels * _zoom * 0.2f;
        foreach (var p in _state.Players)
        {
            if (p.Bankrupt || p.Id >= _tokenPos.Length) continue;
            var at = ToScreen(_tokenPos[p.Id] - new Vector2(0, _tokenHop[p.Id]));
            var color = Tokens.Player(p.Id);
            DrawCircle(at + new Vector2(0, radius * 0.25f), radius * 1.05f, new Color(0, 0, 0, 0.35f));
            if (p.Id == _state.CurrentPlayer && !_state.IsOver) DrawArc(at, radius * 1.45f, 0, Mathf.Tau, 28, Tokens.Text, 2.5f);
            DrawCircle(at, radius, color);
            DrawArc(at, radius, 0, Mathf.Tau, 24, Tokens.BgDeep, 2f);
            DrawTokenMark(at, radius * 0.55f, p.Token);
            if (p.InJail)
                for (int bar = -1; bar <= 1; bar++)
                    DrawLine(at + new Vector2(bar * radius * 0.5f, -radius), at + new Vector2(bar * radius * 0.5f, radius), Tokens.BgDeep, 2.5f);
        }
    }

    /// <summary>Each token has its own silhouette, so players are told apart without colour.</summary>
    private void DrawTokenMark(Vector2 c, float s, int token)
    {
        var ink = Tokens.BgDeep;
        switch (token % 8)
        {
            case 0:
                DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.8f, s * 0.8f), c + new Vector2(-s * 0.8f, s * 0.8f) }, ink);
                break;
            case 1:
                DrawRect(new Rect2(c - new Vector2(s, s) * 0.75f, new Vector2(s, s) * 1.5f), ink);
                break;
            case 2:
                DrawColoredPolygon(new[] { c + new Vector2(s * 0.3f, -s), c + new Vector2(-s * 0.6f, s * 0.15f), c + new Vector2(0, s * 0.15f),
                    c + new Vector2(-s * 0.3f, s), c + new Vector2(s * 0.6f, -s * 0.15f), c + new Vector2(0, -s * 0.15f) }, ink);
                break;
            case 3:
                DrawColoredPolygon(new[] { c + new Vector2(-s, s * 0.7f), c + new Vector2(-s, -s * 0.6f), c + new Vector2(-s * 0.4f, 0),
                    c + new Vector2(0, -s * 0.8f), c + new Vector2(s * 0.4f, 0), c + new Vector2(s, -s * 0.6f), c + new Vector2(s, s * 0.7f) }, ink);
                break;
            case 4:
                DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s, 0), c + new Vector2(0, s), c + new Vector2(-s, 0) }, ink);
                break;
            case 5:
                DrawArc(c, s * 0.75f, 0, Mathf.Tau, 16, ink, 2.5f);
                DrawCircle(c, s * 0.25f, ink);
                break;
            case 6:
                DrawLine(c + new Vector2(0, -s), c + new Vector2(0, s), ink, 3);
                DrawLine(c + new Vector2(-s, 0), c + new Vector2(s, 0), ink, 3);
                break;
            default:
                DrawColoredPolygon(Enumerable.Range(0, 10)
                    .Select(k => c + Vector2.FromAngle(k * Mathf.Tau / 10 - Mathf.Pi / 2) * (k % 2 == 0 ? s : s * 0.45f)).ToArray(), ink);
                break;
        }
    }

    private void DrawDice()
    {
        if (_diceShow <= 0 && _diceSpin <= 0) return;
        float alpha = _diceSpin > 0 ? 1 : Mathf.Clamp(_diceShow, 0, 1);
        float size = _layout.CornerSize * BoardPixels * _zoom * 0.55f;
        var center = ToScreen(new Vector2(0.5f, 0.66f));
        DrawDie(center + new Vector2(-size * 0.62f, 0), size, _d1, alpha, _diceSpin > 0 ? Mathf.Sin(_time * 30) * 0.2f : 0);
        DrawDie(center + new Vector2(size * 0.62f, 0), size, _d2, alpha, _diceSpin > 0 ? Mathf.Cos(_time * 27) * 0.2f : 0);
    }

    private void DrawDie(Vector2 center, float size, int value, float alpha, float tilt)
    {
        DrawSetTransform(center, tilt, Vector2.One);
        _dieBox.BgColor = Tokens.Text with { A = alpha };
        int radius = (int)(size * 0.18f);
        _dieBox.CornerRadiusTopLeft = _dieBox.CornerRadiusTopRight = _dieBox.CornerRadiusBottomLeft = _dieBox.CornerRadiusBottomRight = radius;
        _dieBox.ShadowColor = new Color(0, 0, 0, 0.4f * alpha);
        _dieBox.ShadowSize = 6;
        DrawStyleBox(_dieBox, new Rect2(-size / 2, -size / 2, size, size));
        float o = size * 0.26f, pip = size * 0.085f;
        var ink = Tokens.BgDeep with { A = alpha };
        void Pip(float x, float y) => DrawCircle(new Vector2(x * o, y * o), pip, ink);
        if (value % 2 == 1) Pip(0, 0);
        if (value >= 2)
        {
            Pip(-1, -1);
            Pip(1, 1);
        }
        if (value >= 4)
        {
            Pip(1, -1);
            Pip(-1, 1);
        }
        if (value == 6)
        {
            Pip(-1, 0);
            Pip(1, 0);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawParticles()
    {
        float scale = BoardPixels * _zoom;
        foreach (var p in _particles)
        {
            if (p.Age < 0) continue;
            float t = p.Age / p.Life;
            var a = p.From.Lerp(p.Control, t);
            var b = p.Control.Lerp(p.To, t);
            DrawCircle(ToScreen(a.Lerp(b, t)), p.Size * scale * (1.2f - t * 0.5f), p.Color with { A = 1 - t * t });
        }
        int size = (int)Mathf.Clamp(_layout.TileWidth * scale * 0.3f, 10, 30);
        foreach (var f in _floats)
        {
            float t = f.Age / 1.3f;
            var at = ToScreen(f.At) + new Vector2(-60, -size - t * size * 2.2f);
            DrawString(Ui.MonoBold, at + new Vector2(1, 1), f.Text, HorizontalAlignment.Center, 120, size, new Color(0, 0, 0, 0.7f * (1 - t)));
            DrawString(Ui.MonoBold, at, f.Text, HorizontalAlignment.Center, 120, size, f.Color with { A = 1 - t * t });
        }
    }
}
