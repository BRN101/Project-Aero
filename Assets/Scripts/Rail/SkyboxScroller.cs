using UnityEngine;

/// <summary>
/// Slowly rotates the skybox to reinforce the feeling of travel.
///
/// Note: the skybox is "at infinity", so on its own it conveys rotation, not forward motion.
/// Forward motion comes from the RailRig actually moving or from parallax elements
/// (clouds, particles, scenery). Use this as a complement.
///
/// Requires a skybox material with a "_Rotation" property (Cubemap, 6 Sided or Panoramic).
/// Works on a copy of the material so the original asset is never modified.
/// </summary>
public class SkyboxScroller : MonoBehaviour
{
    [Tooltip("Degrees per second at the reference speed.")]
    public float degreesPerSecond = 1.5f;

    [Tooltip("Scales rotation with the RailRig's current speed (stops when the rig stops).")]
    public bool scaleWithRailSpeed = true;

    [Min(0.01f)] public float referenceSpeed = 12f;

    public string property = "_Rotation";

    Material _original;
    Material _copy;
    float    _rotation;

    void Start()
    {
        _original = RenderSettings.skybox;
        if (_original == null || !_original.HasProperty(property))
        {
            Debug.LogWarning($"[SkyboxScroller] The skybox has no '{property}' property.");
            enabled = false;
            return;
        }
        _copy = new Material(_original);
        RenderSettings.skybox = _copy;
        _rotation = _copy.GetFloat(property);
    }

    void Update()
    {
        float factor = 1f;
        if (scaleWithRailSpeed && RailRig.Current != null)
            factor = RailRig.Current.CurrentSpeed / referenceSpeed;

        _rotation = Mathf.Repeat(_rotation + degreesPerSecond * factor * Time.deltaTime, 360f);
        _copy.SetFloat(property, _rotation);
    }

    void OnDestroy()
    {
        if (_copy == null) return;
        if (RenderSettings.skybox == _copy) RenderSettings.skybox = _original;
        Destroy(_copy);
    }
}
