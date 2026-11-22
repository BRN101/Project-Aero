using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player aiming, works with mouse and gamepad:
///   • Mouse: the reticle follows the pointer.
///   • Gamepad: the right stick moves a virtual reticle across the screen.
/// Switches mode automatically based on the last device used.
///
/// From the screen position it casts a ray from the camera into the scene and
/// computes the world point the weapon fires at (aiming into depth).
///
/// AIM ASSIST:
///   • Magnet: when the reticle passes near an enemy (assistRadius), it marks it as the target.
///   • Friction (gamepad only): the reticle slows down over an enemy so you don't overshoot.
///   • Measures the target's velocity so the weapon can predict its movement.
///
/// REQUIRED ACTIONS (on the PlayerInput):
///   AimPointer (Vector2, &lt;Mouse&gt;/position) and AimStick (Vector2, &lt;Gamepad&gt;/rightStick)
/// </summary>
public class PlayerAim : MonoBehaviour
{
    [Tooltip("Camera used to aim. Empty = Camera.main.")]
    public Camera aimCamera;

    [Header("Actions")]
    public string pointerAction = "AimPointer";
    public string stickAction   = "AimStick";

    [Header("Gamepad reticle")]
    [Tooltip("Reticle speed with the stick (screen heights per second).")]
    public float stickSpeed = 1.3f;
    [Tooltip("Initial reticle position (0-1 on screen).")]
    public Vector2 startPosition = new Vector2(0.5f, 0.55f);
    [Range(0f, 0.3f)] public float screenMargin = 0.03f;

    [Header("Aim assist")]
    [Tooltip("Magnet radius as a fraction of screen height (0 = disabled).")]
    [Range(0f, 0.3f)] public float assistRadius = 0.05f;
    [Tooltip("Radius multiplier when using a gamepad (mouse uses 1).")]
    [Min(0f)] public float gamepadAssistMultiplier = 1.6f;
    [Tooltip("Stick speed while over an enemy (1 = no friction).")]
    [Range(0.1f, 1f)] public float gamepadFriction = 0.45f;

    /// <summary>Set by the Player from the equipped weapon (Weapon.aimAssist).</summary>
    [HideInInspector] public float weaponAssistMultiplier = 1f;

    [Header("Ray")]
    [Tooltip("Max distance of the reticle ray.")]
    public float maxDistance = 200f;
    [Tooltip("Distance of the aim point when the ray hits nothing.")]
    public float defaultDistance = 80f;
    public LayerMask layerMask = ~0;

    [Header("Cursor")]
    [Tooltip("Hides the OS cursor while playing (use ReticleUI instead).")]
    public bool hideCursor = true;

    /// <summary>Reticle position in screen pixels.</summary>
    public Vector2 ScreenPosition { get; private set; }
    /// <summary>World point the reticle aims at.</summary>
    public Vector3 WorldPoint     { get; private set; }
    /// <summary>Enemy under the reticle (null if none).</summary>
    public Health  Target         { get; private set; }
    public bool    UsingPointer   { get; private set; } = true;
    /// <summary>Estimated target velocity in world space.</summary>
    public Vector3 TargetVelocity { get; private set; }

    /// <summary>Aim data ready to pass to the weapon.</summary>
    public AimData Data => new AimData { point = WorldPoint, target = Target, targetVelocity = TargetVelocity };

    static readonly RaycastHit[] _hits = new RaycastHit[16];

    InputAction _pointer;
    InputAction _stick;
    Vector2     _lastPointer;
    Health      _self;
    Health      _previousTarget;
    Vector3     _previousTargetPos;

    void Start()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        _self = GetComponentInParent<Health>();

        var input = GetComponent<PlayerInput>();
        if (input != null && input.actions != null)
        {
            _pointer = input.actions.FindAction(pointerAction, throwIfNotFound: false);
            _stick   = input.actions.FindAction(stickAction,   throwIfNotFound: false);
        }
        if (_pointer == null && _stick == null)
            Debug.LogWarning($"[PlayerAim] Actions '{pointerAction}' / '{stickAction}' not found.");

