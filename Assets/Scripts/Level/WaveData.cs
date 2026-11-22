using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One wave entry: which enemy, how many, when and where.
/// Positions are in rig space (x = side, y = height, z = distance ahead).
/// </summary>
[Serializable]
public class WaveEntry
{
    [Tooltip("Note to identify the entry in the Inspector.")]
    public string note;

    public Enemy prefab;

    [Header("Timing")]
    [Tooltip("Seconds from the start of the wave until the first spawn.")]
    [Min(0f)] public float delay = 0f;
    [Min(1)] public int count = 1;
    [Tooltip("Seconds between spawns of this entry.")]
    [Min(0f)] public float interval = 0.4f;

    [Header("Position")]
    public Enemy.Anchor anchor = Enemy.Anchor.RailRig;
    [Tooltip("Position of the first enemy.")]
    public Vector3 position = new Vector3(0f, 2f, 30f);
    [Tooltip("Offset between consecutive enemies (formations).")]
    public Vector3 spacing = new Vector3(3f, 0f, 0f);
    [Tooltip("Pattern time offset between consecutive enemies (seconds). Creates 'wave' motions.")]
    public float phaseOffsetPerUnit = 0f;

    [Header("Overrides (otherwise the prefab's values are used)")]
    public bool overrideEntry = false;
    public Vector3 entryOffset = new Vector3(0f, -15f, 0f);

    public bool overrideMovement = false;
    public MovementPattern movement = new MovementPattern();

    public bool overrideTimeOnScreen = false;
    [Tooltip("0 = never leaves.")]
    [Min(0f)] public float timeOnScreen = 8f;

    public float LastSpawnTime => delay + interval * (count - 1);
}

/// <summary>
/// Reusable wave. Create it via: Create → Aero → Wave.
/// Launched by a LevelDirector step.
/// A boss is just a wave whose prefab has the Boss component.
/// </summary>
[CreateAssetMenu(menuName = "Aero/Wave", fileName = "NewWave")]
public class WaveData : ScriptableObject
{
    public List<WaveEntry> entries = new List<WaveEntry>();

    public int TotalEnemies
    {
        get
        {
            int n = 0;
            foreach (var e in entries) if (e.prefab != null) n += e.count;
            return n;
        }
    }
}
