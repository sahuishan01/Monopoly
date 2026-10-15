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

    /// <summary>Offset (in % of the board) of the k-th of m pawns sharing a tile.</summary>
    public static (double X, double Y) Slot(int k, int m, int side)
    {
        if (m <= 1) return (0, 0);
        double r = (m <= 4 ? 0.2 : 0.27) / Span(side) * 100;
        double a = -Math.PI / 2 + 2 * Math.PI * k / m + (m == 2 ? Math.PI / 4 : 0);
        return (Math.Cos(a) * r, Math.Sin(a) * r);
    }

    /// <summary>Displayed (x%, y%) of a player's pawn, matching the slot ordering used when rendering.</summary>
    public static (double X, double Y) PawnAt(GameState s, int player, int side)
    {
        if (player < 0 || player >= s.Players.Count) return (50, 50);
        var p = s.Players[player];
        var mates = s.Players.Where(q => !q.Bankrupt && q.Position == p.Position).Select(q => q.Id).ToList();
        int k = Math.Max(0, mates.IndexOf(p.Id));
        var c = Centre(p.Position, side);
        var o = Slot(k, mates.Count, side);
        return (c.X + o.X, c.Y + o.Y);
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
