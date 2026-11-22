using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Level script: a list of steps executed in order.
///
/// Each step waits for its trigger and then launches a wave, changes the rail
/// speed and/or raises events. Typical example:
///
///   1. Distance 40     → Wave "Squadron"
///   2. Previous cleared → Wave "Snake"
///   3. Time 6 s        → Wave "Turrets"
///   4. Distance 400    → Wave "Boss" (pause rail until cleared)
///
/// The level completes once every step has run, no enemies remain and
/// (optionally) the rig has reached the end of the path.
/// </summary>
public class LevelDirector : MonoBehaviour
{
    public enum Trigger
    {
        RailDistance,       // when the rig has travelled 'value' units
        TimeAfterPrevious,  // 'value' seconds after the previous step started
        PreviousCleared     // when the previous step's wave has been cleared (+ 'value' s)
    }

    [Serializable]
    public class Step
    {
        public string name = "Step";
        public Trigger trigger = Trigger.RailDistance;
        [Tooltip("Distance (units) or seconds, depending on the trigger.")]
        public float value = 0f;

        [Tooltip("Wave to launch (optional).")]
        public WaveData wave;

        [Tooltip("Stops the rail until this wave is cleared (bosses, ambushes).")]
        public bool pauseRailUntilCleared = false;

        public bool changeRailSpeed = false;
        public float railSpeed = 12f;

        public UnityEvent onStart;
        public UnityEvent onCleared;
    }

    /// <summary>Runtime state of a launched wave.</summary>
    public class ActiveWave
    {
        public WaveData Data;
        public int  Pending;
        public int  Alive;
        public int  Killed;
        public bool Cleared => Pending == 0 && Alive == 0;
    }

    [Tooltip("Empty = RailRig.Current.")]
    public RailRig rig;
    public List<Step> steps = new List<Step>();

    [Header("Level end")]
    [Tooltip("Wait for the rig to reach the end of the path before completing the level.")]
    public bool waitForRailEnd = true;
    public float completeDelay = 2f;
    public UnityEvent onLevelComplete;

    [Header("Game Over")]
    public bool restartOnDeath = true;
    public float restartDelay = 3f;
    public UnityEvent onGameOver;

    public event Action<int, Step> StepStarted;

    public int  CurrentStep { get; private set; } = -1;
    public bool Completed   { get; private set; }
    public bool GameOver    { get; private set; }

    readonly List<ActiveWave> _active = new List<ActiveWave>();
    Health _playerHealth;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Start()
    {
        if (rig == null) rig = RailRig.Current;
        if (Player.Current != null)
        {
            _playerHealth = Player.Current.Health;
            _playerHealth.Died += OnPlayerDied;
        }
        else Debug.LogWarning("[LevelDirector] No Player in the scene.");

        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
    }

    // ─── Sequence ────────────────────────────────────────────────────────────
    IEnumerator Run()
    {
        float previousStart = Time.time;
        ActiveWave previous = null;

        for (int i = 0; i < steps.Count; i++)
        {
            Step s = steps[i];

            switch (s.trigger)
            {
                case Trigger.RailDistance:
                    if (rig != null) yield return new WaitUntil(() => rig.Distance >= s.value);
                    break;
                case Trigger.TimeAfterPrevious:
                    float target = previousStart + s.value;
                    yield return new WaitUntil(() => Time.time >= target);
                    break;
                case Trigger.PreviousCleared:
                    var awaited = previous;
                    if (awaited != null) yield return new WaitUntil(() => awaited.Cleared);
                    if (s.value > 0f) yield return new WaitForSeconds(s.value);
                    break;
            }
            if (GameOver) yield break;

            CurrentStep = i;
            previousStart = Time.time;

            if (s.changeRailSpeed && rig != null) rig.SetSpeed(s.railSpeed);
            s.onStart?.Invoke();
            StepStarted?.Invoke(i, s);

            previous = s.wave != null ? LaunchWave(s.wave) : null;
            StartCoroutine(TrackStep(s, previous));
        }

        // Level end
        yield return new WaitUntil(() => _active.Count == 0);
        if (waitForRailEnd && rig != null && rig.path != null && !rig.path.closed)
            yield return new WaitUntil(() => rig.AtEnd);
        if (GameOver) yield break;

        yield return new WaitForSeconds(completeDelay);
        Completed = true;
        Debug.Log("[LevelDirector] Level complete!");
        onLevelComplete?.Invoke();
    }

    IEnumerator TrackStep(Step s, ActiveWave wave)
    {
        bool paused = s.pauseRailUntilCleared && rig != null && wave != null;
        if (paused) rig.Pause();

        if (wave != null) yield return new WaitUntil(() => wave.Cleared);

        if (paused) rig.Resume();
        if (!GameOver) s.onCleared?.Invoke();
    }

    // ─── Waves ───────────────────────────────────────────────────────────────
    /// <summary>Launches a wave. Can also be called from other scripts or events.</summary>
    public ActiveWave LaunchWave(WaveData data)
    {
        var wave = new ActiveWave { Data = data };

        // Sort every spawn by time
        var spawns = new List<(float t, WaveEntry e, int i)>();
        foreach (var e in data.entries)
        {
            if (e.prefab == null) { Debug.LogWarning($"[LevelDirector] Entry without prefab in '{data.name}'."); continue; }
            for (int i = 0; i < e.count; i++) spawns.Add((e.delay + e.interval * i, e, i));
        }
        spawns.Sort((a, b) => a.t.CompareTo(b.t));

        wave.Pending = spawns.Count;
        _active.Add(wave);
        StartCoroutine(Spawn(wave, spawns));
        return wave;
    }

    IEnumerator Spawn(ActiveWave wave, List<(float t, WaveEntry e, int i)> spawns)
    {
        float start = Time.time;
        foreach (var s in spawns)
        {
            float when = start + s.t;
            if (Time.time < when) yield return new WaitForSeconds(when - Time.time);
            if (GameOver) yield break;

            Enemy enemy = Instantiate(s.e.prefab, Enemy.SpawnPosition(s.e, s.i, s.e.prefab), Quaternion.identity);
            enemy.ConfigureFromWave(s.e, s.i);
            wave.Alive++;
            wave.Pending--;

            enemy.Despawned += (en, killed) =>
            {
                wave.Alive--;
                if (killed) wave.Killed++;
                if (wave.Cleared) _active.Remove(wave);
            };
        }
        if (wave.Cleared) _active.Remove(wave);
    }

    // ─── Game Over ───────────────────────────────────────────────────────────
    void OnPlayerDied(Health _)
    {
        if (GameOver || Completed) return;
        GameOver = true;
        if (rig != null) rig.Pause();
        Debug.Log("[LevelDirector] Game Over.");
        onGameOver?.Invoke();
        if (restartOnDeath) StartCoroutine(RestartAfterDelay());
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(restartDelay);
        RestartLevel();
    }

    public void RestartLevel()
    {
        GameState.Instance.ResetScore();
        Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // In the editor this works even if the scene is not in Build Settings
        if (scene.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        SceneManager.LoadScene(scene.buildIndex);
    }

    // ─── Gizmos: marks on the path where each distance step happens ──────────
    void OnDrawGizmosSelected()
    {
        RailRig r = rig != null ? rig : FindFirstObjectByType<RailRig>();
        if (r == null || r.path == null) return;

        foreach (var s in steps)
        {
            if (s.trigger != Trigger.RailDistance) continue;
            Vector3 pos = r.path.PointAt(s.value);
            Gizmos.color = s.pauseRailUntilCleared ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(pos, Vector3.one * 2f);
        }
    }
}
