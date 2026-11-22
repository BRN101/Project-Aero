using System;
using UnityEngine;

/// <summary>
/// Generic projectile for the player or enemies.
///
/// Moves itself in Update and detects hits with a SphereCast between the previous and
/// the new position, so fast bullets never tunnel through targets.
/// Its collider is forced to trigger and its Rigidbody to kinematic: it never pushes anything.
///
/// DAMAGE RULES (via Health on the hit object or its parents):
///   • Same team      → ignored (passes through the shooter and its allies)
///   • Opposing team  → applies damage and is destroyed
///   • Solid collider without Health (scenery) → destroyed
///   • Triggers (other bullets, zones) → ignored
///
/// Create bullets with Bullet.Spawn(...) instead of calling Instantiate directly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Tooltip("Seconds before self-destructing.")]
    public float lifetime = 3f;

    [Tooltip("Hit detection radius. 0 = computed from the collider.")]
    [Min(0f)] public float radius = 0f;

    [Tooltip("Layers the bullet can hit.")]
    public LayerMask collisionMask = ~0;

    [Tooltip("Optional prefab spawned on impact (particles, sound...).")]
    public GameObject impactEffect;

    /// <summary>Raised when the bullet hits something with Health (used by the reticle hitmarker).</summary>
    public static event Action<Bullet, Health> Hit;

    static readonly RaycastHit[] _hits = new RaycastHit[16];

    Vector3 _velocity;
    Vector3 _inherited;
    Health  _homingTarget;
    float   _homingRate;
    float   _damage;
    Team    _team;
    bool    _initialized;
    bool    _hasHit;

    public Team    Team     => _team;
    public Vector3 Velocity => _velocity;
    public float   Damage   => _damage;

    // ─── Creation ────────────────────────────────────────────────────────────
    /// <summary>Instantiates and initializes a bullet.</summary>
    /// <param name="inheritedVelocity">Velocity of the reference frame (e.g. the RailRig) added to the bullet.</param>
    public static Bullet Spawn(GameObject prefab, Vector3 origin, Vector3 direction, float speed,
                               float damage, Team team, Vector3 inheritedVelocity = default)
    {
        if (prefab == null) return null;
        if (direction.sqrMagnitude < 1e-8f) direction = Vector3.forward;

        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction));
        Bullet bullet = go.GetComponent<Bullet>();
        if (bullet == null) bullet = go.AddComponent<Bullet>();
        bullet.Initialize(direction, speed, damage, team, inheritedVelocity);
        return bullet;
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Awake()
    {
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (radius <= 0f)
        {
            radius = 0.1f;
            if (col != null)
            {
                Vector3 e = col.bounds.extents;
                radius = Mathf.Max(0.02f, Mathf.Min(e.x, Mathf.Min(e.y, e.z)));
            }
        }
    }

    void Start()
    {
        // If someone instantiated it without Initialize(), at least it expires
        if (!_initialized) Destroy(gameObject, lifetime);
    }

    public void Initialize(Vector3 direction, float speed, float damage, Team team, Vector3 inheritedVelocity = default)
    {
        _initialized = true;
        _inherited   = inheritedVelocity;
        _velocity    = direction.normalized * speed + inheritedVelocity;
        _damage      = damage;
        _team        = team;

        if (_velocity.sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(_velocity);

        Destroy(gameObject, lifetime);
    }

    /// <summary>Makes the bullet steer towards a target at 'degreesPerSecond'.</summary>
    public void Home(Health target, float degreesPerSecond)
    {
        _homingTarget = target;
        _homingRate   = degreesPerSecond;
    }

    void Update()
    {
        if (_hasHit) return;
        UpdateHoming();

        Vector3 step = _velocity * Time.deltaTime;
        float   dist = step.magnitude;
        if (dist <= 0f) return;

        Vector3 origin = transform.position;
        int n = Physics.SphereCastNonAlloc(origin, radius, step / dist, _hits, dist, collisionMask, QueryTriggerInteraction.Ignore);

        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (!IsValidTarget(_hits[i].collider)) continue;
            if (best < 0 || _hits[i].distance < _hits[best].distance) best = i;
        }

        if (best >= 0)
        {
            Vector3 point = _hits[best].distance > 0f ? _hits[best].point : origin;
            Impact(_hits[best].collider, point);
            return;
        }

        transform.position = origin + step;
    }

    void UpdateHoming()
    {
        if (_homingTarget == null || _homingRate <= 0f) return;
        if (!_homingTarget.IsTargetable) { _homingTarget = null; return; }

        // Steer the bullet's own velocity (without the inherited rail velocity), which is what "aims"
        Vector3 own     = _velocity - _inherited;
        Vector3 towards = _homingTarget.Center - transform.position;
        if (own.sqrMagnitude < 1e-6f || towards.sqrMagnitude < 1e-6f) return;

        // Once the target is behind the bullet, stop homing
        if (Vector3.Dot(own, towards) <= 0f) { _homingTarget = null; return; }

        own = Vector3.RotateTowards(own, towards, _homingRate * Mathf.Deg2Rad * Time.deltaTime, 0f);
        _velocity = own + _inherited;
        transform.rotation = Quaternion.LookRotation(_velocity);
    }

    // Fallback: covers bullets that spawn already overlapping a target
    void OnTriggerEnter(Collider other)
    {
        if (!_hasHit && IsValidTarget(other)) Impact(other, transform.position);
    }

    // ─── Impact ──────────────────────────────────────────────────────────────
    bool IsValidTarget(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        if (c.transform.IsChildOf(transform)) return false;
        Health h = c.GetComponentInParent<Health>();
        return h == null || h.Team != _team;
    }

    void Impact(Collider c, Vector3 point)
    {
        _hasHit = true;
        Health h = c.GetComponentInParent<Health>();
        if (h != null)
        {
            h.TakeDamage(_damage);
            Hit?.Invoke(this, h);
        }

        if (impactEffect != null) Instantiate(impactEffect, point, Quaternion.identity);
        Destroy(gameObject);
    }
}
