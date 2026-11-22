using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// "Aero" menu to build levels quickly:
///   • Aero → Set Up Current Scene: adds GameState, path, RailRig + player + camera, LevelDirector and HUD.
///   • Aero → Create Sample Level: creates a playable scene with enemies, waves and a boss.
///
/// Never overwrites prefabs or waves that already exist with the same name.
/// </summary>
public static class AeroLevelBuilder
{
    const string InputActionsPath = "Assets/PlayerActions.inputactions";
    const string EnemyBulletPath  = "Assets/Prefabs/EnemyBullet.prefab";
    const string EnemiesFolder    = "Assets/Prefabs/Enemies";
    const string WavesFolder      = "Assets/Data/Waves";
    const string MaterialsFolder  = "Assets/Materials";
    const string SampleScenePath  = "Assets/Scenes/SampleLevel.unity";

    // ═══ MENUS ══════════════════════════════════════════════════════════════
    [MenuItem("Aero/Set Up Current Scene", priority = 0)]
    static void SetUpCurrentScene()
    {
        if (Object.FindFirstObjectByType<RailRig>() != null &&
            !EditorUtility.DisplayDialog("Aero", "The scene already has a RailRig. Add another one anyway?", "Yes", "Cancel"))
            return;

        var playerMat = CreateMaterial("Player", new Color(0.3f, 0.6f, 1f));
        var rig = CreateCore(playerMat);
        Undo.RegisterCreatedObjectUndo(rig.gameObject, "Set Up Aero Scene");
        Selection.activeGameObject = rig.gameObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Aero] Scene set up. Add nodes to 'Path' and steps to the LevelDirector.");
    }

    [MenuItem("Aero/Create Sample Level", priority = 1)]
    static void CreateSampleLevel()
    {
        if (!EditorUtility.DisplayDialog("Aero",
                $"A new scene will be created at {SampleScenePath} with sample enemies, waves and a boss.", "Create", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ── Assets ──
        var playerMat      = CreateMaterial("Player",      new Color(0.3f, 0.6f, 1f));
        var enemyMat       = CreateMaterial("Enemy",       new Color(0.9f, 0.25f, 0.3f));
        var bossMat        = CreateMaterial("Boss",        new Color(0.6f, 0.2f, 0.8f));
        var environmentMat = CreateMaterial("Environment", new Color(0.55f, 0.6f, 0.65f));
        var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBulletPath);
        if (bullet == null) Debug.LogWarning($"[Aero] {EnemyBulletPath} not found; enemies won't shoot.");

        Enemy basic = CreateEnemyPrefab<Enemy>("BasicEnemy", PrimitiveType.Cube, Vector3.one * 1.4f, enemyMat, 30f, e =>
        {
            e.scoreValue = 100;
            e.timeOnScreen = 10f;
            e.movement.type = MovementType.HorizontalOscillation;
            e.movement.amplitude = 2.5f;
            SetUpFiring(e.firing, bullet, interval: 2.4f, speed: 28f);
        });

        Boss boss = CreateEnemyPrefab<Boss>("SampleBoss", PrimitiveType.Cube, new Vector3(6f, 3f, 3f), bossMat, 600f, b =>
        {
            b.bossName = "Guardian";
            b.scoreValue = 5000;
            b.position = new Vector3(0f, 3f, 38f);
            b.entryOffset = new Vector3(0f, 25f, 15f);
            b.entryDuration = 3f;
            b.invulnerableOnEntry = true;
            b.timeOnScreen = 0f;
            SetUpFiring(b.firing, bullet, 1.6f, 26f);

            b.phases.Add(MakePhase("Phase 1", 1f,    MovementType.Figure8,  5f, 0.15f, bullet, bullets: 5,  spread: 40f,  burst: 1, interval: 1.6f));
            b.phases.Add(MakePhase("Phase 2", 0.6f,  MovementType.Circular, 4f, 0.25f, bullet, bullets: 3,  spread: 25f,  burst: 3, interval: 1.5f));
            b.phases.Add(MakePhase("Phase 3", 0.25f, MovementType.Wave,     4f, 0.35f, bullet, bullets: 12, spread: 360f, burst: 1, interval: 1.2f));
        });

        WaveData squadron = CreateWave("Wave1_Squadron", w => w.entries.Add(new WaveEntry
        {
            note = "Row of 5 entering from below",
            prefab = basic, count = 5, interval = 0.25f,
            position = new Vector3(-6f, 3f, 32f), spacing = new Vector3(3f, 0f, 0f),
            phaseOffsetPerUnit = 0.3f
        }));

        WaveData snake = CreateWave("Wave2_Snake", w => w.entries.Add(new WaveEntry
        {
            note = "Crosses from left to right along a path",
            prefab = basic, count = 6, interval = 0.45f,
            position = new Vector3(-14f, 3f, 30f), spacing = Vector3.zero,
            overrideEntry = true, entryOffset = new Vector3(-15f, 0f, 0f),
            overrideMovement = true,
            movement = new MovementPattern
            {
                type = MovementType.Path, blendDuration = 0.3f,
                points = new[] { Vector3.zero, new Vector3(7f, -4f, -4f), new Vector3(14f, 0f, -8f),
                                 new Vector3(21f, 4f, -4f), new Vector3(28f, 0f, 0f), new Vector3(36f, -2f, 4f) },
                pathSpeed = 9f, pathMode = PathMode.ExitAtEnd
            },
            overrideTimeOnScreen = true, timeOnScreen = 0f
        }));

        WaveData pincer = CreateWave("Wave3_Pincer", w =>
        {
            foreach (float side in new[] { -1f, 1f })
                w.entries.Add(new WaveEntry
                {
                    note = side < 0 ? "Left column" : "Right column",
                    prefab = basic, count = 3, interval = 0.2f,
                    position = new Vector3(9f * side, -2.5f, 26f), spacing = new Vector3(0f, 2.5f, 0f),
                    overrideEntry = true, entryOffset = new Vector3(22f * side, 0f, 0f),
                    overrideMovement = true,
                    movement = new MovementPattern { type = MovementType.VerticalOscillation, amplitude = 1.5f, frequency = 0.5f },
                    phaseOffsetPerUnit = 0.4f
                });
        });

        WaveData bossWave = CreateWave("Wave4_Boss", w => w.entries.Add(new WaveEntry
        {
            note = "Boss", prefab = boss, count = 1,
            position = boss.position, spacing = Vector3.zero
        }));

        // ── Scene ──
        RailRig rig = CreateCore(playerMat);
        CreateEnvironment(rig.path, environmentMat);

        var director = Object.FindFirstObjectByType<LevelDirector>();
        director.steps.Add(new LevelDirector.Step { name = "Squadron", trigger = LevelDirector.Trigger.RailDistance,      value = 25f,  wave = squadron });
        director.steps.Add(new LevelDirector.Step { name = "Snake",    trigger = LevelDirector.Trigger.PreviousCleared,   value = 1f,   wave = snake });
        director.steps.Add(new LevelDirector.Step { name = "Pincer",   trigger = LevelDirector.Trigger.TimeAfterPrevious, value = 6f,   wave = pincer });
        director.steps.Add(new LevelDirector.Step { name = "Boss",     trigger = LevelDirector.Trigger.RailDistance,      value = 330f, wave = bossWave, pauseRailUntilCleared = true });

        EditorSceneManager.SaveScene(scene, SampleScenePath);
        AddToBuildSettings(SampleScenePath);
        Selection.activeGameObject = director.gameObject;
        Debug.Log($"[Aero] Sample level created at {SampleScenePath}. Press Play.");
    }

    // ═══ CORE: GameState, path, rig, player, camera, director, HUD ═════════
    static RailRig CreateCore(Material playerMat)
    {
        if (Object.FindFirstObjectByType<GameState>() == null)
            new GameObject("GameState").AddComponent<GameState>();

        // Path
        var path = new GameObject("Path").AddComponent<RailPath>();
        Vector3[] nodes =
        {
            new Vector3(0f, 0f, 0f),     new Vector3(0f, 0f, 90f),    new Vector3(25f, 6f, 170f),
            new Vector3(25f, 10f, 260f), new Vector3(-10f, 6f, 340f), new Vector3(-10f, 6f, 440f)
        };
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = new GameObject($"Node {i}").transform;
            n.SetParent(path.transform, false);
            n.position = nodes[i];
        }
        path.Rebuild();

        // Rig
        var rig = new GameObject("RailRig").AddComponent<RailRig>();
        rig.path = path;
        rig.transform.SetPositionAndRotation(path.PointAt(0f), Quaternion.LookRotation(path.TangentAt(0f)));

        // Player
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.transform.SetParent(rig.transform, false);
        player.transform.localScale = Vector3.one * 0.8f;
        player.GetComponent<Renderer>().sharedMaterial = playerMat;

        var health = player.AddComponent<Health>();
        health.Configure(Team.Player, 100f);
        var so = new SerializedObject(health);
        so.FindProperty("invulnerabilityAfterHit").floatValue = 0.6f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var rb = player.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        var input = player.AddComponent<PlayerInput>();
        input.actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        input.defaultActionMap = "Shooter";
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        if (input.actions == null) Debug.LogWarning($"[Aero] {InputActionsPath} not found.");

        player.AddComponent<PlayerAim>();
        player.AddComponent<Player>();

        // Camera
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.transform.SetParent(rig.transform, false);
        cam.farClipPlane = 800f;
        var railCam = cam.gameObject.AddComponent<RailCamera>();
        railCam.rig = rig;
        cam.transform.localPosition = railCam.offset;
        cam.transform.localRotation = Quaternion.LookRotation(railCam.lookAt - railCam.offset);

        // Director
        new GameObject("LevelDirector").AddComponent<LevelDirector>().rig = rig;

        CreateHUD();
        return rig;
    }

    static void CreateEnvironment(RailPath path, Material mat)
    {
        var root = new GameObject("Environment").transform;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.position = new Vector3(0f, -14f, 220f);
        ground.transform.localScale = new Vector3(60f, 1f, 60f);
        ground.GetComponent<Renderer>().sharedMaterial = mat;

        // Pillars on both sides of the path: a visual reference for forward motion
        var rnd = new System.Random(1234);
        float L = path.Length;
        for (int i = 0; i < 70; i++)
        {
            float d = L * i / 70f;
            Vector3 p = path.PointAt(d);
            Vector3 side = Vector3.Cross(Vector3.up, path.TangentAt(d)).normalized;
            float sign   = (i % 2 == 0) ? 1f : -1f;
            float dist   = 16f + (float)rnd.NextDouble() * 30f;
            float height = 10f + (float)rnd.NextDouble() * 35f;

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = $"Pillar {i}";
            pillar.transform.SetParent(root, false);
            pillar.transform.position = new Vector3(0f, -14f + height * 0.5f, 0f) + Vector3.Scale(p + side * dist * sign, new Vector3(1f, 0f, 1f));
            pillar.transform.localScale = new Vector3(3f + (float)rnd.NextDouble() * 3f, height, 3f + (float)rnd.NextDouble() * 3f);
            pillar.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static void CreateHUD()
    {
        var canvasGO = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Reticle: drawn procedurally from the equipped weapon (Weapon.reticle)
        var reticle = new GameObject("Reticle", typeof(RectTransform), typeof(ReticleUI));
        reticle.transform.SetParent(canvasGO.transform, false);
        ((RectTransform)reticle.transform).sizeDelta = Vector2.zero;

        CreateBar(canvasGO.transform, "PlayerHealth", HealthBarUI.Source.Player,
                  new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(420f, 26f), new Color(0.3f, 0.9f, 0.4f), withLabel: false);
        CreateBar(canvasGO.transform, "BossHealth", HealthBarUI.Source.ActiveBoss,
                  new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 22f), new Color(0.9f, 0.25f, 0.3f), withLabel: true);
    }

    static void CreateBar(Transform parent, string name, HealthBarUI.Source source, Vector2 anchor,
                          Vector2 pos, Vector2 size, Color color, bool withLabel)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(HealthBarUI));
        root.transform.SetParent(parent, false);
        var rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);
        Stretch((RectTransform)background.transform);
        var bgImg = background.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);
        bgImg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(root.transform, false);
        Stretch((RectTransform)fill.transform, 3f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.sprite = sprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.color = color;
        fillImg.raycastTarget = false;

        var bar = root.GetComponent<HealthBarUI>();
        bar.source = source;
        bar.fill   = fillImg;

        if (withLabel)
        {
            var txt = new GameObject("Name", typeof(RectTransform), typeof(Text));
            txt.transform.SetParent(root.transform, false);
            var trt = (RectTransform)txt.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 0f);
            trt.anchoredPosition = new Vector2(0f, 4f);
            trt.sizeDelta = new Vector2(0f, 32f);
            var t = txt.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 24;
            t.alignment = TextAnchor.LowerCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            bar.label = t;
        }

        if (source == HealthBarUI.Source.ActiveBoss) root.GetComponent<CanvasGroup>().alpha = 0f;
    }

    static void Stretch(RectTransform rt, float margin = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    // ═══ ASSETS ═════════════════════════════════════════════════════════════
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Material CreateMaterial(string name, Color color)
    {
        EnsureFolder(MaterialsFolder);
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static T CreateEnemyPrefab<T>(string name, PrimitiveType shape, Vector3 scale, Material mat, float health,
                                  System.Action<T> configure) where T : Enemy
    {
        EnsureFolder(EnemiesFolder);
        string path = $"{EnemiesFolder}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.AddComponent<Health>().Configure(Team.Enemy, health);
        configure(go.AddComponent<T>());

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<T>();
    }

    static WaveData CreateWave(string name, System.Action<WaveData> configure)
    {
        EnsureFolder(WavesFolder);
        string path = $"{WavesFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<WaveData>(path);
        if (existing != null) return existing;

        var w = ScriptableObject.CreateInstance<WaveData>();
        configure(w);
        AssetDatabase.CreateAsset(w, path);
        return w;
    }

    static void SetUpFiring(FirePattern f, GameObject bullet, float interval, float speed)
    {
        f.bulletPrefab = bullet;
        f.interval = interval;
        f.bulletSpeed = speed;
    }

    static Boss.Phase MakePhase(string name, float threshold, MovementType movement, float amplitude, float frequency,
                                GameObject bullet, int bullets, float spread, int burst, float interval) => new Boss.Phase
    {
        name = name,
        healthThreshold = threshold,
        movement = new MovementPattern { type = movement, amplitude = amplitude, radius = amplitude, frequency = frequency },
        firing = new FirePattern
        {
            bulletPrefab = bullet, bulletsPerShot = bullets, spreadAngle = spread,
            burstCount = burst, interval = interval, bulletSpeed = 26f, damage = 10f, initialDelay = 1f
        }
    };

    static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
