using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Aiming data the Player passes to the weapon on every shot.
/// Computed by PlayerAim (aim assist included).
/// </summary>
public struct AimData
{
    /// <summary>World point under the reticle.</summary>
    public Vector3 point;
    /// <summary>Enemy marked by the reticle (null if none).</summary>
    public Health target;
    /// <summary>Target velocity in world space (to predict its movement).</summary>
    public Vector3 targetVelocity;

    public static AimData AtPoint(Vector3 p) => new AimData { point = p };
}

/// <summary>
/// Base class for player weapons.
///
/// SHOOTING FEEL:
///   • Fast bullets + prediction: with a marked enemy, the weapon aims where it will be
///     when the bullet arrives (not where it is now).
///   • Optional homing: the bullet slightly corrects its course towards the marked target.
///   • Dynamic spread (bloom): opens up with sustained fire and recovers when released.
///     The reticle (ReticleUI) shows that spread in real time.
///
/// TO CREATE A NEW WEAPON: inherit and override Fire(). See SpreadWeapon.
///
///   public override bool Fire(in AimData aim)
///   {
///       if (!CanFire) return false;
///       RegisterShot();
///       Vector3 dir = DirectionTo(aim);
///       SpawnBullet(dir, aim);                    // applies spread and homing
///       return true;
///   }
///
/// WEAPON PREFAB:
///   • Weapon component (or subclass) on the root, with its own "reticle" settings
///   • Empty child at the muzzle assigned to "firePoint"
///   • Registered in Addressables with the key used by GameState
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Bullet")]
    [Tooltip("Bullet prefab. If empty, it is loaded from bulletAddressableKey.")]
    public GameObject bulletPrefab;

    [Tooltip("Addressable key of the bullet. Only used if bulletPrefab is empty.")]
    public string bulletAddressableKey = "";

    [Header("Firing")]
    [Tooltip("Units/s. High values (100+) make shots feel instant.")]
    public float bulletSpeed = 110f;

    public float damage = 10f;

    [Tooltip("Minimum seconds between shots.")]
    [Min(0f)] public float fireInterval = 0.12f;

    [Tooltip("Adds the RailRig's velocity to the bullet so it doesn't fall behind.")]
    public bool inheritRailVelocity = true;

    [Tooltip("Where bullets spawn. If empty, the weapon's position is used.")]
    public Transform firePoint;

    [Header("Aim assist")]
    [Tooltip("Aims where the marked enemy will be when the bullet arrives.")]
    public bool predictMovement = true;
    [Tooltip("Degrees per second the bullet turns towards the marked enemy (0 = no homing).")]
    [Min(0f)] public float homing = 25f;
    [Tooltip("Multiplier of the reticle's 'magnet' radius for this weapon.")]
    [Min(0f)] public float aimAssist = 1f;

    [Header("Spread (half-angle in degrees)")]
    [Tooltip("Base spread.")]
    [Min(0f)] public float spread = 0f;
    [Tooltip("Spread added per shot (bloom).")]
    [Min(0f)] public float bloomPerShot = 0.25f;
    [Min(0f)] public float maxBloom = 1.5f;
    [Tooltip("Degrees per second the bloom recovers.")]
    [Min(0f)] public float bloomRecovery = 5f;

    [Header("Reticle")]
    public ReticleConfig reticle = ReticleConfig.Rifle();

    [Header("Visual")]
    [Tooltip("Rotates the weapon towards the aim point every frame.")]
    public bool rotateTowardsTarget = true;
    [Min(0f)] public float rotationSmoothing = 20f;

    protected float _lastShot = Mathf.NegativeInfinity;
    protected float _bloom;
    AsyncOperationHandle<GameObject> _bulletHandle;

    /// <summary>Team of the wielder. The Player sets it to Team.Player.</summary>
    public Team Team { get; set; } = Team.Player;

    public Vector3 Origin  => firePoint != null ? firePoint.position : transform.position;
    public bool    CanFire => bulletPrefab != null && Time.time >= _lastShot + fireInterval;

    /// <summary>Current half-angle of the firing cone in degrees (used by the reticle).</summary>
    public virtual float CurrentCone => spread + _bloom;

    Vector3 InheritedVelocity =>
        inheritRailVelocity && RailRig.Current != null ? RailRig.Current.Velocity : Vector3.zero;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    /// <summary>If you override Start() in a subclass, call base.Start().</summary>
    protected virtual void Start()
    {
        if (bulletPrefab != null || string.IsNullOrEmpty(bulletAddressableKey)) return;

        _bulletHandle = Addressables.LoadAssetAsync<GameObject>(bulletAddressableKey);
        _bulletHandle.Completed += h =>
        {
            if (h.Status == AsyncOperationStatus.Succeeded) bulletPrefab = h.Result;
            else Debug.LogError($"[Weapon] Could not load bullet '{bulletAddressableKey}'.");
        };
    }

    protected virtual void Update()
    {
        // Bloom starts recovering shortly after the last shot
        if (Time.time > _lastShot + fireInterval * 1.2f)
            _bloom = Mathf.MoveTowards(_bloom, 0f, bloomRecovery * Time.deltaTime);
    }

    protected virtual void OnDestroy()
    {
        if (_bulletHandle.IsValid()) Addressables.Release(_bulletHandle);
    }

    // ─── API ─────────────────────────────────────────────────────────────────
    /// <summary>Points the weapon at the aim point (called by the Player every frame).</summary>
    public virtual void AimAt(Vector3 target)
    {
        if (!rotateTowardsTarget) return;
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 1e-6f) return;

        Quaternion rot = Quaternion.LookRotation(dir);
        transform.rotation = rotationSmoothing > 0f
            ? Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime))
            : rot;
    }

    /// <summary>Tries to fire. Returns true if it fired.</summary>
    public virtual bool Fire(in AimData aim)
    {
        if (!CanFire) return false;
        RegisterShot();
        SpawnBullet(DirectionTo(aim), aim);
        return true;
    }

    /// <summary>Shortcut to fire at a point without a marked target.</summary>
    public bool Fire(Vector3 point) => Fire(AimData.AtPoint(point));

    // ─── Helpers for subclasses ──────────────────────────────────────────────
    /// <summary>Records the shot (fire rate) and increases bloom.</summary>
    protected void RegisterShot()
    {
        _lastShot = Time.time;
        _bloom = Mathf.Min(maxBloom, _bloom + bloomPerShot);
    }

    /// <summary>Direction from the muzzle to the aimed point, with prediction when applicable.</summary>
    protected Vector3 DirectionTo(in AimData aim)
    {
        Vector3 target = aim.point;
        if (predictMovement && aim.target != null)
            target = InterceptPoint(Origin, aim.point, aim.targetVelocity - InheritedVelocity, bulletSpeed);

        Vector3 dir = target - Origin;
        return dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
    }

    /// <summary>
    /// Spawns a bullet in the given direction. Applies the current spread (unless another
    /// value is given) and homing towards the marked target.
    /// </summary>
    protected Bullet SpawnBullet(Vector3 direction, in AimData aim, float spreadDegrees = -1f)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning($"[{name}] bulletPrefab is not assigned.");
            return null;
        }

        float cone = spreadDegrees >= 0f ? spreadDegrees : CurrentCone;
        Vector3 dir = ApplySpread(direction.normalized, cone);
        Bullet b = Bullet.Spawn(bulletPrefab, Origin, dir, bulletSpeed, damage, Team, InheritedVelocity);
        if (b != null && homing > 0f && aim.target != null) b.Home(aim.target, homing);
        return b;
    }

    /// <summary>Rotates a direction to a random point inside a cone of half-angle 'degrees'.</summary>
    protected static Vector3 ApplySpread(Vector3 dir, float degrees)
    {
        if (degrees <= 0f || dir == Vector3.zero) return dir;
        Vector2 r = Random.insideUnitCircle * degrees;
        return Quaternion.LookRotation(dir) * Quaternion.Euler(r.y, r.x, 0f) * Vector3.forward;
    }

    /// <summary>
    /// Point where a bullet travelling at 'speed' meets a target moving at 'relativeVelocity'
    /// (in the rig's frame). Returns the target's current position if there is no solution.
    /// </summary>
    public static Vector3 InterceptPoint(Vector3 origin, Vector3 target, Vector3 relativeVelocity, float speed)
    {
        Vector3 d = target - origin;
        float a = Vector3.Dot(relativeVelocity, relativeVelocity) - speed * speed;
        float b = 2f * Vector3.Dot(d, relativeVelocity);
        float c = Vector3.Dot(d, d);

        float t;
        if (Mathf.Abs(a) < 1e-4f)
            t = Mathf.Abs(b) > 1e-4f ? -c / b : 0f;
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return target;
            float s = Mathf.Sqrt(disc);
            float t1 = (-b - s) / (2f * a), t2 = (-b + s) / (2f * a);
            t = Mathf.Min(t1, t2);
            if (t < 0f) t = Mathf.Max(t1, t2);
        }
        if (t <= 0f || t > 3f) return target;
        return target + relativeVelocity * t;
    }
}
