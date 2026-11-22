using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Base enemy with a lifecycle: Dormant → Entering → Active → Exiting → Dead.
///
/// ANCHOR:
///   • RailRig: its position is expressed in RailRig space (e.g. (0, 2, 30)
///     = 30 units ahead of the player). Travels with the player. Ideal for air battles.
///   • World: fixed position in the level; the player flies past it (turrets, ambushes).
///
/// MOVEMENT: oscillating presets or a custom path (MovementPattern).
/// FIRING: FirePattern (aimed, fan, ring, bursts).
///
/// Normally spawned from a wave (WaveData + LevelDirector), which overrides its
/// position and optionally its movement. It can also be placed by hand in the scene;
/// in that case it activates when the player gets close.
///
/// For special behaviours, inherit and override Shoot(), OnActivated(),
/// OnPatternStarted() or OnKilled(). See Boss.
/// </summary>
[DisallowMultipleComponent]
public class Enemy : MonoBehaviour
{
    public enum Anchor { RailRig, World }
    protected enum State { Dormant, Entering, Active, Exiting, Dead }

    // ─── SETTINGS ─────────────────────────────────────────────────────────────
    [Header("Score and effects")]
    public int scoreValue = 100;
    [Tooltip("Optional prefab spawned on death (explosion, particles...).")]
    public GameObject deathEffect;

    [Header("Position")]
    public Anchor anchor = Anchor.RailRig;
    [Tooltip("Center of its movement, in rig or world space depending on the anchor.")]
    public Vector3 position = new Vector3(0f, 2f, 30f);
    [Tooltip("[World] Use the object's position in the scene instead of 'position'.")]
    public bool useObjectPosition = true;

    [Header("Activation (hand-placed enemies)")]
    [Tooltip("Activates when the player gets close. Waves disable this and activate it on spawn.")]
    public bool activateByDistance = true;
    public float activationDistance = 45f;

    [Header("Entry")]
    [Tooltip("Where it comes from, relative to its position (off-screen).")]
    public Vector3 entryOffset = new Vector3(0f, -15f, 0f);
    [Min(0f)] public float entryDuration = 1.5f;
    public AnimationCurve entryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Takes no damage while entering.")]
    public bool invulnerableOnEntry = false;

    [Header("Movement")]
    public MovementPattern movement = new MovementPattern();

    [Header("Firing")]
    public FirePattern firing = new FirePattern();
    [Tooltip("Where bullets spawn. Empty = enemy center.")]
    public Transform firePoint;

    [Header("Orientation")]
    public bool  facePlayer = true;
    public float turnSpeed  = 6f;

    [Header("Exit")]
    [Tooltip("Seconds in the Active state before leaving. 0 = never leaves (until killed).")]
    [Min(0f)] public float timeOnScreen = 8f;
    [Min(0.01f)] public float exitDuration = 1.5f;
    [Tooltip("Exit direction. (0,0,0) = keeps going in the direction it was moving.")]
    public Vector3 exitDirection = Vector3.zero;
    [Min(1f)] public float exitDistance = 25f;

    /// <summary>Raised when it disappears. The bool tells whether the player killed it.</summary>
    public event Action<Enemy, bool> Despawned;

    // ─── INTERNAL STATE ───────────────────────────────────────────────────────
    protected State _state = State.Dormant;
    protected Health Health { get; private set; }

    Vector3   _localPos;            // current position in anchor space
    Vector3   _center;              // pattern center in anchor space
    Vector3   _blendFrom;           // to blend smoothly when the pattern changes
    Vector3   _lastDirection;
    Vector3   _exitCenterStart, _exitCenterEnd, _exitOffset;
    float     _stateTime, _patternTime, _pathDistance, _phaseOffset;
    int       _pathDirection = 1;
    float     _nextShot;
    CurvePath _pathCurve;
    bool      _notified;
    Renderer[] _renderers;
    Collider[] _colliders;

    public bool IsActive => _state == State.Entering || _state == State.Active || _state == State.Exiting;

