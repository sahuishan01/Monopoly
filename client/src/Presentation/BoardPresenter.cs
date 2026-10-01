using BoardEmpire.Core;
using Game.Core.Board;
using Game.Core.Events;
using Game.Core.State;
using Godot;

namespace BoardEmpire.Presentation;

/// <summary>
/// Turns the authoritative event stream into a paced show. It owns the display state (a replica
/// that advances one event at a time, in step with the animations) and feeds whichever view is
/// active. Animations can be sped up or skipped freely: nothing here can change the match.
/// </summary>
public partial class BoardPresenter : Node
{
    private readonly Queue<GameEvent> _queue = new();
    private Settings _settings = null!;
    private bool _busy;

    public GameState? Display { get; private set; }
    public IGameView? View { get; set; }
    public bool IsIdle => !_busy && _queue.Count == 0;
    public int Backlog => _queue.Count;
    /// <summary>Replays override the configured speed.</summary>
    public float SpeedOverride { get; set; }
    public bool Paused { get; set; }

    /// <summary>The display state has just been advanced past this event.</summary>
    public event Action<GameEvent, GameState>? EventStarting;
    public event Action<GameEvent, GameState>? EventFinished;
    public event Action? Idle;

    public void Setup(Settings settings) => _settings = settings;

    public void Load(GameState snapshot, BoardDefinition board)
    {
        _queue.Clear();
        Display = snapshot.Clone();
        Display.Board = board;
        View?.Sync(Display);
        Idle?.Invoke();
    }

    public void Enqueue(IEnumerable<GameEvent> events)
    {
        foreach (var e in events) _queue.Enqueue(e);
    }

    public void Clear() => _queue.Clear();

    private AnimContext Context()
    {
        float speed = _settings.AnimationSpeed switch
        {
            AnimationSpeed.Fast => 2.2f,
            AnimationSpeed.Minimal => 6f,
            _ => 1f,
        };
        if (_settings.ReduceMotion) speed = Mathf.Max(speed, 1.6f);
        if (_queue.Count > 10) speed *= 1.6f;
        if (SpeedOverride > 0) speed = SpeedOverride;
        return new AnimContext(speed, _settings.ReduceMotion, _settings.CameraShake && !_settings.ReduceMotion);
    }

    public override void _Process(double delta)
    {
        if (_busy || Paused || Display == null || _queue.Count == 0) return;

        // A long backlog (reconnect, returning from the background) is applied without the show.
        if (_queue.Count > 80 && SpeedOverride <= 0)
        {
            while (_queue.Count > 12) Step(_queue.Dequeue(), animate: false);
            View?.Sync(Display);
        }
        _ = RunNext();
    }

    private void Step(GameEvent e, bool animate)
    {
        e.Apply(Display!);
        Display!.Version = e.Version;
        EventStarting?.Invoke(e, Display);
        if (!animate) EventFinished?.Invoke(e, Display);
    }

    private async Task RunNext()
    {
        _busy = true;
        var e = _queue.Dequeue();
        Step(e, animate: true);
        try
        {
            if (View != null)
            {
                var context = Context();
                bool minimal = _settings.AnimationSpeed == AnimationSpeed.Minimal && SpeedOverride <= 0;
                if (minimal && e is not GameEnded)
                {
                    View.Sync(Display!);
                }
                else
                {
                    // A view that is swapped out mid-animation never completes; do not wait for it.
                    var play = View.Play(e, Display!, context);
                    await Task.WhenAny(play, Delay(8));
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"Animation for {e.GetType().Name} failed: {ex.Message}");
            View?.Sync(Display!);
        }
        EventFinished?.Invoke(e, Display!);
        _busy = false;
        if (_queue.Count == 0) Idle?.Invoke();
    }

    private async Task Delay(double seconds)
    {
        if (!IsInsideTree()) return;
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}

/// <summary>Await-friendly helpers shared by both views.</summary>
public static class Anim
{
    public static async Task Wait(Godot.Node node, float seconds)
    {
        if (seconds <= 0.001f || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
        await node.ToSignal(node.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    public static async Task Finished(Godot.Node node, Tween tween)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !tween.IsValid()) return;
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }
}
