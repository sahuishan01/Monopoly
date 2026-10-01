using Godot;

namespace BoardEmpire.View3D;

public enum CameraShot
{
    Overview,
    TokenFollow,
    PropertyInspect,
    Dice,
    Auction,
    Trade,
    Event,
    Victory,
}

/// <summary>
/// Orbit camera with named shots. Every shot is just a goal (look-at point, yaw, pitch,
/// distance); the rig eases towards it, so shots can be interrupted at any time.
/// </summary>
public partial class CameraRig : Node3D
{
    private readonly Camera3D _camera;
    private Vector3 _target, _goalTarget;
    private float _yaw, _pitch = 0.95f, _distance = 30;
    private float _goalYaw, _goalPitch = 0.95f, _goalDistance = 30;
    private float _orbitSpeed;
    private float _shake;
    private float _userYaw, _userZoom = 1f;
    private readonly Random _random = new();

    public float BoardSize { get; set; } = 24;
    public bool ReduceMotion { get; set; }
    public bool AllowShake { get; set; } = true;
    public Camera3D Camera => _camera;
    public CameraShot Shot { get; private set; }
    /// <summary>Aspect of the viewport; narrow screens need the camera further away.</summary>
    public float Aspect { get; set; } = 1.6f;

    public CameraRig()
    {
        // Built eagerly: the view configures the rig before it enters the scene tree.
        _camera = new Camera3D { Fov = 42, Near = 0.3f, Far = 220 };
        AddChild(_camera);
    }

    public override void _Ready() => Cut(CameraShot.Overview, Vector3.Zero);

    private float FitDistance() => BoardSize * (Aspect >= 1.25f ? 1.42f : 1.42f * 1.25f / Mathf.Max(0.45f, Aspect));

    public void Go(CameraShot shot, Vector3 focus, float yaw = 0)
    {
        if (ReduceMotion && shot != CameraShot.Overview) return;
        Shot = shot;
        _orbitSpeed = 0;
        switch (shot)
        {
            case CameraShot.Overview:
                _goalTarget = new Vector3(0, 0, BoardSize * 0.03f);
                _goalYaw = 0;
                _goalPitch = 0.98f;
                _goalDistance = FitDistance();
                break;
            case CameraShot.TokenFollow:
                _goalTarget = focus.Lerp(Vector3.Zero, 0.35f);
                _goalYaw = yaw * 0.35f;
                _goalPitch = 0.86f;
                _goalDistance = FitDistance() * 0.72f;
                break;
            case CameraShot.PropertyInspect:
                _goalTarget = focus.Lerp(Vector3.Zero, 0.12f);
                _goalYaw = yaw * 0.6f;
                _goalPitch = 0.72f;
                _goalDistance = BoardSize * 0.5f;
                break;
            case CameraShot.Dice:
                _goalTarget = focus;
                _goalYaw = 0;
                _goalPitch = 1.02f;
                _goalDistance = FitDistance() * 0.62f;
                break;
            case CameraShot.Auction:
                _goalTarget = focus.Lerp(Vector3.Zero, 0.2f);
                _goalYaw = yaw * 0.5f;
                _goalPitch = 0.66f;
                _goalDistance = BoardSize * 0.58f;
                _orbitSpeed = 0.12f;
                break;
            case CameraShot.Trade:
                _goalTarget = Vector3.Zero;
                _goalYaw = 0;
                _goalPitch = 0.9f;
                _goalDistance = FitDistance() * 0.85f;
                break;
            case CameraShot.Event:
                _goalTarget = Vector3.Zero;
                _goalYaw = 0;
                _goalPitch = 0.74f;
                _goalDistance = FitDistance() * 0.92f;
                _orbitSpeed = 0.08f;
                break;
            case CameraShot.Victory:
                _goalTarget = focus;
                _goalPitch = 0.5f;
                _goalDistance = BoardSize * 0.42f;
                _orbitSpeed = 0.45f;
                break;
        }
        if (ReduceMotion) Snap();
    }

    public void Cut(CameraShot shot, Vector3 focus)
    {
        bool reduce = ReduceMotion;
        ReduceMotion = false;
        Go(shot, focus);
        ReduceMotion = reduce;
        Snap();
    }

    private void Snap()
    {
        _target = _goalTarget;
        _yaw = _goalYaw;
        _pitch = _goalPitch;
        _distance = _goalDistance;
        Place();
    }

    public void Shake(float strength)
    {
        if (AllowShake && !ReduceMotion) _shake = Mathf.Max(_shake, strength);
    }

    /// <summary>Player input: drag to orbit, pinch or wheel to zoom.</summary>
    public void Orbit(float deltaYaw) => _userYaw = Mathf.Wrap(_userYaw + deltaYaw, -Mathf.Pi, Mathf.Pi);

    public void Zoom(float factor) => _userZoom = Mathf.Clamp(_userZoom * factor, 0.45f, 1.3f);

    public void ResetUser()
    {
        _userYaw = 0;
        _userZoom = 1;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float k = 1f - Mathf.Exp(-dt * 3.2f);
        _goalYaw += _orbitSpeed * dt;
        _target = _target.Lerp(_goalTarget, k);
        _yaw = Mathf.LerpAngle(_yaw, _goalYaw, k);
        _pitch = Mathf.Lerp(_pitch, _goalPitch, k);
        _distance = Mathf.Lerp(_distance, _goalDistance, k);
        if (_shake > 0.001f) _shake = Mathf.Max(0, _shake - dt * 1.8f);
        Place();
    }

    private void Place()
    {
        float yaw = _yaw + _userYaw;
        float distance = _distance * _userZoom;
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(yaw) * Mathf.Cos(_pitch)) * distance;
        var jitter = _shake > 0
            ? new Vector3((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * _shake
            : Vector3.Zero;
        // look_at needs global transforms, which only exist inside the tree.
        _camera.Transform = new Transform3D(Basis.Identity, _target + offset + jitter).LookingAt(_target + jitter * 0.5f, Vector3.Up);
    }
}
