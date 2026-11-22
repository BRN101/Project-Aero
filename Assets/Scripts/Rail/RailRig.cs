using System;
using UnityEngine;

/// <summary>
/// "Cart" that moves through the level automatically. The player and the camera are its
/// children: the player only moves on the rig's local XY plane; forward motion comes from the rig.
///
/// MODES:
///   • With a path (RailPath): really moves along the curve and turns to follow its tangent.
///   • Without a path: stays still, but distance advances virtually (speed × time),
///     so LevelDirector distance steps still work. Combine it with SkyboxScroller or
///     scrolling scenery to fake forward motion.
///
/// Runs before other scripts so rig-anchored enemies read its updated position
/// in the same frame and don't jitter.
/// </summary>
[DefaultExecutionOrder(-100)]
public class RailRig : MonoBehaviour
{
    public static RailRig Current { get; private set; }

    [Header("Path")]
    [Tooltip("Path to follow. If empty, the rig doesn't move (virtual advance mode).")]
    public RailPath path;

    [Tooltip("Starting distance along the path.")]
    [Min(0f)] public float startDistance = 0f;

    [Header("Speed")]
    [Min(0f)] public float speed = 12f;
    [Tooltip("Acceleration when changing speed, stopping or resuming (units/s²).")]
    [Min(0.01f)] public float acceleration = 8f;
    public bool autoStart = true;

    [Header("Orientation")]
    [Tooltip("Smoothing when turning to follow the curve (higher = faster).")]
    [Min(0f)] public float turnSmoothing = 4f;
    [Tooltip("Keeps the rig's 'up' aligned with the world (no banking).")]
    public bool keepHorizon = true;

    public event Action ReachedEnd;

    public float   Distance     { get; private set; }
    public float   CurrentSpeed { get; private set; }
    /// <summary>World-space velocity (bullets inherit it so they don't fall behind).</summary>
    public Vector3 Velocity     { get; private set; }
    public bool    IsMoving     => _running && _pauses == 0;
    public bool    AtEnd        => path != null && !path.closed && Distance >= path.Length - 0.001f;

    float _targetSpeed;
    int   _pauses;
    bool  _running;
    bool  _endNotified;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Awake()
    {
        Current = this;
        _targetSpeed = speed;
        _running = autoStart;
        Distance = startDistance;
        PlaceOnPath(instant: true);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float target = IsMoving && !AtEnd ? _targetSpeed : 0f;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, target, acceleration * dt);
        Distance += CurrentSpeed * dt;

        Vector3 before = transform.position;
        PlaceOnPath(instant: false);
        Velocity = (transform.position - before) / dt;

        if (AtEnd && !_endNotified)
        {
            _endNotified = true;
            ReachedEnd?.Invoke();
        }
    }

    void PlaceOnPath(bool instant)
    {
        if (path == null) return;

        Distance = path.Curve.NormalizeDistance(Distance);
        transform.position = path.PointAt(Distance);

        Vector3 tangent = path.TangentAt(Distance);
        Vector3 up = keepHorizon ? Vector3.up : transform.up;
        if (keepHorizon && Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.99f) up = transform.forward;

        Quaternion targetRot = Quaternion.LookRotation(tangent, up);
        transform.rotation = instant || turnSmoothing <= 0f
            ? targetRot
            : Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));
    }

    // ─── API ─────────────────────────────────────────────────────────────────
    public void StartMoving() => _running = true;

    /// <summary>Stops the rig. Supports several simultaneous pauses (e.g. two bosses).</summary>
    public void Pause() => _pauses++;

    public void Resume() => _pauses = Mathf.Max(0, _pauses - 1);

    public void SetSpeed(float newSpeed) => _targetSpeed = Mathf.Max(0f, newSpeed);

    /// <summary>Converts a point from rig space to world space.</summary>
    public Vector3 ToWorld(Vector3 localPoint) => transform.TransformPoint(localPoint);
}
