using UnityEngine;

/// <summary>
/// Shotgun: fires several pellets inside a cone.
///   • Circle: one center pellet and the rest spread on rings (shotgun pattern).
///   • Fan: pellets on a horizontal line.
///
/// The cone (totalAngle) is exactly the reticle radius in Circle style,
/// so what you see in the reticle is where the pellets land.
/// Use it as a template for new weapons.
/// </summary>
public class SpreadWeapon : Weapon
{
    public enum Pattern { Fan, Circle }

    [Header("Pellets")]
    public Pattern pattern = Pattern.Circle;
    [Min(1)] public int pelletCount = 8;
    [Tooltip("Total cone aperture in degrees.")]
    [Range(0f, 90f)] public float totalAngle = 9f;
    [Tooltip("Random deviation of each pellet (degrees).")]
    [Min(0f)] public float pelletJitter = 0.6f;

    public override float CurrentCone => totalAngle * 0.5f + spread + _bloom;

    public override bool Fire(in AimData aim)
    {
        if (!CanFire) return false;
        RegisterShot();

        Vector3 center = DirectionTo(aim);
        Quaternion baseRot = Quaternion.LookRotation(center);
        float half = CurrentCone;

        // Randomly rotate the pattern each shot so it is never identical
        float roll = Random.Range(0f, 360f);

        for (int i = 0; i < pelletCount; i++)
        {
            Quaternion offset;
            if (pattern == Pattern.Circle)
            {
                if (i == 0) offset = Quaternion.identity;   // center pellet
                else
                {
                    float ang = roll + 360f * (i - 1) / Mathf.Max(1, pelletCount - 1);
                    float r = half * (i % 2 == 0 ? 1f : 0.6f);   // two alternating rings to cover the cone
                    offset = Quaternion.AngleAxis(ang, Vector3.forward) * Quaternion.Euler(0f, r, 0f);
                }
            }
            else
            {
                float t = pelletCount > 1 ? (float)i / (pelletCount - 1) : 0.5f;
                offset = Quaternion.Euler(0f, Mathf.Lerp(-half, half, t), 0f);
            }

            SpawnBullet(baseRot * offset * Vector3.forward, aim, pelletJitter);
        }
        return true;
    }
}
