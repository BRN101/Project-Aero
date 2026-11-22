using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Team an entity belongs to. Bullets never damage their own team.</summary>
public enum Team { Player, Enemy }

/// <summary>
/// Reusable health component (player, enemies, bosses, destructible props).
///
/// Bullets look this component up with GetComponentInParent, so an object with
/// several child colliders (e.g. a multi-part boss) shares a single health pool.
///
/// Events:
///   • Changed → whenever health changes (handy for UI bars)
///   • Damaged → when damage is taken (amount)
///   • Died    → when health reaches 0
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] Team team = Team.Enemy;
    [SerializeField, Min(1f)] float maxHealth = 30f;

    [Tooltip("Seconds of invulnerability after taking damage (0 = none).")]
    [SerializeField, Min(0f)] float invulnerabilityAfterHit = 0f;

    [Header("Events (Inspector)")]
    public UnityEvent onDamaged;
    public UnityEvent onDeath;

    public event Action<Health>        Changed;
    public event Action<Health, float> Damaged;
    public event Action<Health>        Died;

    float _current;
    float _invulnerableUntil = -1f;
    bool  _initialized;
    Collider[] _colliders;

    static readonly List<Health> _active = new List<Health>();
    /// <summary>Every enabled Health in the scene (used by aim assist).</summary>
    public static IReadOnlyList<Health> Active => _active;

    // ─── Properties ──────────────────────────────────────────────────────────
    public Team  Team       { get => team; set => team = value; }
    public float Current    { get { Initialize(); return _current; } }
    public float Max        => maxHealth;
    public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
    public bool  IsAlive    => Current > 0f;

    /// <summary>Manual invulnerability (boss intro, phase change, etc.).</summary>
    public bool Invulnerable { get; set; }

    /// <summary>Alive and with at least one enabled collider (dormant or hidden enemies are not).</summary>
    public bool IsTargetable
    {
        get
        {
            if (!IsAlive || !isActiveAndEnabled) return false;
            foreach (var c in Colliders) if (c != null && c.enabled && !c.isTrigger) return true;
            return false;
        }
    }

    /// <summary>Center of the first enabled collider (or the object's position).</summary>
    public Vector3 Center
    {
        get
        {
            foreach (var c in Colliders)
                if (c != null && c.enabled && !c.isTrigger) return c.bounds.center;
            return transform.position;
        }
    }

    Collider[] Colliders => _colliders ??= GetComponentsInChildren<Collider>(includeInactive: true);

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Awake()     => Initialize();
    void OnEnable()  => _active.Add(this);
    void OnDisable() => _active.Remove(this);

    void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _current = maxHealth;
    }

    // ─── API ─────────────────────────────────────────────────────────────────
    /// <summary>Sets team and max health, refilling health.</summary>
    public void Configure(Team newTeam, float max)
    {
        team = newTeam;
        SetMax(max, refill: true);
    }

    public void SetMax(float max, bool refill = true)
    {
        Initialize();
        maxHealth = Mathf.Max(1f, max);
        _current = refill ? maxHealth : Mathf.Min(_current, maxHealth);
        Changed?.Invoke(this);
    }

    /// <summary>Applies damage. Returns true if the damage was applied.</summary>
    public bool TakeDamage(float amount)
    {
        Initialize();
        if (amount <= 0f || !IsAlive || Invulnerable || Time.time < _invulnerableUntil)
            return false;

        _current = Mathf.Max(0f, _current - amount);
        _invulnerableUntil = Time.time + invulnerabilityAfterHit;

        Damaged?.Invoke(this, amount);
        onDamaged?.Invoke();
        Changed?.Invoke(this);

        if (_current <= 0f)
        {
            Died?.Invoke(this);
            onDeath?.Invoke();
        }
        return true;
    }

    public void Heal(float amount)
    {
        Initialize();
        if (amount <= 0f || !IsAlive) return;
        _current = Mathf.Min(maxHealth, _current + amount);
        Changed?.Invoke(this);
    }

    public void Kill()
    {
        Invulnerable = false;
        _invulnerableUntil = -1f;
        TakeDamage(Current);
    }
}
