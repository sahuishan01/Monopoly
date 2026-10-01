using BoardEmpire.Core;
using Game.Core.State;
using Godot;

namespace BoardEmpire.View3D;

/// <summary>
/// Purely cosmetic life around the board: a downtown skyline that grows with the match, traffic,
/// pedestrians, a train, clouds, weather and a day/night cycle. Nothing here is replicated or
/// can influence the game; it only reads how developed the board currently is.
/// </summary>
public partial class CityAmbience : Node3D
{
    private sealed class Mover
    {
        public Node3D Node = null!;
        public float Offset, Speed, Lane;
    }

    private readonly List<Mover> _cars = new();
    private readonly List<Mover> _walkers = new();
    private readonly List<Node3D> _clouds = new();
    private readonly List<(MeshInstance3D Mesh, float Height, float Threshold)> _towers = new();
    private readonly List<StandardMaterial3D> _windowMaterials = new();
    private readonly Random _random = new(11);
    private MultiMeshInstance3D _trees = null!, _trunks = null!, _lamps = null!, _lampHeads = null!;
    private StandardMaterial3D _lampMaterial = null!;
    private Node3D _train = null!, _plane = null!;
    private CpuParticles3D _rain = null!;
    private DirectionalLight3D _sun = null!;
    private Environment _environment = null!;
    private ProceduralSkyMaterial _sky = null!;
    private float _board, _inner;
    private float _progress, _shownProgress = -1;
    private float _clock = 0.32f;
    private float _trainAt, _planeAt = -1;
    private int _maxCars = 8, _maxWalkers = 8;
    private bool _animate = true;

    /// <summary>0 = empty board, 1 = fully built city.</summary>
    public float Progress => _progress;
    public float Night { get; private set; }

