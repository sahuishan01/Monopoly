namespace Game.WebClient;

/// <summary>Maps a tile index to a cell on the (side+1) x (side+1) perimeter grid. Tile 0 is bottom-right and the loop runs clockwise.</summary>
public static class TileGrid
{
    public static (int Row, int Col) Cell(int tile, int side)
    {
        int i = ((tile % (side * 4)) + side * 4) % (side * 4);
        if (i < side) return (side, side - i);
        if (i < side * 2) return (side - (i - side), 0);
        if (i < side * 3) return (0, i - side * 2);
        return (i - side * 3, side);
    }
}
