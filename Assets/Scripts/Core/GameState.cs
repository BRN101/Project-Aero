using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton holding the global game state; persists across scenes.
///
/// The player reads its base attributes from here (max health and equipped weapon).
/// If none exists in the scene, one is created automatically when GameState.Instance is accessed.
///
/// USAGE:
///   GameState.Instance.ChangeWeapon("ShotgunWeapon");   // the player re-equips automatically
///   GameState.Instance.AddScore(100);
/// </summary>
public class GameState : MonoBehaviour
{
    static GameState _instance;

    /// <summary>Global instance. Created automatically if missing.</summary>
    public static GameState Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying)
            {
                _instance = FindFirstObjectByType<GameState>();
                if (_instance == null)
                {
                    Debug.LogWarning("[GameState] No GameState in the scene; created one with default values.");
                    _instance = new GameObject("GameState").AddComponent<GameState>();
                }
            }
            return _instance;
        }
    }

    [Header("Player base attributes")]
    [Tooltip("Addressable key of the equipped weapon. Must match the asset's address in Addressables.")]
    public string equippedWeaponKey = "BasicWeapon";

    [Tooltip("Weapons the player can cycle through (Addressable keys).")]
    public List<string> availableWeapons = new List<string> { "BasicWeapon", "ShotgunWeapon" };

    [Tooltip("Player's starting max health.")]
    [Min(1f)] public float playerMaxHealth = 100f;

    [Header("Progress")]
    public string playerName = "Player1";
    public int score = 0;
    public int level = 1;

    /// <summary>Raised when the equipped weapon changes (Player subscribes and re-equips).</summary>
    public event Action<string> WeaponChanged;
    /// <summary>Raised when the score changes.</summary>
    public event Action<int> ScoreChanged;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─── API ─────────────────────────────────────────────────────────────────
    /// <summary>Equips the next (or previous, with -1) weapon in availableWeapons.</summary>
    public void NextWeapon(int direction = 1)
    {
        if (availableWeapons == null || availableWeapons.Count == 0) return;
        int i = availableWeapons.IndexOf(equippedWeaponKey);
        int n = availableWeapons.Count;
        i = i < 0 ? 0 : ((i + direction) % n + n) % n;
        ChangeWeapon(availableWeapons[i]);
    }

    public void AddScore(int points)
    {
        score += points;
        ScoreChanged?.Invoke(score);
    }

    public void ResetScore()
    {
        score = 0;
        ScoreChanged?.Invoke(score);
    }

    /// <summary>Changes the equipped weapon. Players in the scene re-equip automatically.</summary>
    public void ChangeWeapon(string newKey)
    {
        if (string.IsNullOrEmpty(newKey) || newKey == equippedWeaponKey) return;
        equippedWeaponKey = newKey;
        Debug.Log($"[GameState] Weapon changed to: {newKey}");
        WeaponChanged?.Invoke(newKey);
    }
}
