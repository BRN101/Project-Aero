using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Player controller for an on-rails shooter.
///
/// MOVEMENT:
///   Only on the parent's (RailRig's) local XY plane: left/right and up/down,
///   within bounds. Forward motion comes from the RailRig, never from the player.
///
/// BASE ATTRIBUTES (read from GameState):
///   • Max health → Health
///   • Equipped weapon → instantiated via Addressables as a child. Re-equips automatically
///     when GameState.equippedWeaponKey changes (GameState.ChangeWeapon).
///
/// REQUIREMENTS: PlayerInput with the actions Move, Shoot, AimPointer, AimStick and NextWeapon.
/// Health, PlayerAim and a (kinematic) Rigidbody are added automatically if missing.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public class Player : MonoBehaviour
{
    public static Player Current { get; private set; }

    [Header("Movement (rig XY plane)")]
    public float speed = 9f;
    [Tooltip("Acceleration and braking (units/s²). Higher = snappier response.")]
    public float acceleration = 45f;
    [Tooltip("Half width and half height of the movement area.")]
    public Vector2 bounds = new Vector2(7f, 4f);
    [Tooltip("Center of the movement area in rig space.")]
    public Vector2 boundsCenter = Vector2.zero;

    [Header("Visual tilt")]
    [Tooltip("Object that tilts while moving. Empty = this object.")]
    public Transform visual;
    public float maxRoll  = 25f;
    public float maxPitch = 12f;
    public float tiltSmoothing = 8f;

    [Header("Weapon")]
    [Tooltip("Where the weapon is instantiated. Empty = this object.")]
    public Transform weaponMount;
    [Tooltip("Local position of the weapon relative to the mount.")]
    public Vector3 weaponOffset = new Vector3(0.5f, 0f, 0.5f);

    [Header("Input actions")]
    public string moveAction         = "Move";
    public string shootAction        = "Shoot";
    public string switchWeaponAction = "NextWeapon";

    Health      _health;
    PlayerAim   _aim;
    PlayerInput _input;
    InputAction _move;
    InputAction _shoot;
    InputAction _switchWeapon;
    Weapon      _weapon;
    Vector2     _velocity;

    AsyncOperationHandle<GameObject> _weaponHandle;
    int _weaponVersion;

    public Health    Health        => _health;
    public PlayerAim Aim           => _aim;
    public Weapon    CurrentWeapon => _weapon;
    public bool      IsAlive       => _health != null && _health.IsAlive;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    void Awake()
    {
        Current = this;
        _input = GetComponent<PlayerInput>();

        _health = GetComponent<Health>();
        if (_health == null) _health = gameObject.AddComponent<Health>();
        _health.Team = Team.Player;

        _aim = GetComponent<PlayerAim>();
        if (_aim == null) _aim = gameObject.AddComponent<PlayerAim>();

        // Kinematic Rigidbody: needed for triggers, without physics pushing the player around
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        if (visual == null) visual = transform;
        if (weaponMount == null) weaponMount = transform;

        _health.Died    += OnDied;
        _health.Damaged += OnDamaged;
    }

    void Start()
    {
        if (_input.actions != null)
        {
            _move         = _input.actions.FindAction(moveAction,         throwIfNotFound: false);
            _shoot        = _input.actions.FindAction(shootAction,        throwIfNotFound: false);
            _switchWeapon = _input.actions.FindAction(switchWeaponAction, throwIfNotFound: false);
        }
        if (_move == null || _shoot == null)
            Debug.LogError($"[Player] Actions '{moveAction}' or '{shootAction}' are missing from the PlayerInput.");

        var gs = GameState.Instance;
        _health.SetMax(gs.playerMaxHealth, refill: true);
        gs.WeaponChanged += OnWeaponChanged;

        EquipWeaponFromGameState();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (_health != null) { _health.Died -= OnDied; _health.Damaged -= OnDamaged; }
        // Look up the existing GameState without creating a new one during shutdown
        var gs = FindFirstObjectByType<GameState>();
        if (gs != null) gs.WeaponChanged -= OnWeaponChanged;
        ReleaseWeapon();
    }

    void Update()
    {
        if (!IsAlive) return;

        Move();

        if (_switchWeapon != null && _switchWeapon.WasPressedThisFrame())
            GameState.Instance.NextWeapon();

        if (_weapon != null)
        {
            AimData aim = _aim.Data;
            _weapon.AimAt(aim.point);
            if (_shoot != null && _shoot.IsPressed()) _weapon.Fire(aim);
        }
    }

    // ─── Movement ────────────────────────────────────────────────────────────
    void Move()
    {
        float dt = Time.deltaTime;
        Vector2 input = _move != null ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        _velocity = Vector2.MoveTowards(_velocity, input * speed, acceleration * dt);

        Vector3 lp = transform.localPosition;
        lp.x += _velocity.x * dt;
        lp.y += _velocity.y * dt;

        float minX = boundsCenter.x - bounds.x, maxX = boundsCenter.x + bounds.x;
        float minY = boundsCenter.y - bounds.y, maxY = boundsCenter.y + bounds.y;
        if (lp.x < minX || lp.x > maxX) { lp.x = Mathf.Clamp(lp.x, minX, maxX); _velocity.x = 0f; }
        if (lp.y < minY || lp.y > maxY) { lp.y = Mathf.Clamp(lp.y, minY, maxY); _velocity.y = 0f; }
        transform.localPosition = lp;   // local Z never changes: the player never moves forward by itself

        // Visual tilt based on velocity
        float nx = speed > 0f ? _velocity.x / speed : 0f;
        float ny = speed > 0f ? _velocity.y / speed : 0f;
        Quaternion tilt = Quaternion.Euler(-ny * maxPitch, 0f, -nx * maxRoll);
        visual.localRotation = Quaternion.Slerp(visual.localRotation, tilt, 1f - Mathf.Exp(-tiltSmoothing * dt));
    }

    // ─── Weapon ──────────────────────────────────────────────────────────────
    void OnWeaponChanged(string _) => EquipWeaponFromGameState();

    /// <summary>Instantiates the weapon set in GameState.equippedWeaponKey.</summary>
    public void EquipWeaponFromGameState()
    {
        string key = GameState.Instance.equippedWeaponKey;
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("[Player] GameState.equippedWeaponKey is empty.");
            return;
        }

        int version = ++_weaponVersion;
        var handle = Addressables.InstantiateAsync(key, weaponMount);
        handle.Completed += h =>
        {
            // The player was destroyed or another weapon was requested while loading
            if (this == null || version != _weaponVersion)
            {
                Addressables.ReleaseInstance(h);
                return;
            }
            if (h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
            {
                Debug.LogError($"[Player] Could not instantiate '{key}' from Addressables.");
                Addressables.ReleaseInstance(h);
                return;
            }

            ReleaseWeapon();
            _weaponHandle = h;

            Transform t = h.Result.transform;
            t.localPosition = weaponOffset;
            t.localRotation = Quaternion.identity;

            _weapon = h.Result.GetComponent<Weapon>();
            if (_weapon == null) Debug.LogError($"[Player] Prefab '{key}' has no Weapon component.");
            else
            {
                _weapon.Team = Team.Player;
                _aim.weaponAssistMultiplier = _weapon.aimAssist;
            }
        };
    }

    void ReleaseWeapon()
    {
        _weapon = null;
        if (_weaponHandle.IsValid()) Addressables.ReleaseInstance(_weaponHandle);
        _weaponHandle = default;
    }

    // ─── Health ──────────────────────────────────────────────────────────────
    void OnDamaged(Health _, float amount)
    {
        if (RailCamera.Current != null) RailCamera.Current.Shake();
    }

    void OnDied(Health _)
    {
        Debug.Log("[Player] Died.");
        gameObject.SetActive(false);   // LevelDirector handles Game Over
    }

    // ─── Gizmos ──────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Transform parent = transform.parent;
        Gizmos.matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        Gizmos.color  = new Color(0.3f, 1f, 0.4f, 0.8f);
        Vector3 c = new Vector3(boundsCenter.x, boundsCenter.y, transform.localPosition.z);
        Gizmos.DrawWireCube(c, new Vector3(bounds.x * 2f, bounds.y * 2f, 0.01f));
    }
}