    // ─── LIFECYCLE ────────────────────────────────────────────────────────────
    protected virtual void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);

        Health = GetComponent<Health>();
        if (Health == null) Health = gameObject.AddComponent<Health>();
        Health.Team = Team.Enemy;
        Health.Died += OnHealthDepleted;

        // Kinematic Rigidbody: moving colliders without a Rigidbody is expensive for physics
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        SetVisible(false);
    }

    protected virtual void Start()
    {
        _center = anchor == Anchor.World && useObjectPosition ? transform.position : position;
        _localPos = _center + entryOffset;
        ApplyTransform(instant: true);

        if (!activateByDistance) Activate();
    }

    protected virtual void OnDestroy()
    {
        if (Health != null) Health.Died -= OnHealthDepleted;
        Notify(killed: false);   // in case it gets destroyed externally, so the wave doesn't wait forever
    }

    protected virtual void Update()
    {
        float dt = Time.deltaTime;
        _stateTime += dt;

        switch (_state)
        {
            case State.Dormant:
                if (activateByDistance && PlayerAlive() &&
                    Vector3.Distance(ToWorld(_center), Player.Current.transform.position) <= activationDistance)
                    Activate();
                return;

            case State.Entering:
            {
                float t = entryDuration > 0f ? Mathf.Clamp01(_stateTime / entryDuration) : 1f;
                _localPos = Vector3.LerpUnclamped(_center + entryOffset, _center, entryCurve.Evaluate(t));
                if (t >= 1f) StartPattern();
                break;
            }

            case State.Active:
                UpdatePattern(dt);
                if (_state != State.Active) break;   // the path may have triggered the exit
                UpdateFiring();
                if (timeOnScreen > 0f && _stateTime >= timeOnScreen) BeginExit();
                break;

            case State.Exiting:
            {
                float t = Mathf.Clamp01(_stateTime / exitDuration);
                _center = Vector3.LerpUnclamped(_exitCenterStart, _exitCenterEnd, t * t * t);
                _localPos = _center + (movement.IsPath ? _exitOffset : PatternOffset(dt) * (1f - t));
                if (t >= 1f) { Despawn(killed: false); return; }
                break;
            }

            case State.Dead:
                return;
        }

        ApplyTransform(instant: false);
    }

    // ─── CONFIGURATION FROM A WAVE ────────────────────────────────────────────
    /// <summary>
    /// Called by the LevelDirector right after instantiating (before Start).
    /// </summary>
    public virtual void ConfigureFromWave(WaveEntry e, int index)
    {
        anchor = e.anchor;
        useObjectPosition = false;
        position = e.position + e.spacing * index;
        activateByDistance = false;

        if (e.overrideEntry)        entryOffset  = e.entryOffset;
        if (e.overrideMovement)     movement     = e.movement.Clone();
        if (e.overrideTimeOnScreen) timeOnScreen = e.timeOnScreen;
        _phaseOffset = e.phaseOffsetPerUnit * index;
    }

    /// <summary>World spawn position (to instantiate without flicker).</summary>
    public static Vector3 SpawnPosition(WaveEntry e, int index, Enemy prefab)
    {
        Vector3 offset = e.overrideEntry ? e.entryOffset : prefab.entryOffset;
        Vector3 local  = e.position + e.spacing * index + offset;
        return e.anchor == Anchor.RailRig && RailRig.Current != null ? RailRig.Current.ToWorld(local) : local;
    }

    // ─── STATE TRANSITIONS ────────────────────────────────────────────────────
    public void Activate()
    {
        if (_state != State.Dormant) return;
        SetVisible(true);
        _state     = State.Entering;
        _stateTime = 0f;
        if (invulnerableOnEntry) Health.Invulnerable = true;
        OnActivated();
        if (entryDuration <= 0f) StartPattern();
    }

    void StartPattern()
    {
        if (invulnerableOnEntry) Health.Invulnerable = false;
        _localPos  = _center;
        _state     = State.Active;
        _stateTime = 0f;
        ResetPattern();
        _nextShot = Time.time + firing.initialDelay + UnityEngine.Random.Range(0f, firing.interval * 0.3f);
        OnPatternStarted();
    }

    /// <summary>Changes the movement pattern with a smooth transition (used by bosses).</summary>
    public void SetPattern(MovementPattern pattern)
    {
        movement = pattern.Clone();
        if (_state == State.Active) ResetPattern();
    }

    void ResetPattern()
    {
        _blendFrom     = _localPos - _center;
        _patternTime   = _phaseOffset;
        _pathCurve     = movement.CreateCurve();
        _pathDistance  = movement.IsPath ? _phaseOffset * movement.pathSpeed : 0f;
        _pathDirection = 1;
    }

    /// <summary>Starts leaving the scene.</summary>
    public void BeginExit()
    {
        if (_state != State.Active && _state != State.Entering) return;
        _state     = State.Exiting;
        _stateTime = 0f;

        Vector3 dir = exitDirection.sqrMagnitude > 0f ? exitDirection.normalized
                    : _lastDirection.sqrMagnitude > 0f ? _lastDirection
                    : (entryOffset.sqrMagnitude > 0f ? -entryOffset.normalized : Vector3.up);

        _exitCenterStart = _center;
        _exitCenterEnd   = _center + dir * exitDistance;
        _exitOffset      = _localPos - _center;
    }

    // ─── MOVEMENT ─────────────────────────────────────────────────────────────
    void UpdatePattern(float dt)
    {
        Vector3 previous = _localPos;
        _localPos = _center + PatternOffset(dt);

        Vector3 delta = _localPos - previous;
        if (delta.sqrMagnitude > 1e-6f) _lastDirection = delta.normalized;
    }

    /// <summary>Advances the pattern and returns the current offset from the center.</summary>
    Vector3 PatternOffset(float dt)
    {
        _patternTime += dt;
        float blend = movement.blendDuration > 0f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_patternTime - _phaseOffset) / movement.blendDuration))
            : 1f;

        Vector3 target = _pathCurve != null ? AdvancePath(dt) : movement.Offset(_patternTime);
        return Vector3.Lerp(_blendFrom, target, blend);
    }

    Vector3 AdvancePath(float dt)
    {
        if (_state == State.Active)
        {
            _pathDistance += movement.pathSpeed * dt * _pathDirection;
            float L = _pathCurve.Length;

            switch (movement.pathMode)
            {
                case PathMode.PingPong:
                    if (_pathDistance >= L) { _pathDistance = L; _pathDirection = -1; }
                    else if (_pathDistance <= 0f) { _pathDistance = 0f; _pathDirection = 1; }
                    break;
                case PathMode.ExitAtEnd:
                    if (_pathDistance >= L) { _pathDistance = L; BeginExit(); }
                    break;
            }
        }
        return _pathCurve.Evaluate(_pathDistance);
    }

    // ─── FIRING ──────────────────────────────────────────────────────────────
    void UpdateFiring()
    {
        if (!firing.enabled || firing.bulletPrefab == null || !PlayerAlive()) return;
        if (Time.time < _nextShot) return;

        float dist = Vector3.Distance(transform.position, Player.Current.transform.position);
        if (dist > firing.range) return;

        _nextShot = Time.time + firing.interval;
        if (firing.burstCount > 1) StartCoroutine(Burst(firing.Clone()));
        else Shoot(firing);
    }

    IEnumerator Burst(FirePattern p)
    {
        for (int i = 0; i < p.burstCount; i++)
        {
            if (_state != State.Active || !PlayerAlive()) yield break;
            Shoot(p);
            if (p.burstInterval > 0f) yield return new WaitForSeconds(p.burstInterval);
        }
    }

    /// <summary>Fires once with the given pattern. Override for special attacks.</summary>
    protected virtual void Shoot(FirePattern p)
    {
        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        Vector3 dir = p.aimAtPlayer && PlayerAlive()
            ? (Player.Current.transform.position - origin).normalized
            : transform.forward;
        if (dir == Vector3.zero) dir = transform.forward;

        Vector3 inherited = anchor == Anchor.RailRig && RailRig.Current != null ? RailRig.Current.Velocity : Vector3.zero;
        Quaternion baseRot = Quaternion.LookRotation(dir);
        int n = Mathf.Max(1, p.bulletsPerShot);
        bool ring = p.spreadAngle >= 359f;

        for (int i = 0; i < n; i++)
        {
            float t   = n > 1 ? (ring ? (float)i / n : (float)i / (n - 1)) : 0.5f;
            float ang = ring ? t * 360f : Mathf.Lerp(-p.spreadAngle * 0.5f, p.spreadAngle * 0.5f, t);
            Vector3 d = baseRot * Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Bullet.Spawn(p.bulletPrefab, origin, d, p.bulletSpeed, p.damage, Team.Enemy, inherited);
        }
    }

    // ─── HEALTH ───────────────────────────────────────────────────────────────
    void OnHealthDepleted(Health _)
    {
        if (_state == State.Dead) return;
        GameState.Instance.AddScore(scoreValue);
        if (deathEffect != null) Instantiate(deathEffect, transform.position, transform.rotation);
        OnKilled();
        Despawn(killed: true);
    }

    protected void Despawn(bool killed)
    {
        if (_state == State.Dead) return;
        _state = State.Dead;
        StopAllCoroutines();
        Notify(killed);
        Destroy(gameObject);
    }

    void Notify(bool killed)
    {
        if (_notified) return;
        _notified = true;
        Despawned?.Invoke(this, killed);
    }

    // ─── HOOKS FOR SUBCLASSES ─────────────────────────────────────────────────
    protected virtual void OnActivated() { }
    protected virtual void OnPatternStarted() { }
    protected virtual void OnKilled() { }

    // ─── UTILITIES ────────────────────────────────────────────────────────────
    static bool PlayerAlive() => Player.Current != null && Player.Current.IsAlive;

    Transform AnchorTransform => anchor == Anchor.RailRig && RailRig.Current != null ? RailRig.Current.transform : null;

    Vector3 ToWorld(Vector3 local)
    {
        Transform a = AnchorTransform;
        return a != null ? a.TransformPoint(local) : local;
    }

    void ApplyTransform(bool instant)
    {
        transform.position = ToWorld(_localPos);

        Quaternion target;
        if (facePlayer && PlayerAlive())
        {
            Vector3 dir = Player.Current.transform.position - transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            target = Quaternion.LookRotation(dir);
        }
        else if (AnchorTransform != null)
            target = AnchorTransform.rotation * Quaternion.Euler(0f, 180f, 0f);   // facing the player
        else return;

        transform.rotation = instant || turnSpeed <= 0f
            ? target
            : Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }

    void SetVisible(bool visible)
    {
        foreach (var r in _renderers) if (r != null) r.enabled = visible;
        foreach (var c in _colliders) if (c != null) c.enabled = visible;
    }

    // ─── GIZMOS ──────────────────────────────────────────────────────────────
    protected virtual void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? _center
                       : anchor == Anchor.World && useObjectPosition ? transform.position
                       : position;

        Gizmos.matrix = Matrix4x4.identity;
        if (anchor == Anchor.RailRig)
        {
            RailRig rig = Application.isPlaying ? RailRig.Current : FindFirstObjectByType<RailRig>();
            if (rig != null) Gizmos.matrix = rig.transform.localToWorldMatrix;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(center, 0.3f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(center + entryOffset, 0.25f);
        Gizmos.DrawLine(center + entryOffset, center);

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        if (movement.IsPath)
        {
            var s = movement.CreateCurve().Samples;
            for (int i = 1; i < s.Count; i++) Gizmos.DrawLine(center + s[i - 1], center + s[i]);
            Gizmos.color = Color.magenta;
            foreach (var p in movement.points) Gizmos.DrawSphere(center + p, 0.18f);
        }
        else if (movement.type != MovementType.Static)
        {
            const int steps = 64;
            float period = 1f / Mathf.Max(movement.frequency, 0.01f);
            Vector3 prev = center + movement.Offset(0f);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 pt = center + movement.Offset(period * i / steps);
                Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
        }
    }
}
