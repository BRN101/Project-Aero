using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health bar bound to a Health component.
///   • Player:     binds to the current Player
///   • ActiveBoss: binds to Boss.Current and hides when there is no boss
///   • Manual:     uses the assigned Health
///
/// "fill" must be a Filled Image. Requires a CanvasGroup to hide itself.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class HealthBarUI : MonoBehaviour
{
    public enum Source { Player, ActiveBoss, Manual }

    public Source source = Source.Player;
    public Health manual;
    public Image fill;
    [Tooltip("Optional label (e.g. the boss name).")]
    public Text label;
    public float smoothing = 10f;

    CanvasGroup _group;
    float _shown = 1f;

    void Awake() => _group = GetComponent<CanvasGroup>();

    void LateUpdate()
    {
        Health h = Resolve();
        bool visible = h != null && h.IsAlive;
        _group.alpha = Mathf.MoveTowards(_group.alpha, visible ? 1f : 0f, Time.deltaTime * 4f);
        if (h == null) return;

        _shown = Mathf.Lerp(_shown, h.Normalized, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        if (fill != null) fill.fillAmount = _shown;

        if (label != null && source == Source.ActiveBoss && Boss.Current != null)
            label.text = Boss.Current.bossName;
    }

    Health Resolve()
    {
        switch (source)
        {
            case Source.Player:     return Player.Current != null ? Player.Current.Health : null;
            case Source.ActiveBoss: return Boss.Current   != null ? Boss.Current.BossHealth : null;
            default:                return manual;
        }
    }
}
