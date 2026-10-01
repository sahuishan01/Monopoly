using BoardEmpire.Core;
using BoardEmpire.Presentation;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.Rules;
using Game.Core.State;
using Godot;

namespace BoardEmpire.View3D;

/// <summary>
/// The living city. The same events that move sprites on the flat board raise buildings, drive
/// tokens and steer the camera here; ownership and development are what the skyline is made of.
/// </summary>
public partial class Board3DView : SubViewportContainer, IGameView
{
    private const float B = 24f;
    private const float TileHeight = 0.24f;

    private sealed class TileVisual
    {
        public MeshInstance3D Slab = null!;
        public MeshInstance3D? Strip;
        public Label3D? Price;
        public Node3D Lot = null!;
        public StandardMaterial3D Material = null!;
        public int Owner = -2, Level = -1;
        public bool Mortgaged;
        public DevelopmentType Dev;
        public Color BaseColor;
    }

    private readonly Dictionary<string, StandardMaterial3D> _materials = new();
    private readonly List<MeshInstance3D> _highlights = new();
    private readonly List<Label3D> _floaters = new();
    private readonly List<CpuParticles3D> _bursts = new();
    private readonly Random _random = new();

    private SubViewport _viewport = null!;
    private Node3D _world = null!;
    private CameraRig _rig = null!;
    private Dice3D _dice = null!;
    private CityAmbience _city = null!;
    private DirectionalLight3D _sun = null!;
    private BoardDefinition _board = null!;
    private BoardLayout _layout = null!;
    private Settings _settings = null!;
    private GameState? _state;
    private TileVisual[] _tiles = Array.Empty<TileVisual>();
    private Node3D[] _tokens = Array.Empty<Node3D>();
    private MeshInstance3D _turnRing = null!;
    private MeshInstance3D _selection = null!;
    private int _nextHighlight, _nextFloater, _nextBurst;
    private Vector2 _pressAt;
    private bool _pressed, _dragged;
    private double _lastTap;

    public Control Node => this;

    public event Action<int>? TileTapped;

    // ------------------------------------------------------------------ construction

    private Vector3 World(Vector2 unit, float y = 0) => new((unit.X - 0.5f) * B, y, (unit.Y - 0.5f) * B);

    private StandardMaterial3D Mat(Color color, float roughness = 0.8f, float metallic = 0f)
    {
        string key = $"{color.ToRgba32()}:{roughness}:{metallic}";
        if (!_materials.TryGetValue(key, out var m))
        {
            m = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
            _materials[key] = m;
        }
        return m;
    }

    private Color DistrictColor(string? id)
    {
        int index = _board.DistrictIndex(id);
        return index >= 0 ? new Color(_board.Districts[index].Color) : new Color(0.6f, 0.62f, 0.68f);
    }

    public void Build(BoardDefinition board, Settings settings)
    {
        _board = board;
        _layout = new BoardLayout(board.Count);
        _settings = settings;
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;

        _viewport = new SubViewport
        {
            OwnWorld3D = true, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I(64, 64),
        };
        AddChild(_viewport);
        _world = new Node3D();
        _viewport.AddChild(_world);

        var sky = new ProceduralSkyMaterial { SunAngleMax = 18, SunCurve = 0.1f };
        var environment = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            TonemapMode = Environment.ToneMapper.Filmic,
            TonemapExposure = 0.92f,
        };
        _world.AddChild(new WorldEnvironment { Environment = environment });
        _sun = new DirectionalLight3D { RotationDegrees = new Vector3(-52, 38, 0), LightEnergy = 1.2f, DirectionalShadowMaxDistance = 70 };
        _world.AddChild(_sun);

        _rig = new CameraRig { BoardSize = B };
        _world.AddChild(_rig);