    public void Build(float boardSize, float innerSize, DirectionalLight3D sun, Environment environment, ProceduralSkyMaterial sky, Quality quality, bool reduceMotion)
    {
        _board = boardSize;
        _inner = innerSize;
        _sun = sun;
        _environment = environment;
        _sky = sky;
        _animate = !reduceMotion;
        _maxCars = quality switch { Quality.Low => 4, Quality.Medium => 8, Quality.High => 12, _ => 16 };
        _maxWalkers = quality == Quality.Low ? 0 : _maxCars;

        float half = innerSize / 2;
        // Ring road just inside the tiles.
        float road = half - innerSize * 0.06f;
        var asphalt = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.18f, 0.23f), Roughness = 0.95f };
        float width = innerSize * 0.07f;
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(0, 0.02f, road), new Vector3(road * 2 + width, 0.04f, width)),
                     (new Vector3(0, 0.02f, -road), new Vector3(road * 2 + width, 0.04f, width)),
                     (new Vector3(road, 0.02f, 0), new Vector3(width, 0.04f, road * 2 + width)),
                     (new Vector3(-road, 0.02f, 0), new Vector3(width, 0.04f, road * 2 + width)),
                 })
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = asphalt });

        // Park in the middle.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(road * 2 - width, 0.03f, road * 2 - width) },
            Position = new Vector3(0, 0.015f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.17f, 0.3f, 0.24f), Roughness = 1f },
        });

        BuildSkyline(road - width);
        BuildTrees(road - width, quality);
        BuildLamps(road, width);
        BuildMovers(road, width);
        BuildSkyObjects();

        _rain = new CpuParticles3D
        {
            Emitting = false, Amount = quality == Quality.Low ? 120 : 420, Lifetime = 1.1f,
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.5f, 0.03f) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(boardSize * 0.6f, 0.2f, boardSize * 0.6f),
            Direction = Vector3.Down, Spread = 4, Gravity = new Vector3(0, -30, 0), InitialVelocityMin = 12, InitialVelocityMax = 16,
            Position = new Vector3(0, boardSize * 0.55f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.7f, 0.8f, 1f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(_rain);
        ApplyDaylight();
    }

    private void BuildSkyline(float extent)
    {
        // Downtown towers sit in the centre and rise as the board develops.
        var glass = new[] { new Color(0.42f, 0.55f, 0.72f), new Color(0.55f, 0.6f, 0.7f), new Color(0.35f, 0.46f, 0.6f), new Color(0.62f, 0.56f, 0.5f) };
        int count = 14;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.399f;
            float radius = extent * (0.12f + 0.5f * Mathf.Sqrt((i + 0.5f) / count));
            var pos = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius * 0.8f - extent * 0.12f);
            float footprint = extent * (0.1f + 0.05f * (float)_random.NextDouble());
            float height = extent * (0.1f + 0.34f * (1f - (float)i / count) * (0.6f + 0.4f * (float)_random.NextDouble()));
            var material = new StandardMaterial3D
            {
                AlbedoColor = glass[i % glass.Length], Roughness = 0.35f, Metallic = 0.3f,
                EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.55f), EmissionEnergyMultiplier = 0,
            };
            _windowMaterials.Add(material);
            var tower = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(footprint, 1, footprint) }, Position = pos, MaterialOverride = material,
                Scale = new Vector3(1, 0.001f, 1), Visible = false,
            };
            AddChild(tower);
            _towers.Add((tower, height, 0.12f + 0.8f * i / count));
        }
    }

    private void BuildTrees(float extent, Quality quality)
    {
        int count = quality switch { Quality.Low => 24, Quality.Medium => 48, _ => 80 };
        var crown = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.32f, Height = 0.9f, RadialSegments = 7 },
            InstanceCount = count,
        };
        var trunk = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 0.4f, RadialSegments = 5 },
            InstanceCount = count,
        };
        for (int i = 0; i < count; i++)
        {
            // Trees keep to the edge of the park so that the skyline has room.
            float t = (float)_random.NextDouble() * 4;
            float along = ((float)_random.NextDouble() * 2 - 1) * extent * 0.92f;
            float edge = extent * (0.8f + 0.16f * (float)_random.NextDouble());
            var p = ((int)t) switch
            {
                0 => new Vector3(along, 0, edge),
                1 => new Vector3(along, 0, -edge),
                2 => new Vector3(edge, 0, along),
                _ => new Vector3(-edge, 0, along),
            };
            float s = 0.8f + (float)_random.NextDouble() * 0.7f;
            crown.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), p + new Vector3(0, 0.75f * s, 0)));
            trunk.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), p + new Vector3(0, 0.2f * s, 0)));
        }
        _trees = new MultiMeshInstance3D { Multimesh = crown, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.5f, 0.32f), Roughness = 0.9f } };
        _trunks = new MultiMeshInstance3D { Multimesh = trunk, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.26f, 0.2f), Roughness = 1f } };
        AddChild(_trees);
        AddChild(_trunks);
    }

    private void BuildLamps(float road, float width)
    {
        const int perSide = 6;
        int count = perSide * 4;
        var poles = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 0.9f, RadialSegments = 5 },
            InstanceCount = count,
        };
        var heads = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3 },
            InstanceCount = count,
        };
        float inside = road - width * 0.75f;
        for (int i = 0; i < count; i++)
        {
            float along = -inside + (i % perSide + 0.5f) * (inside * 2 / perSide);
            var p = (i / perSide) switch
            {
                0 => new Vector3(along, 0, inside),
                1 => new Vector3(along, 0, -inside),
                2 => new Vector3(inside, 0, along),
                _ => new Vector3(-inside, 0, along),
            };
            poles.SetInstanceTransform(i, new Transform3D(Basis.Identity, p + new Vector3(0, 0.45f, 0)));
            heads.SetInstanceTransform(i, new Transform3D(Basis.Identity, p + new Vector3(0, 0.95f, 0)));
        }
        _lampMaterial = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.93f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.5f), EmissionEnergyMultiplier = 0 };
        _lamps = new MultiMeshInstance3D { Multimesh = poles, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.27f, 0.32f) } };
        _lampHeads = new MultiMeshInstance3D { Multimesh = heads, MaterialOverride = _lampMaterial };
        AddChild(_lamps);
        AddChild(_lampHeads);
    }

    private void BuildMovers(float road, float width)
    {
        var colors = new[] { new Color("#ef5b5b"), new Color("#f2b33d"), new Color("#e8ecf4"), new Color("#4ea3ff"), new Color("#3ecf8e"), new Color("#b47bff") };
        for (int i = 0; i < _maxCars; i++)
        {
            var car = new Node3D { Visible = false };
            car.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.2f, 0.32f) }, Position = new Vector3(0, 0.16f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colors[i % colors.Length], Roughness = 0.4f, Metallic = 0.3f },
            });
            car.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.14f, 0.28f) }, Position = new Vector3(-0.04f, 0.32f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.2f, 0.28f), Roughness = 0.2f },
            });
            AddChild(car);
            bool clockwise = i % 2 == 0;
            _cars.Add(new Mover
            {
                Node = car, Offset = (float)i / _maxCars, Speed = (0.035f + 0.02f * (float)_random.NextDouble()) * (clockwise ? 1 : -1),
                Lane = road + (clockwise ? width * 0.2f : -width * 0.2f),
            });
        }
        for (int i = 0; i < _maxWalkers; i++)
        {
            var walker = new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.07f, Height = 0.3f, RadialSegments = 6, Rings = 2 }, Visible = false,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colors[(i + 2) % colors.Length].Lightened(0.2f) },
            };
            AddChild(walker);
            _walkers.Add(new Mover
            {
                Node = walker, Offset = (float)_random.NextDouble(), Speed = (0.006f + 0.006f * (float)_random.NextDouble()) * (i % 2 == 0 ? 1 : -1),
                Lane = road - width * 0.72f,
            });
        }

        // A commuter train on a track outside the board.
        _train = new Node3D { Visible = false };
        for (int c = 0; c < 3; c++)
        {
            _train.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.42f, 0.5f) }, Position = new Vector3(-c * 1.62f, 0.3f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = c == 0 ? new Color("#f2b33d") : new Color("#e8ecf4"), Roughness = 0.4f },
            });
        }
        AddChild(_train);
        float rail = _board / 2 + 1.0f;
        var railMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.22f, 0.26f) };
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(0, -0.05f, rail), new Vector3(rail * 2 + 0.7f, 0.06f, 0.7f)), (new Vector3(0, -0.05f, -rail), new Vector3(rail * 2 + 0.7f, 0.06f, 0.7f)),
                     (new Vector3(rail, -0.05f, 0), new Vector3(0.7f, 0.06f, rail * 2 + 0.7f)), (new Vector3(-rail, -0.05f, 0), new Vector3(0.7f, 0.06f, rail * 2 + 0.7f)),
                 })
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = railMaterial });
    }

    private void BuildSkyObjects()
    {
        var cloudMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 1, 1, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        for (int i = 0; i < 7; i++)
        {
            var cloud = new Node3D
            {
                Position = new Vector3(((float)_random.NextDouble() * 2 - 1) * _board, _board * (0.42f + 0.1f * (float)_random.NextDouble()), ((float)_random.NextDouble() * 2 - 1) * _board * 0.8f),
            };
            for (int puff = 0; puff < 3; puff++)
            {
                cloud.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 1.2f, Height = 1.3f, RadialSegments = 8, Rings = 4 },
                    Position = new Vector3(puff * 1.3f - 1.3f, puff == 1 ? 0.25f : 0, 0), Scale = new Vector3(1.4f, 0.55f, 1f),
                    MaterialOverride = cloudMaterial,
                });
            }
            AddChild(cloud);
            _clouds.Add(cloud);
        }
        _plane = new Node3D { Visible = false };
        var body = new StandardMaterial3D { AlbedoColor = new Color("#e8ecf4") };
        _plane.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.18f, Height = 1.6f }, RotationDegrees = new Vector3(0, 0, 90), MaterialOverride = body });
        _plane.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.04f, 1.7f) }, MaterialOverride = body });
        AddChild(_plane);
    }

    /// <summary>Recomputes how alive the city should be from the display state.</summary>
    public void Observe(GameState s)
    {
        int ownable = 0, owned = 0, levels = 0, streets = 0;
        foreach (var p in s.Properties)
        {
            if (p == null) continue;
            ownable++;
            if (p.Owner >= 0) owned++;
            if (s.Board.Tiles[p.Tile].Type == Game.Core.Board.TileType.Street)
            {
                streets++;
                levels += p.Level;
            }
        }
        float ownership = ownable > 0 ? (float)owned / ownable : 0;
        float development = streets > 0 ? levels / (streets * 5f) : 0;
        _progress = Mathf.Clamp(ownership * 0.4f + development * 1.4f, 0, 1);
        _rain.Emitting = _animate && s.Effects.Any(e => e.Name == "Flash Flood");
        _trainOn = s.Properties.Any(p => p != null && p.Owner >= 0 && s.Board.Tiles[p.Tile].Type == Game.Core.Board.TileType.Transit);
        if (Mathf.Abs(_progress - _shownProgress) > 0.01f) ApplyProgress();
    }

    private bool _trainOn;

    private void ApplyProgress()
    {
        _shownProgress = _progress;
        foreach (var (mesh, height, threshold) in _towers)
        {
            float grown = Mathf.Clamp((_progress - threshold * 0.7f) / 0.3f, 0, 1);
            float target = Mathf.Max(0.001f, height * grown);
            mesh.Visible = grown > 0;
            if (!mesh.Visible) continue;
            var tween = CreateTween().SetParallel();
            tween.TweenProperty(mesh, "scale:y", target, _animate ? 1.4f : 0.01f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(mesh, "position:y", target / 2, _animate ? 1.4f : 0.01f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        int visibleTrees = (int)Mathf.Lerp(_trees.Multimesh.InstanceCount * 0.25f, _trees.Multimesh.InstanceCount, Mathf.Min(1, _progress * 2.5f));
        _trees.Multimesh.VisibleInstanceCount = visibleTrees;
        _trunks.Multimesh.VisibleInstanceCount = visibleTrees;
        int cars = Mathf.RoundToInt(Mathf.Lerp(1, _maxCars, Mathf.Min(1, _progress * 1.8f)));
        for (int i = 0; i < _cars.Count; i++) _cars[i].Node.Visible = i < cars;
        int walkers = Mathf.RoundToInt(Mathf.Lerp(0, _maxWalkers, Mathf.Clamp((_progress - 0.1f) * 2.2f, 0, 1)));
        for (int i = 0; i < _walkers.Count; i++) _walkers[i].Node.Visible = i < walkers;
    }

    /// <summary>Point on a square loop of half-size r; t wraps in [0,1). Also returns the heading.</summary>
    private static (Vector3 Position, float Yaw) Loop(float t, float r)
    {
        t = Mathf.PosMod(t, 1f) * 4;
        int side = (int)t;
        float k = (t - side) * 2 - 1;
        return side switch
        {
            0 => (new Vector3(k * r, 0, r), 0),
            1 => (new Vector3(r, 0, -k * r), Mathf.Pi / 2),
            2 => (new Vector3(-k * r, 0, -r), Mathf.Pi),
            _ => (new Vector3(-r, 0, k * r), -Mathf.Pi / 2),
        };
    }

    public override void _Process(double delta)
    {
        if (!_animate || _sun == null) return;
        float dt = (float)delta;

        foreach (var car in _cars)
        {
            if (!car.Node.Visible) continue;
            car.Offset += car.Speed * dt;
            var (pos, yaw) = Loop(car.Offset, car.Lane);
            car.Node.Position = pos;
            car.Node.Rotation = new Vector3(0, yaw + (car.Speed < 0 ? Mathf.Pi : 0), 0);
        }
        foreach (var walker in _walkers)
        {
            if (!walker.Node.Visible) continue;
            walker.Offset += walker.Speed * dt;
            var (pos, _) = Loop(walker.Offset, walker.Lane);
            walker.Node.Position = pos + new Vector3(0, 0.17f + Mathf.Abs(Mathf.Sin(walker.Offset * 900)) * 0.03f, 0);
        }
        foreach (var cloud in _clouds)
        {
            cloud.Position += new Vector3(dt * 0.5f, 0, 0);
            if (cloud.Position.X > _board * 1.2f) cloud.Position = cloud.Position with { X = -_board * 1.2f };
        }

        _train.Visible = _trainOn;
        if (_trainOn)
        {
            _trainAt += dt * 0.03f;
            var (pos, yaw) = Loop(_trainAt, _board / 2 + 1.0f);
            _train.Position = pos;
            _train.Rotation = new Vector3(0, yaw, 0);
        }

        // Air traffic only shows up once the city has grown.
        if (_planeAt < 0 && _progress > 0.5f && _random.NextDouble() < dt / 40) _planeAt = 0;
        if (_planeAt >= 0)
        {
            _planeAt += dt / 14;
            _plane.Visible = true;
            _plane.Position = new Vector3(Mathf.Lerp(-_board * 1.3f, _board * 1.3f, _planeAt), _board * 0.5f, -_board * 0.3f);
            if (_planeAt > 1)
            {
                _planeAt = -1;
                _plane.Visible = false;
            }
        }

        _clock = Mathf.PosMod(_clock + dt / 300f, 1f);
        ApplyDaylight();
    }

    private void ApplyDaylight()
    {
        // _clock: 0 = midnight, 0.5 = noon.
        float sunHeight = Mathf.Sin((_clock - 0.25f) * Mathf.Tau);
        float day = Mathf.Clamp(sunHeight * 2.2f + 0.45f, 0, 1);
        Night = 1 - day;
        _sun.RotationDegrees = new Vector3(-Mathf.Lerp(22, 62, Mathf.Clamp(sunHeight, 0, 1)), 35 + _clock * 60, 0);
        _sun.LightEnergy = Mathf.Lerp(0.16f, 1.0f, day);
        _sun.LightColor = new Color(1f, 0.82f, 0.62f).Lerp(new Color(1f, 0.98f, 0.94f), Mathf.Clamp(sunHeight * 1.5f, 0, 1)).Lerp(new Color(0.55f, 0.62f, 0.9f), Night);
        _environment.AmbientLightEnergy = Mathf.Lerp(0.34f, 0.66f, day);
        _sky.SkyTopColor = new Color(0.03f, 0.05f, 0.12f).Lerp(new Color(0.22f, 0.42f, 0.78f), day);
        _sky.SkyHorizonColor = new Color(0.1f, 0.12f, 0.22f).Lerp(new Color(0.72f, 0.82f, 0.94f), day).Lerp(new Color(0.98f, 0.62f, 0.4f), Mathf.Clamp(1 - Mathf.Abs(sunHeight) * 4, 0, 1) * 0.6f);
        _sky.GroundHorizonColor = _sky.SkyHorizonColor.Darkened(0.25f);
        _sky.GroundBottomColor = new Color(0.05f, 0.07f, 0.11f);
        float glow = Mathf.Clamp(Night * 1.6f - 0.2f, 0, 1);
        _lampMaterial.EmissionEnergyMultiplier = glow * 3f;
        foreach (var m in _windowMaterials) m.EmissionEnergyMultiplier = glow * 0.55f;
    }
}
