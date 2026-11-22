using System;
using UnityEngine;

public enum ReticleStyle
{
    Cross,      // Center dot + 4 lines that open with spread (rifle)
    Circle      // Center dot + ring of arcs whose radius is the firing cone (shotgun)
}

/// <summary>
/// Look of a weapon's reticle. Every Weapon has its own; ReticleUI draws it.
/// Sizes are in reference pixels (1080p, per the CanvasScaler).
///
/// The line gap / circle radius is computed from the weapon's actual spread
/// (Weapon.CurrentCone), so the reticle shows where bullets can land.
/// </summary>
[Serializable]
public class ReticleConfig
{
    public ReticleStyle style = ReticleStyle.Cross;

    [Header("Shape")]
    public bool centerDot = true;
    [Min(0f)] public float dotSize = 4f;
    [Tooltip("[Cross] Length of each line.")]
    [Min(0f)] public float lineLength = 11f;
    [Min(0.5f)] public float thickness = 2.5f;
    [Tooltip("Minimum gap from the center (Cross) or minimum radius (Circle).")]
    [Min(0f)] public float minGap = 7f;

    [Header("Circle")]
    [Range(1, 8)] public int arcCount = 4;
    [Tooltip("Fraction of each arc that is drawn (the rest is a gap).")]
    [Range(0.05f, 1f)] public float arcFill = 0.6f;
    [Tooltip("Arc rotation in degrees (0 = arcs centered at top/bottom/sides).")]
    public float arcRotation = 0f;

    [Header("Color")]
    public Color color        = Color.white;
    public Color targetColor  = new Color(1f, 0.32f, 0.28f);
    public bool  outline      = true;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.55f);
    [Min(0f)] public float outlineThickness = 1f;

    [Header("Response")]
    [Tooltip("How fast the reticle opens/closes.")]
    [Min(0f)] public float smoothing = 25f;

    public static ReticleConfig Rifle() => new ReticleConfig();

    public static ReticleConfig Shotgun() => new ReticleConfig
    {
        style = ReticleStyle.Circle,
        dotSize = 4f,
        thickness = 2.5f,
        minGap = 20f,
        arcCount = 4,
        arcFill = 0.6f
    };
}
