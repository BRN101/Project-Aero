using System;
using UnityEngine;

// ─── Movement presets ───────────────────────────────────────────────────────
public enum MovementType
{
    Static,                 // No movement; stays at its position
    HorizontalOscillation,  // Left/right sway
    VerticalOscillation,    // Up/down sway
    Diagonal,               // Sway along 'diagonalDirection'
    Circular,               // Orbit around its position
    Figure8,                // Lissajous curve (∞)
    Wave,                   // Asymmetric jerk and glide (octopus-like)
    Path                    // Custom path (points relative to its position)
}

public enum PathMode
{
    Loop,       // Returns to the first point and repeats
    PingPong,   // Goes forward and backward along the path
    ExitAtEnd   // Leaves the scene when it reaches the last point
}

/// <summary>
/// Movement pattern definition. Used by Enemy, by wave entries (to override the
/// prefab's pattern) and by boss phases.
/// All values are in the enemy's anchor space (rig or world).
/// </summary>
[Serializable]
public class MovementPattern
{
    public MovementType type = MovementType.HorizontalOscillation;

    [Tooltip("Cycles per second for oscillating patterns.")]
    public float frequency = 0.4f;
    [Tooltip("Amplitude in units.")]
    public float amplitude = 3f;
    [Tooltip("[Circular] Orbit radius.")]
    public float radius = 3f;
    [Tooltip("[Diagonal] Sway direction.")]
    public Vector2 diagonalDirection = new Vector2(1f, 0.5f);

    [Tooltip("Seconds to blend smoothly from the current position into the pattern.")]
    [Min(0f)] public float blendDuration = 1.2f;

    [Header("Custom path (type = Path)")]
    [Tooltip("Points RELATIVE to the enemy's position.")]
    public Vector3[] points = new Vector3[0];
    public bool smoothPath = true;
    [Min(0.01f)] public float pathSpeed = 5f;
    public PathMode pathMode = PathMode.Loop;

    public bool IsPath => type == MovementType.Path && points != null && points.Length > 1;

    /// <summary>Offset of the oscillating pattern at time t (not used for Path).</summary>
    public Vector3 Offset(float t)
    {
        float ang = t * frequency * Mathf.PI * 2f;
        switch (type)
        {
            case MovementType.HorizontalOscillation:
                return new Vector3(Mathf.Sin(ang) * amplitude, 0f, 0f);
            case MovementType.VerticalOscillation:
                return new Vector3(0f, Mathf.Sin(ang) * amplitude, 0f);
            case MovementType.Diagonal:
            {
                Vector2 d = diagonalDirection.sqrMagnitude > 0f ? diagonalDirection.normalized : Vector2.right;
                float s = Mathf.Sin(ang) * amplitude;
                return new Vector3(d.x * s, d.y * s, 0f);
            }
            case MovementType.Circular:
                return new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            case MovementType.Figure8:
                return new Vector3(Mathf.Sin(ang) * amplitude, Mathf.Sin(ang * 2f) * amplitude * 0.5f, 0f);
            case MovementType.Wave:
            {
                // Asymmetric phase shift: a sharp "jerk" and a smooth glide back
                float y = Mathf.Sin(ang - 1.2f * Mathf.Cos(ang)) * amplitude;
                float x = Mathf.Sin(ang * 0.5f) * amplitude * 0.2f;
                return new Vector3(x, y, 0f);
            }
            default:
                return Vector3.zero;
        }
    }

    public CurvePath CreateCurve() =>
        IsPath ? new CurvePath(points, pathMode == PathMode.Loop, smoothPath) : null;

    public MovementPattern Clone()
    {
        var c = (MovementPattern)MemberwiseClone();
        c.points = points != null ? (Vector3[])points.Clone() : new Vector3[0];
        return c;
    }
}

/// <summary>
/// Enemy firing pattern definition. Used by Enemy and by boss phases.
/// </summary>
[Serializable]
public class FirePattern
{
    public bool enabled = true;
    [Tooltip("Bullet prefab. In a boss phase, empty = keep the current one.")]
    public GameObject bulletPrefab;
    [Tooltip("Units/s. Relative to the rig, so it doesn't depend on the rail speed.")]
    public float bulletSpeed = 28f;
    public float damage = 10f;

    [Tooltip("Seconds between shots (or between bursts).")]
    [Min(0.05f)] public float interval = 2f;
    [Tooltip("Delay before the first shot once the pattern starts.")]
    [Min(0f)] public float initialDelay = 0.6f;

    [Header("Shape")]
    [Min(1)] public int bulletsPerShot = 1;
    [Tooltip("Fan aperture in degrees (when there are several bullets). 360 = full ring.")]
    [Range(0f, 360f)] public float spreadAngle = 0f;
    [Min(1)] public int burstCount = 1;
    [Min(0f)] public float burstInterval = 0.12f;

    [Header("Targeting")]
    [Tooltip("Aim at the player. Otherwise fires along the enemy's forward.")]
    public bool aimAtPlayer = true;
    [Tooltip("Only fires if the player is within this distance.")]
    public float range = 80f;

    public FirePattern Clone() => (FirePattern)MemberwiseClone();
}
