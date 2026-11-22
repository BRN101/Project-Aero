using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Boss or mini-boss: an Enemy with phases that change based on remaining health.
///
/// Each phase can change the movement and firing patterns, and raises an event
/// (for animations, sounds, spawning minions...).
/// A mini-boss is simply a Boss with a single phase (or none).
///
/// To stop the rail during the fight, tick "Pause Rail Until Cleared" on the
/// LevelDirector step that spawns it.
/// Health bars in "ActiveBoss" mode (HealthBarUI) hook up to Boss.Current automatically.
/// </summary>
public class Boss : Enemy
{
    [Serializable]
    public class Phase
    {
        public string name = "Phase";
        [Tooltip("The phase starts when health (0-1) drops to this value or lower. The first phase should use 1.")]
        [Range(0f, 1f)] public float healthThreshold = 1f;

        public bool overrideMovement = true;
        public MovementPattern movement = new MovementPattern();

        public bool overrideFiring = true;
        public FirePattern firing = new FirePattern();

        [Tooltip("Seconds of invulnerability when the phase starts.")]
        [Min(0f)] public float invulnerability = 1f;

        public UnityEvent onStart;
    }

    public static Boss Current { get; private set; }
    /// <summary>Raised when a boss appears (for UI, music, etc.).</summary>
    public static event Action<Boss> Appeared;

    [Header("Boss")]
    public string bossName = "Boss";
    [Tooltip("Sorted from highest to lowest health threshold.")]
    public List<Phase> phases = new List<Phase>();

    public event Action<Boss, int> PhaseChanged;

    public int    CurrentPhase { get; private set; } = -1;
    public Health BossHealth   => Health;

    float _invulnerableUntil;

    protected override void Awake()
    {
        base.Awake();
        Health.Changed += CheckPhase;
    }

    protected override void Start()
    {
        phases.Sort((a, b) => b.healthThreshold.CompareTo(a.healthThreshold));
        if (phases.Count > 0) ApplyPhase(0, applyInvulnerability: false);

        base.Start();

        Current = this;
        Appeared?.Invoke(this);
    }

    protected override void OnDestroy()
    {
        if (Health != null) Health.Changed -= CheckPhase;
        if (Current == this) Current = null;
        base.OnDestroy();
    }

    protected override void Update()
    {
        base.Update();
        if (_invulnerableUntil > 0f && Time.time >= _invulnerableUntil)
        {
            _invulnerableUntil = 0f;
            if (_state == State.Active) Health.Invulnerable = false;
        }
    }

    void CheckPhase(Health h)
    {
        if (!h.IsAlive) return;
        int next = CurrentPhase;
        for (int i = CurrentPhase + 1; i < phases.Count; i++)
            if (h.Normalized <= phases[i].healthThreshold) next = i;
        if (next != CurrentPhase) ApplyPhase(next, applyInvulnerability: true);
    }

    void ApplyPhase(int index, bool applyInvulnerability)
    {
        CurrentPhase = index;
        Phase p = phases[index];

        if (p.overrideMovement) SetPattern(p.movement);
        if (p.overrideFiring)
        {
            GameObject currentBullet = firing.bulletPrefab;
            firing = p.firing.Clone();
            if (firing.bulletPrefab == null) firing.bulletPrefab = currentBullet;
        }

        if (applyInvulnerability && p.invulnerability > 0f)
        {
            Health.Invulnerable = true;
            _invulnerableUntil  = Time.time + p.invulnerability;
        }

        p.onStart?.Invoke();
        PhaseChanged?.Invoke(this, index);
    }
}