        // Ground and the board slab.
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(B * 8, B * 8) }, Position = new Vector3(0, -0.2f, 0),
            MaterialOverride = Mat(new Color(0.11f, 0.15f, 0.2f), 1f),
        });
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(B + 0.7f, 0.2f, B + 0.7f) }, Position = new Vector3(0, -0.1f, 0),
            MaterialOverride = Mat(new Color(0.07f, 0.09f, 0.14f), 0.9f),
        });

        BuildTiles();

        float inner = B * (1 - 2 * _layout.CornerSize);
        _city = new CityAmbience();
        _world.AddChild(_city);
        _city.Build(B, inner, _sun, environment, sky, settings.Quality, settings.ReduceMotion);

        _dice = new Dice3D { Position = new Vector3(0, TileHeight, inner * 0.36f) };
        _world.AddChild(_dice);
        _dice.Build(0.95f);

        _turnRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.52f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.White, EmissionEnabled = true, Emission = Colors.White, EmissionEnergyMultiplier = 1.2f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        _world.AddChild(_turnRing);
        _selection = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(Tokens.Accent, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        _world.AddChild(_selection);

        for (int i = 0; i < 6; i++)
        {
            var highlight = new MeshInstance3D { Visible = false };
            _world.AddChild(highlight);
            _highlights.Add(highlight);
            var floater = new Label3D
            {
                Font = Ui.MonoBold, FontSize = 72, PixelSize = 0.015f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
                OutlineSize = 18, OutlineModulate = new Color(0, 0, 0, 0.85f), Visible = false,
            };
            _world.AddChild(floater);
            _floaters.Add(floater);
        }
        for (int i = 0; i < 4; i++)
        {
            var burst = new CpuParticles3D
            {
                Emitting = false, OneShot = true, Amount = 18, Lifetime = 0.9f, Explosiveness = 0.95f,
                Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.05f, 0.14f) },
                Direction = Vector3.Up, Spread = 55, Gravity = new Vector3(0, -12, 0), InitialVelocityMin = 3.5f, InitialVelocityMax = 6.5f,
                AngularVelocityMin = -360, AngularVelocityMax = 360,
            };
            _world.AddChild(burst);
            _bursts.Add(burst);
        }

        ApplySettings(settings);
        Resized += UpdateAspect;
        UpdateAspect();
    }

    private void UpdateAspect()
    {
        if (Size.Y <= 0) return;
        _rig.Aspect = Size.X / Size.Y;
        if (_rig.Shot == CameraShot.Overview) _rig.Cut(CameraShot.Overview, Vector3.Zero);
    }

    public void ApplySettings(Settings settings)
    {
        _settings = settings;
        _rig.ReduceMotion = settings.ReduceMotion;
        _rig.AllowShake = settings.CameraShake;
        _sun.ShadowEnabled = settings.Quality >= Quality.High;
        _viewport.Msaa3D = settings.Quality switch
        {
            Quality.Low => Viewport.Msaa.Disabled,
            Quality.Medium => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Msaa4X,
        };
        _viewport.Scaling3DScale = settings.Quality == Quality.Low ? 0.75f : 1f;
    }

    private void BuildTiles()
    {
        _tiles = new TileVisual[_board.Count];
        float gap = 0.06f;
        float pixel = 0.0080f;
        for (int i = 0; i < _board.Count; i++)
        {
            var def = _board.Tiles[i];
            var rect = _layout.Rect(i);
            var visual = new TileVisual();
            var size = new Vector3(rect.Size.X * B - gap, TileHeight, rect.Size.Y * B - gap);
            visual.BaseColor = def.Type switch
            {
                TileType.Street or TileType.Transit or TileType.Utility => new Color(0.66f, 0.68f, 0.72f),
                TileType.Start => new Color(0.74f, 0.63f, 0.4f),
                TileType.GoToJail or TileType.Jail => new Color(0.58f, 0.53f, 0.56f),
                TileType.Plaza => new Color(0.5f, 0.64f, 0.54f),
                _ => new Color(0.56f, 0.6f, 0.68f),
            };
            visual.Material = new StandardMaterial3D { AlbedoColor = visual.BaseColor, Roughness = 0.85f };
            visual.Slab = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size }, Position = World(rect.GetCenter(), TileHeight / 2), MaterialOverride = visual.Material,
            };
            _world.AddChild(visual.Slab);

            var inward = _layout.Inward(i);
            var nameAt = rect.GetCenter();
            if (def.Type == TileType.Street)
            {
                var band = _layout.Band(i);
                _world.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(band.Size.X * B - gap, TileHeight + 0.03f, band.Size.Y * B - gap) },
                    Position = World(band.GetCenter(), (TileHeight + 0.03f) / 2),
                    MaterialOverride = Mat(DistrictColor(def.District), 0.7f),
                });
                nameAt -= inward * _layout.CornerSize * 0.11f;
            }
            visual.Lot = new Node3D { Position = World(def.Type == TileType.Street ? _layout.Band(i).GetCenter() : rect.GetCenter() + inward * _layout.CornerSize * 0.2f, TileHeight + 0.03f) };
            _world.AddChild(visual.Lot);

            float widthWorld = Mathf.Min(rect.Size.X, 1f) * B;
            bool corner = _layout.IsCorner(i);
            var name = new Label3D
            {
                Text = def.Name, Font = Ui.BodyBold, FontSize = corner ? 46 : 38, PixelSize = pixel,
                Modulate = new Color(0.08f, 0.10f, 0.14f), OutlineSize = 0,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, Width = widthWorld / pixel * 0.92f,
                RotationDegrees = new Vector3(-90, 0, 0), Position = World(nameAt, TileHeight + 0.012f),
                HorizontalAlignment = HorizontalAlignment.Center, DoubleSided = false, LineSpacing = -4,
            };
            _world.AddChild(name);

            if (def.IsOwnable)
            {
                var outer = rect.GetCenter() - inward * (_layout.CornerSize * 0.5f - 0.012f);
                bool horizontal = _layout.Side(i) is 0 or 2;
                visual.Strip = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = horizontal ? new Vector3(size.X, 0.34f, 0.28f) : new Vector3(0.28f, 0.34f, size.Z) },
                    Position = World(outer, 0.17f), Visible = false,
                };
                _world.AddChild(visual.Strip);
                visual.Price = new Label3D
                {
                    Font = Ui.MonoBold, FontSize = 34, PixelSize = pixel, Modulate = new Color(0.10f, 0.13f, 0.20f), OutlineSize = 0,
                    RotationDegrees = new Vector3(-90, 0, 0), DoubleSided = false,
                    Position = World(nameAt - inward * _layout.CornerSize * 0.25f + new Vector2(0, horizontal ? 0 : _layout.TileWidth * 0.3f), TileHeight + 0.012f),
                };
                _world.AddChild(visual.Price);
            }
            _tiles[i] = visual;
        }
    }

    // ------------------------------------------------------------------ tokens

    private void BuildTokens(GameState s)
    {
        foreach (var t in _tokens) t.QueueFree();
        _tokens = new Node3D[s.Players.Count];
        for (int i = 0; i < s.Players.Count; i++)
        {
            var color = Tokens.Player(i);
            var body = Mat(color, 0.35f, 0.2f);
            var token = new Node3D();
            token.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = 0.12f, RadialSegments = 20 }, Position = new Vector3(0, 0.06f, 0), MaterialOverride = body });
            Mesh top = (s.Players[i].Token % 8) switch
            {
                0 => new CylinderMesh { TopRadius = 0, BottomRadius = 0.24f, Height = 0.6f, RadialSegments = 16 },
                1 => new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.42f, RadialSegments = 16 },
                2 => new PrismMesh { Size = new Vector3(0.42f, 0.56f, 0.22f) },
                3 => new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.18f, Height = 0.4f, RadialSegments = 6 },
                4 => new SphereMesh { Radius = 0.24f, Height = 0.56f, RadialSegments = 4, Rings = 2 },
                5 => new TorusMesh { InnerRadius = 0.1f, OuterRadius = 0.26f, Rings = 16, RingSegments = 8 },
                6 => new CapsuleMesh { Radius = 0.16f, Height = 0.58f, RadialSegments = 12, Rings = 4 },
                _ => new BoxMesh { Size = new Vector3(0.34f, 0.44f, 0.34f) },
            };
            var mesh = new MeshInstance3D { Mesh = top, Position = new Vector3(0, 0.42f, 0), MaterialOverride = body };
            if (s.Players[i].Token % 8 == 5) mesh.RotationDegrees = new Vector3(90, 0, 0);
            token.AddChild(mesh);
            _world.AddChild(token);
            _tokens[i] = token;
        }
    }

    private Vector3 TokenWorld(GameState s, int player)
    {
        int tile = s.Players[player].Position;
        int total = 0, slot = 0;
        foreach (var p in s.Players)
        {
            if (p.Bankrupt || p.Position != tile) continue;
            if (p.Id == player) slot = total;
            total++;
        }
        return World(_layout.TokenSpot(tile, slot, total), TileHeight);
    }

    private float SideYaw(int tile) => _layout.Side(tile) switch
    {
        1 => -0.9f,
        3 => 0.9f,
        _ => 0,
    };

    private void Settle(GameState s)
    {
        for (int i = 0; i < _tokens.Length && i < s.Players.Count; i++)
        {
            var p = s.Players[i];
            _tokens[i].Visible = !p.Bankrupt;
            if (!p.Bankrupt) _tokens[i].Position = TokenWorld(s, i);
        }
        UpdateTurnRing(s);
    }

    private void UpdateTurnRing(GameState s)
    {
        bool show = !s.IsOver && s.CurrentPlayer < _tokens.Length && !s.Players[s.CurrentPlayer].Bankrupt;
        _turnRing.Visible = show;
        if (!show) return;
        _turnRing.Position = _tokens[s.CurrentPlayer].Position + new Vector3(0, 0.03f, 0);
        ((StandardMaterial3D)_turnRing.MaterialOverride).AlbedoColor = Tokens.Player(s.CurrentPlayer).Lightened(0.35f);
    }

    // ------------------------------------------------------------------ tiles and buildings

    private void RefreshTile(int i, GameState s, bool animate)
    {
        var visual = _tiles[i];
        var prop = s.Property(i);
        if (prop == null) return;
        var def = _board.Tiles[i];

        if (visual.Price != null)
        {
            visual.Price.Text = prop.Owner < 0
                ? Ui.Money(_board.Currency, Calc.PurchasePrice(s, i))
                : prop.Mortgaged ? "MORTGAGED" : Ui.Money(_board.Currency, Calc.Rent(s, i, 7));
            visual.Price.Modulate = prop.Owner < 0 ? new Color(0.36f, 0.4f, 0.5f) : prop.Mortgaged ? new Color(0.6f, 0.3f, 0.3f) : new Color(0.62f, 0.4f, 0.02f);
        }
        if (visual.Owner != prop.Owner || visual.Mortgaged != prop.Mortgaged)
        {
            if (visual.Strip != null)
            {
                visual.Strip.Visible = prop.Owner >= 0;
                if (prop.Owner >= 0) visual.Strip.MaterialOverride = Mat(prop.Mortgaged ? Tokens.Player(prop.Owner).Darkened(0.55f) : Tokens.Player(prop.Owner), 0.5f);
            }
            visual.Material.AlbedoColor = prop.Mortgaged
                ? visual.BaseColor.Darkened(0.45f)
                : prop.Owner >= 0 ? visual.BaseColor.Lerp(Tokens.Player(prop.Owner), 0.16f) : visual.BaseColor;
        }
        bool rebuild = visual.Owner != prop.Owner || visual.Level != prop.Level || visual.Dev != prop.DevType || visual.Mortgaged != prop.Mortgaged;
        bool grew = prop.Level > visual.Level || (visual.Owner < 0 && prop.Owner >= 0);
        visual.Owner = prop.Owner;
        visual.Level = prop.Level;
        visual.Mortgaged = prop.Mortgaged;
        visual.Dev = prop.DevType;
        if (!rebuild) return;

        foreach (var child in visual.Lot.GetChildren()) child.QueueFree();
        if (prop.Owner < 0) return;
        var holder = new Node3D();
        visual.Lot.AddChild(holder);
        Construct(holder, i, def, prop, s);
        if (animate && grew && !_settings.ReduceMotion)
        {
            holder.Scale = new Vector3(1, 0.02f, 1);
            holder.CreateTween().TweenProperty(holder, "scale", Vector3.One, 0.55f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    private void Add(Node3D parent, Mesh mesh, Vector3 position, StandardMaterial3D material, Vector3? rotationDegrees = null)
    {
        var instance = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material };
        if (rotationDegrees is { } r) instance.RotationDegrees = r;
        parent.AddChild(instance);
    }

    /// <summary>The structure that stands on a lot: claim marker, buildings, landmark, station or plant.</summary>
    private void Construct(Node3D lot, int tile, TileDef def, PropertyState prop, GameState s)
    {
        var owner = Tokens.Player(prop.Owner);
        bool alongX = _layout.Side(tile) is 0 or 2;
        var district = DistrictColor(def.District);
        var walls = Mat(new Color(0.96f, 0.94f, 0.9f), 0.8f);

        if (def.Type == TileType.Transit)
        {
            Add(lot, new BoxMesh { Size = new Vector3(1.1f, 0.14f, 0.6f) }, new Vector3(0, 0.07f, 0), Mat(new Color(0.55f, 0.58f, 0.64f)));
            Add(lot, new BoxMesh { Size = new Vector3(1.2f, 0.07f, 0.7f) }, new Vector3(0, 0.62f, 0), Mat(owner, 0.5f));
            foreach (float x in new[] { -0.45f, 0.45f })
                Add(lot, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.5f, RadialSegments = 6 }, new Vector3(x, 0.36f, 0), Mat(new Color(0.3f, 0.32f, 0.38f)));
            return;
        }
        if (def.Type == TileType.Utility)
        {
            Add(lot, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.7f, RadialSegments = 14 }, new Vector3(-0.25f, 0.35f, 0), Mat(new Color(0.75f, 0.78f, 0.84f), 0.4f, 0.4f));
            Add(lot, new BoxMesh { Size = new Vector3(0.6f, 0.04f, 0.5f) }, new Vector3(0.42f, 0.3f, 0), Mat(new Color(0.12f, 0.2f, 0.42f), 0.2f, 0.5f), new Vector3(0, 0, 24));
            Add(lot, new BoxMesh { Size = new Vector3(0.1f, 0.5f, 0.1f) }, new Vector3(-0.25f, 0.95f, 0), Mat(owner));
            return;
        }

        if (prop.Level == 0)
        {
            // Land has been claimed: a flag in the owner's colour.
            Add(lot, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.7f, RadialSegments = 5 }, new Vector3(0, 0.35f, 0), Mat(new Color(0.3f, 0.3f, 0.34f)));
            Add(lot, new BoxMesh { Size = new Vector3(0.34f, 0.2f, 0.03f) }, new Vector3(0.18f, 0.58f, 0), Mat(owner, 0.6f));
            return;
        }

        var type = s.Rules.AdvancedDevelopment ? prop.DevType : DevelopmentType.Residential;
        if (prop.Level == 5)
        {
            // Landmark tower.
            float h = type == DevelopmentType.Industrial ? 1.5f : 2.3f;
            var glass = new StandardMaterial3D
            {
                AlbedoColor = type == DevelopmentType.Luxury ? new Color(0.95f, 0.82f, 0.45f) : district.Lerp(new Color(0.55f, 0.7f, 0.9f), 0.5f),
                Roughness = 0.25f, Metallic = 0.45f, EmissionEnabled = true, Emission = new Color(1f, 0.88f, 0.6f),
                EmissionEnergyMultiplier = 0.15f + _city.Night * 0.7f,
            };
            Add(lot, new BoxMesh { Size = new Vector3(0.62f, h, 0.62f) }, new Vector3(0, h / 2, 0), glass);
            Add(lot, new BoxMesh { Size = new Vector3(0.74f, 0.1f, 0.74f) }, new Vector3(0, h + 0.05f, 0), Mat(district.Darkened(0.2f), 0.5f));
            Add(lot, new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) }, new Vector3(0, h + 0.3f, 0), glass);
            Add(lot, new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.035f, Height = 0.7f, RadialSegments = 5 }, new Vector3(0, h + 0.85f, 0), Mat(owner, 0.3f, 0.6f));
            return;
        }

        float span = (alongX ? _layout.Rect(tile).Size.X : _layout.Rect(tile).Size.Y) * B * 0.86f;
        for (int n = 0; n < prop.Level; n++)
        {
            float offset = -span / 2 + span * (n + 0.5f) / prop.Level;
            var at = alongX ? new Vector3(offset, 0, 0) : new Vector3(0, 0, offset);
            var house = new Node3D { Position = at };
            if (!alongX) house.RotationDegrees = new Vector3(0, 90, 0);
            lot.AddChild(house);
            switch (type)
            {
                case DevelopmentType.Commercial:
                    Add(house, new BoxMesh { Size = new Vector3(0.36f, 0.62f, 0.36f) }, new Vector3(0, 0.31f, 0), Mat(new Color(0.45f, 0.62f, 0.82f), 0.25f, 0.4f));
                    Add(house, new BoxMesh { Size = new Vector3(0.4f, 0.05f, 0.4f) }, new Vector3(0, 0.64f, 0), Mat(district.Darkened(0.25f)));
                    break;
                case DevelopmentType.Luxury:
                    Add(house, new BoxMesh { Size = new Vector3(0.38f, 0.36f, 0.38f) }, new Vector3(0, 0.18f, 0), walls);
                    Add(house, new PrismMesh { Size = new Vector3(0.46f, 0.26f, 0.44f) }, new Vector3(0, 0.49f, 0), Mat(new Color(0.88f, 0.72f, 0.3f), 0.35f, 0.6f));
                    break;
                case DevelopmentType.Industrial:
                    Add(house, new BoxMesh { Size = new Vector3(0.4f, 0.26f, 0.38f) }, new Vector3(0, 0.13f, 0), Mat(new Color(0.55f, 0.56f, 0.6f)));
                    Add(house, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.42f, RadialSegments = 6 }, new Vector3(0.1f, 0.44f, 0.08f), Mat(new Color(0.4f, 0.3f, 0.28f)));
                    break;
                default:
                    Add(house, new BoxMesh { Size = new Vector3(0.36f, 0.28f, 0.36f) }, new Vector3(0, 0.14f, 0), walls);
                    Add(house, new PrismMesh { Size = new Vector3(0.44f, 0.22f, 0.42f) }, new Vector3(0, 0.39f, 0), Mat(district.Darkened(0.15f), 0.7f));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ effects

    private void Highlight(int tile, Color color, float seconds = 0.9f)
    {
        var rect = _layout.Rect(tile);
        var h = _highlights[_nextHighlight];
        _nextHighlight = (_nextHighlight + 1) % _highlights.Count;
        h.Mesh = new BoxMesh { Size = new Vector3(rect.Size.X * B, 0.06f, rect.Size.Y * B) };
        h.Position = World(rect.GetCenter(), TileHeight + 0.06f);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(color, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        h.MaterialOverride = material;
        h.Visible = true;
        var tween = h.CreateTween();
        tween.TweenProperty(material, "albedo_color:a", 0f, seconds);
        tween.TweenCallback(Callable.From(() => h.Visible = false));
    }

    private void FloatText(Vector3 at, string text, Color color)
    {
        var label = _floaters[_nextFloater];
        _nextFloater = (_nextFloater + 1) % _floaters.Count;
        label.Text = text;
        label.Modulate = color;
        label.Position = at + new Vector3(0, 1.1f, 0);
        label.Visible = true;
        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "position:y", at.Y + 2.6f, 1.2f);
        tween.TweenProperty(label, "modulate:a", 0f, 1.2f).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(() => label.Visible = false));
    }

    private void Burst(Vector3 at, Color color)
    {
        var burst = _bursts[_nextBurst];
        _nextBurst = (_nextBurst + 1) % _bursts.Count;
        burst.Position = at + new Vector3(0, 0.4f, 0);
        burst.MaterialOverride = Mat(color, 0.3f, 0.7f);
        burst.Restart();
        burst.Emitting = true;
    }

    private Vector3 AnchorOf(int player) => player >= 0 && player < _tokens.Length ? _tokens[player].Position : new Vector3(0, TileHeight, 0);

    private void Pay(int from, int to, int amount)
    {
        if (amount <= 0) return;
        if (from >= 0)
        {
            FloatText(AnchorOf(from), "-" + amount, Tokens.Bad);
            Burst(AnchorOf(from), Tokens.Accent);
        }
        if (to >= 0) FloatText(AnchorOf(to) + new Vector3(0.3f, 0.3f, 0), "+" + amount, Tokens.Good);
        if (amount >= 300) _rig.Shake(0.35f);
    }

    private async Task Hop(int player, Vector3 to, float seconds, float height)
    {
        var token = _tokens[player];
        var from = token.Position;
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(t =>
        {
            token.Position = from.Lerp(to, t) + new Vector3(0, Mathf.Sin(t * Mathf.Pi) * height, 0);
        }), 0f, 1f, Mathf.Max(0.02f, seconds)).SetTrans(Tween.TransitionType.Sine);
        await Anim.Finished(this, tween);
    }

    // ------------------------------------------------------------------ IGameView

    public void Sync(GameState state)
    {
        _state = state;
        if (_tokens.Length != state.Players.Count) BuildTokens(state);
        _city.Observe(state);
        for (int i = 0; i < _board.Count; i++) RefreshTile(i, state, animate: false);
        Settle(state);
        if (state.Die1 > 0 && !state.IsOver) _dice.ShowResult(state.Die1, state.Die2);
    }

    public void Focus(int tile)
    {
        if (tile < 0)
        {
            _selection.Visible = false;
            _rig.Go(CameraShot.Overview, Vector3.Zero);
            return;
        }
        var rect = _layout.Rect(tile);
        _selection.Mesh = new BoxMesh { Size = new Vector3(rect.Size.X * B + 0.12f, 0.08f, rect.Size.Y * B + 0.12f) };
        _selection.Position = World(rect.GetCenter(), TileHeight + 0.02f);
        _selection.Visible = true;
        _rig.Go(CameraShot.PropertyInspect, World(rect.GetCenter()), SideYaw(tile));
    }

    public async Task Play(GameEvent e, GameState after, AnimContext ctx)
    {
        _state = after;
        if (_tokens.Length != after.Players.Count) Sync(after);
        switch (e)
        {
            case DiceRolled roll:
                _rig.Go(CameraShot.Dice, _dice.Position);
                await _dice.Roll(roll.D1, roll.D2, ctx.Time(0.95f));
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case PlayerMoved move:
                var path = move.Kind switch
                {
                    MoveKind.Walk => _layout.PathForward(move.From, move.To),
                    MoveKind.Backward => _layout.PathBackward(move.From, move.To),
                    _ => new List<int> { move.To },
                };
                _turnRing.Visible = false;
                _rig.Go(CameraShot.TokenFollow, World(_layout.Center(move.To)), SideYaw(move.To));
                float step = move.Kind == MoveKind.Jump ? 0.6f : path.Count > 8 ? 0.1f : 0.15f;
                foreach (int tile in path)
                    await Hop(move.Player, World(_layout.TokenSpot(tile, 0, 1), TileHeight), ctx.Time(step), move.Kind == MoveKind.Jump ? 2.2f : 0.45f);
                _dice.Hide(0.2f, 0.25f);
                Settle(after);
                if (_board.Tiles[move.To].IsOwnable)
                {
                    _rig.Go(CameraShot.PropertyInspect, World(_layout.Center(move.To)), SideYaw(move.To));
                    await Anim.Wait(this, ctx.Time(0.35f));
                }
                break;

            case PlayerSentToJail jail:
                Highlight(jail.JailTile, Tokens.Bad);
                _rig.Go(CameraShot.TokenFollow, World(_layout.Center(jail.JailTile)), SideYaw(jail.JailTile));
                await Hop(jail.Player, World(_layout.TokenSpot(jail.JailTile, 0, 1), TileHeight), ctx.Time(0.7f), 3f);
                _rig.Shake(0.2f);
                Settle(after);
                break;

            case PropertyPurchased buy:
                RefreshTile(buy.Tile, after, animate: true);
                Highlight(buy.Tile, Tokens.Player(buy.Player));
                Burst(World(_layout.Center(buy.Tile), TileHeight), Tokens.Accent);
                FloatText(AnchorOf(buy.Player), "-" + buy.Price, Tokens.Bad);
                _city.Observe(after);
                await Anim.Wait(this, ctx.Time(0.6f));
                break;

            case PropertyGranted grant:
                RefreshTile(grant.Tile, after, animate: true);
                _city.Observe(after);
                await Anim.Wait(this, ctx.Time(0.08f));
                break;

            case AuctionStarted auction:
                _rig.Go(CameraShot.Auction, World(_layout.Center(auction.Tile)), SideYaw(auction.Tile));
                Highlight(auction.Tile, Tokens.Accent, 1.4f);
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case AuctionCompleted auction:
                RefreshTile(auction.Tile, after, animate: true);
                if (auction.Winner >= 0)
                {
                    Highlight(auction.Tile, Tokens.Player(auction.Winner));
                    Burst(World(_layout.Center(auction.Tile), TileHeight), Tokens.Player(auction.Winner));
                }
                _city.Observe(after);
                await Anim.Wait(this, ctx.Time(0.6f));
                break;

            case BuildingConstructed build:
                _rig.Go(CameraShot.PropertyInspect, World(_layout.Center(build.Tile)), SideYaw(build.Tile));
                RefreshTile(build.Tile, after, animate: true);
                Burst(World(_layout.Center(build.Tile), TileHeight), build.Level == 5 ? Tokens.Accent : new Color(0.85f, 0.85f, 0.85f));
                if (build.Level == 5) _rig.Shake(0.25f);
                _city.Observe(after);
                await Anim.Wait(this, ctx.Time(0.6f));
                break;

            case BuildingSold sold:
                RefreshTile(sold.Tile, after, animate: false);
                Highlight(sold.Tile, Tokens.Bad, 0.5f);
                _city.Observe(after);
                await Anim.Wait(this, ctx.Time(0.2f));
                break;

            case PropertyMortgaged mortgaged:
                RefreshTile(mortgaged.Tile, after, animate: false);
                await Anim.Wait(this, ctx.Time(0.15f));
                break;

            case PropertyUnmortgaged lifted:
                RefreshTile(lifted.Tile, after, animate: false);
                await Anim.Wait(this, ctx.Time(0.15f));
                break;

            case RentPaid rent:
                Highlight(rent.Tile, Tokens.Player(rent.Payee));
                Pay(rent.Payer, rent.Payee, rent.Amount);
                RefreshTile(rent.Tile, after, animate: false);
                await Anim.Wait(this, ctx.Time(0.7f));
                break;

            case MoneyTransferred money:
                Pay(money.From, money.To, money.Amount);
                await Anim.Wait(this, ctx.Time(0.5f));
                break;

            case DebtPaid debt:
                Pay(debt.Debt.Debtor, debt.Debt.Creditor, debt.Debt.Amount);
                await Anim.Wait(this, ctx.Time(0.5f));
                break;

            case SalaryPaid salary:
                FloatText(AnchorOf(salary.Player), "+" + salary.Amount, Tokens.Good);
                await Anim.Wait(this, ctx.Time(0.3f));
                break;

            case CardDrawn:
                await Anim.Wait(this, ctx.Time(1.3f));
                break;

            case ObjectiveCompleted objective:
                Burst(AnchorOf(objective.Player), Tokens.Accent);
                FloatText(AnchorOf(objective.Player), "+" + objective.Reward, Tokens.Good);
                await Anim.Wait(this, ctx.Time(0.6f));
                break;

            case TradeAccepted or OptionExercised:
                _rig.Go(CameraShot.Trade, Vector3.Zero);
                for (int i = 0; i < _board.Count; i++)
                {
                    int before = _tiles[i].Owner;
                    RefreshTile(i, after, animate: true);
                    if (_tiles[i].Owner != before && _tiles[i].Owner >= 0) Highlight(i, Tokens.Player(_tiles[i].Owner));
                }
                await Anim.Wait(this, ctx.Time(0.8f));
                break;

            case PlayerBankrupt bankrupt:
                _rig.Shake(0.5f);
                Burst(AnchorOf(bankrupt.Player), Tokens.Bad);
                for (int i = 0; i < _board.Count; i++) RefreshTile(i, after, animate: true);
                _city.Observe(after);
                Settle(after);
                await Anim.Wait(this, ctx.Time(1.0f));
                break;

            case EconomyChanged or CityEventOccurred or ProjectCompleted or ProjectProposed:
                _rig.Go(CameraShot.Event, Vector3.Zero);
                _city.Observe(after);
                for (int i = 0; i < _board.Count; i++)
                {
                    RefreshTile(i, after, animate: false);
                    if (e is not ProjectProposed && after.Effects.Count > 0 && _board.Tiles[i].IsOwnable && Calc.EffectApplies(after, after.Effects[^1], i))
                        Highlight(i, Tokens.Info, 1.4f);
                    if (_nextHighlight == 0 && i > 6) break;
                }
                await Anim.Wait(this, ctx.Time(0.9f));
                break;

            case RoundStarted:
                for (int i = 0; i < _board.Count; i++) RefreshTile(i, after, animate: false);
                break;

            case TurnStarted:
                Settle(after);
                _rig.Go(CameraShot.Overview, Vector3.Zero);
                await Anim.Wait(this, ctx.Time(0.2f));
                break;

            case GameEnded ended:
                Settle(after);
                _turnRing.Visible = false;
                int winner = ended.Winners.Length > 0 ? ended.Winners[0] : -1;
                _rig.Go(CameraShot.Victory, AnchorOf(winner));
                for (int i = 0; i < 4; i++)
                {
                    Burst(AnchorOf(winner) + new Vector3((float)_random.NextDouble() * 2 - 1, 0.5f, (float)_random.NextDouble() * 2 - 1), Tokens.Players[_random.Next(Tokens.Players.Length)]);
                    await Anim.Wait(this, ctx.Time(0.35f));
                }
                break;
        }
    }

    // ------------------------------------------------------------------ input

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _rig.Zoom(0.9f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _rig.Zoom(1.1f);
                break;
            case InputEventMagnifyGesture magnify:
                _rig.Zoom(1f / Mathf.Max(0.5f, magnify.Factor));
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _pressed = true;
                    _dragged = false;
                    _pressAt = button.Position;
                }
                else if (_pressed)
                {
                    _pressed = false;
                    if (_dragged) break;
                    double now = Time.GetTicksMsec() / 1000.0;
                    bool doubleTap = now - _lastTap < 0.3;
                    _lastTap = now;
                    int tile = Pick(button.Position);
                    if (tile >= 0) TileTapped?.Invoke(tile);
                    else if (doubleTap) _rig.ResetUser();
                }
                break;
            case InputEventMouseMotion motion when _pressed:
                if (motion.Position.DistanceTo(_pressAt) > 10) _dragged = true;
                if (_dragged) _rig.Orbit(-motion.Relative.X * 0.006f);
                break;
        }
    }

    private int Pick(Vector2 screen)
    {
        var camera = _rig.Camera;
        var scale = new Vector2(_viewport.Size.X / Mathf.Max(1, Size.X), _viewport.Size.Y / Mathf.Max(1, Size.Y));
        var point = screen * scale;
        var origin = camera.ProjectRayOrigin(point);
        var direction = camera.ProjectRayNormal(point);
        if (Mathf.Abs(direction.Y) < 0.0001f) return -1;
        float t = (TileHeight - origin.Y) / direction.Y;
        if (t < 0) return -1;
        var hit = origin + direction * t;
        return _layout.TileAt(new Vector2(hit.X / B + 0.5f, hit.Z / B + 0.5f));
    }
}
