using Game.Core.Events;
using Game.Core.State;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class BoardLayout2DTests
{
    [Fact]
    public void Corners_sit_in_the_corner_tracks()
    {
        double half = BoardLayout2D.CornerSpan / 2 / BoardLayout2D.Span(10) * 100;
        Assert.Equal((100 - half, 100 - half), BoardLayout2D.Centre(0, 10));
        Assert.Equal((half, half), BoardLayout2D.Centre(20, 10));
        Assert.Equal("1.6fr repeat(9, 1fr) 1.6fr", BoardLayout2D.Tracks(10));
    }

    [Fact]
    public void Edge_tiles_are_evenly_spaced_between_corners()
    {
        var a = BoardLayout2D.Centre(1, 10);
        var b = BoardLayout2D.Centre(2, 10);
        Assert.Equal(100 / BoardLayout2D.Span(10), a.X - b.X, 6);
        Assert.Equal(a.Y, b.Y);
    }

    [Theory]
    [InlineData(38, 2, 40, MoveKind.Walk, new[] { 39, 0, 1, 2 })]
    [InlineData(2, 39, 40, MoveKind.Backward, new[] { 1, 0, 39 })]
    [InlineData(10, 30, 40, MoveKind.Walk, new[] { 30 })]
    [InlineData(5, 30, 40, MoveKind.Jump, new[] { 30 })]
    [InlineData(5, 5, 40, MoveKind.Walk, new int[0])]
    public void Hop_path_follows_the_loop(int from, int to, int n, MoveKind kind, int[] expected) =>
        Assert.Equal(expected, BoardLayout2D.HopPath(from, to, n, kind));

    [Fact]
    public void Pawns_sharing_a_tile_get_distinct_slots()
    {
        var s = new GameState();
        for (int i = 0; i < 4; i++) s.Players.Add(new PlayerState { Id = i, Position = 3 });
        var spots = Enumerable.Range(0, 4).Select(i => BoardLayout2D.PawnAt(s, i, 6)).ToHashSet();
        Assert.Equal(4, spots.Count);
        s.Players[1].Position = 4;
        s.Players[2].Bankrupt = true;
        s.Players[3].Position = 4;
        Assert.Equal(BoardLayout2D.Spot(3, 0, 1, 6), BoardLayout2D.PawnAt(s, 0, 6));
    }

    [Fact]
    public void Pawns_stand_on_the_inner_edge_toward_the_centre()
    {
        // tile 2 on a 40-tile board is on the bottom row: inward is up, so pawns sit above the tile centre
        var c = BoardLayout2D.Centre(2, 10);
        var spot = BoardLayout2D.Spot(2, 0, 1, 10);
        Assert.Equal(c.X, spot.X, 6);
        Assert.True(spot.Y < c.Y);
        // left column (tile 12): inward is right
        Assert.True(BoardLayout2D.Spot(12, 0, 1, 10).X > BoardLayout2D.Centre(12, 10).X);
        // start corner (bottom-right): inward is up-left
        var s0 = BoardLayout2D.Spot(0, 0, 1, 10); var c0 = BoardLayout2D.Centre(0, 10);
        Assert.True(s0.X < c0.X && s0.Y < c0.Y);
        // every spot of a crowded tile stays inside that tile's track span
        double u = 100 / BoardLayout2D.Span(10);
        for (int k = 0; k < 6; k++)
        {
            var p = BoardLayout2D.Spot(2, k, 6, 10);
            Assert.InRange(p.X, c.X - 0.5 * u, c.X + 0.5 * u);
            Assert.InRange(p.Y, c.Y - BoardLayout2D.CornerSpan / 2 * u - 0.3 * u, c.Y);
        }
    }

    [Fact]
    public void Coin_count_and_hue_are_bounded()
    {
        Assert.Equal(0, BoardLayout2D.CoinCount(0));
        Assert.Equal(3, BoardLayout2D.CoinCount(1));
        Assert.Equal(24, BoardLayout2D.CoinCount(1_000_000));
        Assert.Equal(0, BoardLayout2D.Hue(0));
        Assert.Equal(61.2, BoardLayout2D.Hue(1), 6);
    }
}
