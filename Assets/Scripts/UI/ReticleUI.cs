using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen reticle. Follows PlayerAim and draws the equipped weapon's reticle
/// (each Weapon defines its own in its "reticle" field).
///
///   • The opening reflects the weapon's actual spread (rifle bloom, shotgun cone).
///   • Changes color when an enemy is under the reticle.
///   • Shows a hitmarker on hits (red and bigger if the hit was lethal).
///
/// Put it on an object inside a Canvas. It creates its graphic (ReticleGraphic) automatically.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ReticleUI : MonoBehaviour
{
    [Tooltip("Empty = the current Player's aim.")]
    public PlayerAim aim;
    [Tooltip("Empty = created automatically as a child.")]
    public ReticleGraphic graphic;
    [Tooltip("Reticle used when no weapon is equipped.")]
    public ReticleConfig defaultReticle = new ReticleConfig();
    [Min(0.01f)] public float hitmarkerDuration = 0.2f;

    RectTransform _rt;
    Canvas _canvas;
    float  _gap = -1f;
    Color  _color = Color.white;
    float  _hit;
    bool   _hitLethal;
    Weapon _previousWeapon;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _canvas = GetComponentInParent<Canvas>();

        // Disable legacy graphics on this object (e.g. the Image from earlier versions)
        foreach (var g in GetComponents<Graphic>()) g.enabled = false;

        if (graphic == null)
        {
            var go = new GameObject("Graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(ReticleGraphic));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            graphic = go.GetComponent<ReticleGraphic>();
            graphic.raycastTarget = false;
        }
    }

    void OnEnable()  => Bullet.Hit += OnBulletHit;
    void OnDisable() => Bullet.Hit -= OnBulletHit;

    void LateUpdate()
    {
        if (aim == null && Player.Current != null) aim = Player.Current.Aim;
        bool visible = aim != null && aim.isActiveAndEnabled;
        graphic.enabled = visible;
        if (!visible) return;

        // Position
        Vector2 screen = aim.ScreenPosition;
        if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            _rt.position = screen;
        else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                     (RectTransform)_rt.parent, screen, _canvas.worldCamera, out Vector2 local))
            _rt.localPosition = local;

        // Weapon and settings
        Weapon weapon = Player.Current != null ? Player.Current.CurrentWeapon : null;
        ReticleConfig cfg = weapon != null && weapon.reticle != null ? weapon.reticle : defaultReticle;
        bool weaponChanged = weapon != _previousWeapon;
        _previousWeapon = weapon;

        // Opening from the actual spread: convert the cone's half-angle into pixels
        float target = cfg.minGap;
        Camera cam = aim.aimCamera != null ? aim.aimCamera : Camera.main;
        if (weapon != null && cam != null)
        {
            float px = Mathf.Tan(weapon.CurrentCone * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                       * Screen.height * 0.5f;
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            target = Mathf.Max(cfg.minGap, px / Mathf.Max(0.01f, scale));
        }

        float dt = Time.unscaledDeltaTime;
        float k = cfg.smoothing > 0f ? 1f - Mathf.Exp(-cfg.smoothing * dt) : 1f;
        _gap = _gap < 0f || weaponChanged ? target : Mathf.Lerp(_gap, target, k);
        _color = Color.Lerp(_color, aim.Target != null ? cfg.targetColor : cfg.color, k);
        _hit = Mathf.Max(0f, _hit - dt / hitmarkerDuration);

        graphic.Set(cfg, _gap, _color, _hit, _hitLethal);
    }

    void OnBulletHit(Bullet bullet, Health target)
    {
        if (bullet.Team != Team.Player || target.Team == Team.Player) return;
        _hit = 1f;
        _hitLethal = !target.IsAlive;
    }
}