        ScreenPosition = new Vector2(Screen.width * startPosition.x, Screen.height * startPosition.y);
        if (_pointer != null) _lastPointer = _pointer.ReadValue<Vector2>();
    }

    void OnEnable()  { if (hideCursor) Cursor.visible = false; }
    void OnDisable() { Cursor.visible = true; }

    void Update()
    {
        UpdateScreenPosition();
        UpdateRay();
    }

    void UpdateScreenPosition()
    {
        Vector2 pos = ScreenPosition;

        if (_pointer != null)
        {
            Vector2 p = _pointer.ReadValue<Vector2>();
            if ((p - _lastPointer).sqrMagnitude > 1f) UsingPointer = true;
            _lastPointer = p;
            if (UsingPointer) pos = p;
        }

        if (_stick != null)
        {
            Vector2 s = _stick.ReadValue<Vector2>();
            if (s.sqrMagnitude > 0.0004f)
            {
                UsingPointer = false;
                float friction = Target != null ? gamepadFriction : 1f;
                pos += s * (stickSpeed * friction * Screen.height * Time.deltaTime);
            }
        }

        float mx = Screen.width * screenMargin, my = Screen.height * screenMargin;
        pos.x = Mathf.Clamp(pos.x, mx, Screen.width  - mx);
        pos.y = Mathf.Clamp(pos.y, my, Screen.height - my);
        ScreenPosition = pos;
    }

    void UpdateRay()
    {
        if (aimCamera == null) return;

        Ray ray = aimCamera.ScreenPointToRay(ScreenPosition);
        int n = Physics.RaycastNonAlloc(ray, _hits, maxDistance, layerMask, QueryTriggerInteraction.Ignore);

        int best = -1;
        for (int i = 0; i < n; i++)
        {
            // Ignore the player itself (and its weapon)
            Transform t = _hits[i].collider.transform;
            if (t.IsChildOf(transform)) continue;
            Health h = _hits[i].collider.GetComponentInParent<Health>();
            if (h != null && h == _self) continue;
            if (best < 0 || _hits[i].distance < _hits[best].distance) best = i;
        }

        Health direct = null;
        if (best >= 0)
        {
            WorldPoint = _hits[best].point;
            Health h = _hits[best].collider.GetComponentInParent<Health>();
            direct = h != null && h.Team == Team.Enemy && h.IsTargetable ? h : null;
        }
        else WorldPoint = ray.GetPoint(defaultDistance);

        Target = direct;
        if (Target == null)
        {
            Health magnet = FindWithMagnet(out Vector3 center);
            if (magnet != null) { Target = magnet; WorldPoint = center; }
        }

        UpdateTargetVelocity();
    }

    /// <summary>Enemy closest to the reticle on screen within the assist radius.</summary>
    Health FindWithMagnet(out Vector3 center)
    {
        center = default;
        float radius = assistRadius * weaponAssistMultiplier * (UsingPointer ? 1f : gamepadAssistMultiplier) * Screen.height;
        if (radius <= 0f) return null;

        Health best = null;
        float bestDist = radius * radius;
        var list = Health.Active;
        for (int i = 0; i < list.Count; i++)
        {
            Health h = list[i];
            if (h.Team != Team.Enemy || !h.IsTargetable) continue;

            Vector3 c = h.Center;
            Vector3 sp = aimCamera.WorldToScreenPoint(c);
            if (sp.z <= 0f || sp.z > maxDistance) continue;   // behind the camera or too far

            float d2 = ((Vector2)sp - ScreenPosition).sqrMagnitude;
            if (d2 < bestDist) { bestDist = d2; best = h; center = c; }
        }
        return best;
    }

    void UpdateTargetVelocity()
    {
        float dt = Time.deltaTime;
        if (Target == null || dt <= 0f)
        {
            TargetVelocity = Vector3.zero;
            _previousTarget = null;
            return;
        }

        Vector3 pos = Target.transform.position;
        if (Target == _previousTarget)
        {
            Vector3 v = (pos - _previousTargetPos) / dt;
            TargetVelocity = Vector3.Lerp(TargetVelocity, v, 1f - Mathf.Exp(-15f * dt));   // smoothed
        }
        else TargetVelocity = RailRig.Current != null ? RailRig.Current.Velocity : Vector3.zero;

        _previousTarget = Target;
        _previousTargetPos = pos;
    }
}
