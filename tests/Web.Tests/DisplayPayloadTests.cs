using Game.Core.Board;
using Game.Core.Events;
using Game.WebClient;
using System.Text.Json;
using Xunit;

namespace Web.Tests;

public class DisplayPayloadTests
{
    [Fact]
    public void Event_json_carries_the_type_discriminator_and_fields()
    {
        string json = DisplayPayload.Event(new DiceRolled(1, 3, 4, 0));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("DiceRolled", doc.RootElement.GetProperty("$t").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("D1").GetInt32());
    }

    [Fact]
    public void Board_json_lists_tiles()
    {
        var board = BoardLibrary.Get(BoardLibrary.DefaultBoardId);
        using var doc = JsonDocument.Parse(DisplayPayload.Board(board));
        Assert.Equal(board.Tiles.Count, doc.RootElement.GetProperty("Tiles").GetArrayLength());
    }
}
