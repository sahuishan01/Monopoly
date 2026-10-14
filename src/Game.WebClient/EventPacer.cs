using Game.Core.Events;

namespace Game.WebClient;

/// <summary>Releases queued events one at a time so views can animate them. The queue is only a presentation concern: the replica state is already ahead.</summary>
public sealed class EventPacer
{
    private readonly Queue<GameEvent> _queue = new();
    private double _wait;

    public int Pending => _queue.Count;

    public void Enqueue(IEnumerable<GameEvent> events)
    {
        foreach (var e in events) _queue.Enqueue(e);
    }

    public void Clear()
    {
        _queue.Clear();
        _wait = 0;
    }

    public IReadOnlyList<GameEvent> Advance(double dt)
    {
        var released = new List<GameEvent>();
        _wait -= dt;
        while (_queue.Count > 0 && _wait <= 0)
        {
            var e = _queue.Dequeue();
            released.Add(e);
            _wait = DelayFor(e, _queue.Count);
        }
        return released;
    }

    public static double DelayFor(GameEvent e, int backlog)
    {
        if (backlog > 40) return 0.02;
        double scale = backlog > 12 ? 0.35 : 1.0;
        double basis = e switch
        {
            DiceRolled => 0.9,
            PlayerMoved => 0.7,
            _ => 0.3,
        };
        return basis * scale;
    }
}
