using Godot;

namespace BoardEmpire.View3D;

/// <summary>
/// A pair of dice that always lands on the values it is told to show. The roll is pure theatre:
/// the result comes from the authority and the tumble is steered to end on it.
/// </summary>
public partial class Dice3D : Node3D
{
    private static readonly ImageTexture?[] Faces = new ImageTexture?[7];
    private readonly Node3D[] _dice = new Node3D[2];
    private readonly Random _random = new();
    private float _size = 1.1f;

    // Face value -> rotation that turns that face upwards. 1:+Y 6:-Y 2:+Z 5:-Z 3:+X 4:-X.
    private static readonly Vector3[] UpRotation =
    {
        Vector3.Zero,
        new(0, 0, 0),
        new(-Mathf.Pi / 2, 0, 0),
        new(0, 0, Mathf.Pi / 2),
        new(0, 0, -Mathf.Pi / 2),
        new(Mathf.Pi / 2, 0, 0),
        new(Mathf.Pi, 0, 0),
    };

    private static ImageTexture Face(int value)
    {
        if (Faces[value] != null) return Faces[value]!;
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        image.Fill(new Color(0.97f, 0.96f, 0.93f));
        void Pip(int gx, int gy)
        {
            int cx = n / 2 + gx * 17, cy = n / 2 + gy * 17;
            for (int y = -7; y <= 7; y++)
                for (int x = -7; x <= 7; x++)
                    if (x * x + y * y <= 44) image.SetPixel(cx + x, cy + y, new Color(0.08f, 0.09f, 0.14f));
        }
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
        var texture = ImageTexture.CreateFromImage(image);
        Faces[value] = texture;
        return texture;
    }

    public void Build(float size)
    {
        _size = size;
        for (int d = 0; d < 2; d++)
        {
            var die = new Node3D { Visible = false };
            var body = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One * size * 0.98f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.96f, 0.93f), Roughness = 0.35f },
            };
            die.AddChild(body);
            (Vector3 Normal, Vector3 Rotation, int Value)[] faces =
            {
                (Vector3.Up, new Vector3(-Mathf.Pi / 2, 0, 0), 1),
                (Vector3.Down, new Vector3(Mathf.Pi / 2, 0, 0), 6),
                (Vector3.Back, Vector3.Zero, 2),
                (Vector3.Forward, new Vector3(0, Mathf.Pi, 0), 5),
                (Vector3.Right, new Vector3(0, Mathf.Pi / 2, 0), 3),
                (Vector3.Left, new Vector3(0, -Mathf.Pi / 2, 0), 4),
            };
            foreach (var (normal, rotation, value) in faces)
            {
                die.AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = Vector2.One * size * 0.92f },
                    Position = normal * size * 0.495f,
                    Rotation = rotation,
                    MaterialOverride = new StandardMaterial3D { AlbedoTexture = Face(value), Roughness = 0.4f },
                });
            }
            AddChild(die);
            _dice[d] = die;
        }
    }

    public void ShowResult(int d1, int d2)
    {
        Place(0, d1, 0.3f);
        Place(1, d2, -0.4f);
    }

    private Vector3 Rest(int index) => new((index == 0 ? -1 : 1) * _size * 0.85f, _size / 2, index == 0 ? 0.2f : -0.25f);

    private void Place(int index, int value, float yaw)
    {
        var die = _dice[index];
        die.Visible = true;
        die.Position = Rest(index);
        die.Basis = Basis.FromEuler(new Vector3(0, yaw, 0)) * Basis.FromEuler(UpRotation[Math.Clamp(value, 1, 6)]);
        die.Scale = Vector3.One;
    }

    public void Hide(float delay, float fade)
    {
        foreach (var die in _dice)
        {
            var tween = CreateTween();
            tween.TweenInterval(delay);
            tween.TweenProperty(die, "scale", Vector3.One * 0.01f, fade);
            tween.TweenCallback(Callable.From(() => die.Visible = false));
        }
    }

    /// <summary>Throws both dice; completes when they have settled on the given values.</summary>
    public async Task Roll(int d1, int d2, float seconds)
    {
        int[] values = { d1, d2 };
        Tween? last = null;
        for (int i = 0; i < 2; i++)
        {
            var die = _dice[i];
            die.Visible = true;
            die.Scale = Vector3.One;
            var rest = Rest(i);
            die.Position = rest + new Vector3((i == 0 ? -1 : 1) * _size * 2.5f, _size * 5f, _size * 3f);
            var spin = new Vector3((float)_random.NextDouble() * 9 + 6, (float)_random.NextDouble() * 6, (float)_random.NextDouble() * 9 + 6);
            float yaw = (float)_random.NextDouble() * 1.2f - 0.6f;
            var final = Basis.FromEuler(new Vector3(0, yaw, 0)) * Basis.FromEuler(UpRotation[Math.Clamp(values[i], 1, 6)]);
            var start = die.Position;

            var tween = CreateTween();
            tween.TweenMethod(Callable.From<float>(t =>
            {
                // Two bounces of decreasing height while the tumble slows into the final face.
                float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.Pi * 2.5f)) * (1 - t) * (1 - t) * _size * 2.2f;
                float ease = 1 - (1 - t) * (1 - t);
                die.Position = start.Lerp(rest, ease) + new Vector3(0, bounce, 0);
                float remaining = (1 - ease);
                die.Basis = Basis.FromEuler(spin * remaining * 2f) * final;
            }), 0f, 1f, Mathf.Max(0.05f, seconds));
            last = tween;
        }
        if (last != null && IsInsideTree()) await ToSignal(last, Tween.SignalName.Finished);
    }
}
