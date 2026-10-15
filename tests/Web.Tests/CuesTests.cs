using Game.Core.Events;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class CuesTests
{
    [Fact]
    public void Dice_and_move_map_to_their_kinds()
    {
        Assert.Equal(CueKind.Dice, Cues.For(new DiceRolled(0, 3, 4, 0)).Kind);
        Assert.Equal(CueKind.Move, Cues.For(new PlayerMoved(0, 0, 7, MoveKind.Walk, false)).Kind);
    }

    [Fact]
    public void Unknown_events_have_no_cue_and_no_sound()
    {
        var c = Cues.For(new TurnEnded(0));
        Assert.Equal(CueKind.None, c.Kind);
        Assert.Equal("", c.Sound);
    }

    [Fact]
    public void Collapse_caps_each_kind_in_a_burst()
    {
        var burst = Enumerable.Range(0, 60).Select(_ => (GameEvent)new DiceRolled(0, 1, 2, 0));
        Assert.Equal(3, Cues.Collapse(burst, 3).Count);
    }
}
