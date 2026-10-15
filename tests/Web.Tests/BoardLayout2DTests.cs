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
        Assert.Equal(BoardLayout2D.Centre(3, 6), BoardLayout2D.PawnAt(s, 0, 6));
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
