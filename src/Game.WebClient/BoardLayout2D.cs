using Game.Core.Events;
using Game.Core.State;

namespace Game.WebClient;

/// <summary>
/// Geometry of the 2D board. The perimeter grid uses wider corner tracks (<see cref="CornerSpan"/> units)
/// and one unit per edge tile; positions are returned as percentages of the board edge.
/// </summary>
public static class BoardLayout2D
{
    public const double CornerSpan = 1.6;
    public const int MaxWalk = 12;

    /// <summary>Total units along one board edge.</summary>
    public static double Span(int side) => 2 * CornerSpan + Math.Max(0, side - 1);

    /// <summary>CSS grid track list for one axis, e.g. "1.6fr repeat(9, 1fr) 1.6fr".</summary>
    public static string Tracks(int side) => FormattableString.Invariant($"{CornerSpan}fr repeat({Math.Max(1, side - 1)}, 1fr) {CornerSpan}fr");

    private static double Axis(int i, int side) =>
        i <= 0 ? CornerSpan / 2 : i >= side ? Span(side) - CornerSpan / 2 : CornerSpan + i - 0.5;

    /// <summary>Centre of a tile as (x%, y%) of the board.</summary>
    public static (double X, double Y) Centre(int tile, int side)
    {
        var (row, col) = TileGrid.Cell(tile, side);
        double s = Span(side);
        return (Axis(col, side) / s * 100, Axis(row, side) / s * 100);
    }

    /// <summary>Unit vector from a tile toward the board centre: edges point straight in, corners diagonally.</summary>
    public static (int X, int Y) Inward(int tile, int side)
    {
        var (row, col) = TileGrid.Cell(tile, side);
        int x = col == 0 ? 1 : col == side ? -1 : 0;
        int y = row == 0 ? 1 : row == side ? -1 : 0;
        bool corner = x != 0 && y != 0;
        if (!corner) { if (row == side || row == 0) x = 0; else y = 0; }
        return (x, y);
    }

    /// <summary>
    /// Where the k-th of m pawns on a tile stands (x%, y%): pawns gather in a compact grid at the tile's inner
    /// edge (toward the board centre, away from the price and name); corners use their inner corner.
    /// </summary>
    public static (double X, double Y) Spot(int tile, int k, int m, int side)
    {
        double u = 100 / Span(side);
        var c = Centre(tile, side);
        var (ix, iy) = Inward(tile, side);
        m = Math.Max(1, m);
        k = Math.Clamp(k, 0, m - 1);
        if (ix != 0 && iy != 0)
        {
            // corner: 2-column grid around a point 0.45u from the inner corner
            int cols = m == 1 ? 1 : 2, rows = (m + cols - 1) / cols;
            int r = k / cols, cc = k % cols;
            double gx = (cc - (cols - 1) / 2.0) * 0.34, gy = (r - (rows - 1) / 2.0) * 0.3;
            double off = CornerSpan / 2 - 0.45;
            return (c.X + (ix * off + gx) * u, c.Y + (iy * off + gy) * u);
        }
        int rowsN = (m + 2) / 3;
        int row = k / 3, col = k % 3, inRow = row == rowsN - 1 ? m - row * 3 : 3;
        double along = (col - (inRow - 1) / 2.0) * 0.3;
        double depth = CornerSpan / 2 - 0.3 + row * 0.26;  // first row sits on the band, later rows step inward
        // tangent is perpendicular to inward
        double tx = iy != 0 ? 1 : 0, ty = ix != 0 ? 1 : 0;
        return (c.X + (ix * depth + tx * along) * u, c.Y + (iy * depth + ty * along) * u);
    }

    /// <summary>Spots for every pawn given the tile each one is displayed on; pawns sharing a tile are ordered by id.</summary>
    public static Dictionary<int, (double X, double Y)> Spots(IEnumerable<(int Id, int Tile)> pawns, int side)
    {
        var result = new Dictionary<int, (double X, double Y)>();
        foreach (var g in pawns.GroupBy(p => p.Tile))
        {
            var ids = g.Select(p => p.Id).OrderBy(i => i).ToList();
            for (int k = 0; k < ids.Count; k++) result[ids[k]] = Spot(g.Key, k, ids.Count, side);
        }
        return result;
    }

    /// <summary>Displayed (x%, y%) of a player's pawn when every pawn is shown at its state position.</summary>
    public static (double X, double Y) PawnAt(GameState s, int player, int side)
    {
        if (player < 0 || player >= s.Players.Count) return (50, 50);
        var spots = Spots(s.Players.Where(q => !q.Bankrupt || q.Id == player).Select(q => (q.Id, q.Position)), side);
        return spots[player];
    }

    /// <summary>Tiles a pawn passes through (excluding <paramref name="from"/>, ending at <paramref name="to"/>).
    /// Jumps and walks longer than <see cref="MaxWalk"/> glide straight to the destination.</summary>
    public static int[] HopPath(int from, int to, int n, MoveKind kind)
    {
        if (n <= 0 || from == to) return Array.Empty<int>();
        if (kind == MoveKind.Jump) return new[] { to };
        int step = kind == MoveKind.Backward ? -1 : 1;
        int d = (((to - from) * step) % n + n) % n;
        if (d > MaxWalk) return new[] { to };
        var path = new int[d];
        for (int i = 1; i <= d; i++) path[i - 1] = ((from + i * step) % n + n) % n;
        return path;
    }

    /// <summary>Number of coins drawn for a payment (grows logarithmically, 3..24).</summary>
    public static int CoinCount(int amount) =>
        amount <= 0 ? 0 : Math.Clamp((int)Math.Round(Math.Log2(amount / 10.0 + 1) * 3), 3, 24);

    /// <summary>Owner hue in degrees, matching the 3D view's HSL(owner * 0.17).</summary>
    public static double Hue(int owner) => (owner * 0.17 % 1 + 1) % 1 * 360;
}
