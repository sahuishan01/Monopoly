using Godot;

namespace BoardEmpire.Presentation;

/// <summary>
/// Geometry of a square ring board in the unit square (y grows downwards). Tile 0 is the
/// bottom-right corner and play proceeds clockwise, for any tile count divisible by four.
/// Both the 2D and the 3D view derive their positions from this single source.
/// </summary>
public sealed class BoardLayout
{
    private readonly Rect2[] _rects;
    private readonly int[] _sides;

    public int Count { get; }
    public float TileWidth { get; }
    public float CornerSize { get; }

    public BoardLayout(int tileCount)
    {
        Count = tileCount;
        int perSide = tileCount / 4;
        int regular = perSide - 1;
        TileWidth = 1f / (regular + 3.2f);
        CornerSize = 1.6f * TileWidth;
        float w = TileWidth, c = CornerSize;
        _rects = new Rect2[tileCount];
        _sides = new int[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            int side = i / perSide, k = i % perSide;
            _sides[i] = side;
            if (k == 0)
            {
                _rects[i] = side switch
                {
                    0 => new Rect2(1 - c, 1 - c, c, c),
                    1 => new Rect2(0, 1 - c, c, c),
                    2 => new Rect2(0, 0, c, c),
                    _ => new Rect2(1 - c, 0, c, c),
                };
                continue;
            }
            _rects[i] = side switch
            {
                0 => new Rect2(1 - c - k * w, 1 - c, w, c),
                1 => new Rect2(0, 1 - c - k * w, c, w),
                2 => new Rect2(c + (k - 1) * w, 0, w, c),
                _ => new Rect2(1 - c, c + (k - 1) * w, c, w),
            };
        }
    }

    public Rect2 Rect(int tile) => _rects[tile];

    public Vector2 Center(int tile) => _rects[tile].GetCenter();

    /// <summary>0 bottom, 1 left, 2 top, 3 right.</summary>
    public int Side(int tile) => _sides[tile];

    public bool IsCorner(int tile) => tile % (Count / 4) == 0;

    /// <summary>Unit vector pointing from the tile towards the middle of the board.</summary>
    public Vector2 Inward(int tile) => _sides[tile] switch
    {
        0 => Vector2.Up,
        1 => Vector2.Right,
        2 => Vector2.Down,
        _ => Vector2.Left,
    };

    /// <summary>The strip along the inner edge that carries the district colour.</summary>
    public Rect2 Band(int tile, float fraction = 0.22f)
    {
        var r = _rects[tile];
        float d = CornerSize * fraction;
        return _sides[tile] switch
        {
            0 => new Rect2(r.Position.X, r.Position.Y, r.Size.X, d),
            1 => new Rect2(r.End.X - d, r.Position.Y, d, r.Size.Y),
            2 => new Rect2(r.Position.X, r.End.Y - d, r.Size.X, d),
            _ => new Rect2(r.Position.X, r.Position.Y, d, r.Size.Y),
        };
    }

    /// <summary>Where the n-th of several tokens stands on a tile so that they do not overlap.</summary>
    public Vector2 TokenSpot(int tile, int slot, int total)
    {
        var r = _rects[tile];
        var center = r.GetCenter() - Inward(tile) * CornerSize * 0.12f;
        if (total <= 1) return center;
        float radius = Mathf.Min(r.Size.X, r.Size.Y) * 0.24f;
        float angle = Mathf.Tau * slot / total;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    public int TileAt(Vector2 unit)
    {
        for (int i = 0; i < Count; i++)
            if (_rects[i].HasPoint(unit)) return i;
        return -1;
    }

    /// <summary>Tiles a token walks through, including the destination, moving forwards.</summary>
    public List<int> PathForward(int from, int to)
    {
        var path = new List<int>();
        int i = from;
        while (i != to)
        {
            i = (i + 1) % Count;
            path.Add(i);
            if (path.Count > Count) break;
        }
        return path;
    }

    public List<int> PathBackward(int from, int to)
    {
        var path = new List<int>();
        int i = from;
        while (i != to)
        {
            i = (i - 1 + Count) % Count;
            path.Add(i);
            if (path.Count > Count) break;
        }
        return path;
    }
}
