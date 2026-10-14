using Game.Core.Events;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class EventPacerTests
{
    private static GameEvent Roll() => new DiceRolled(0, 3, 4, 0);

    [Fact]
    public void First_event_is_released_immediately_then_the_next_after_its_delay()
    {
        var p = new EventPacer();
        p.Enqueue(new[] { Roll(), Roll() });
        Assert.Single(p.Advance(0));
        Assert.Empty(p.Advance(0.1));
        Assert.Single(p.Advance(EventPacer.DelayFor(Roll(), 1)));
        Assert.Equal(0, p.Pending);
    }

    [Fact]
    public void Large_backlog_catches_up_faster()
    {
        Assert.True(EventPacer.DelayFor(Roll(), 60) < EventPacer.DelayFor(Roll(), 1));
    }

    [Fact]
    public void Clear_drops_queued_events()
    {
        var p = new EventPacer();
        p.Enqueue(new[] { Roll(), Roll(), Roll() });
        p.Advance(0);
        p.Clear();
        Assert.Equal(0, p.Pending);
        Assert.Empty(p.Advance(10));
    }
}
