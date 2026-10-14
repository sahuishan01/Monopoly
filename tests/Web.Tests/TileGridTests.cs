using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class TileGridTests
{
    [Fact]
    public void Corners_follow_the_board_loop_for_side_10()
    {
        Assert.Equal((10, 10), TileGrid.Cell(0, 10));
        Assert.Equal((10, 0), TileGrid.Cell(10, 10));
        Assert.Equal((0, 0), TileGrid.Cell(20, 10));
        Assert.Equal((0, 10), TileGrid.Cell(30, 10));
    }

    [Fact]
    public void Every_tile_gets_a_distinct_perimeter_cell()
    {
        var cells = Enumerable.Range(0, 40).Select(i => TileGrid.Cell(i, 10)).ToHashSet();
        Assert.Equal(40, cells.Count);
        Assert.All(cells, c => Assert.True(c.Row is 0 or 10 || c.Col is 0 or 10));
    }
}
