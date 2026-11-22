using UnityEngine;

/// <summary>
/// Camera placed behind the player, fixed laterally and vertically relative to the rig.
/// It does not follow the player on X/Y: the player moves within the frame.
///
/// Works whether or not the camera is a child of the rig.
/// Includes optional shake (e.g. when taking damage).
/// </summary>
[RequireComponent(typeof(Camera))]
public class RailCamera : MonoBehaviour
{
    public static RailCamera Current { get; private set; }

    [Tooltip("Rig to follow. If empty, RailRig.Current or the parent is used.")]
    public RailRig rig;

    [Tooltip("Camera position in rig space (behind and slightly above the player).")]
    public Vector3 offset = new Vector3(0f, 1.2f, -10f);

    [Tooltip("Point the camera looks at, in rig space.")]
    public Vector3 lookAt = new Vector3(0f, 0.5f, 20f);

    [Header("Shake")]
    public float shakeIntensity = 0.25f;
    public float shakeDuration  = 0.2f;

    float _shakeRemaining;
    float _shakeAmount;
    float _shakeLength;

    void Awake() => Current = this;

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    void LateUpdate()
    {
        Transform anchor = rig != null ? rig.transform
                         : RailRig.Current != null ? RailRig.Current.transform
                         : transform.parent;
        if (anchor == null) return;

        Vector3 shake = Vector3.zero;
        if (_shakeRemaining > 0f)
        {
            _shakeRemaining -= Time.deltaTime;
            float k = Mathf.Clamp01(_shakeRemaining / Mathf.Max(0.0001f, _shakeLength));
            shake = Random.insideUnitSphere * (_shakeAmount * k);
        }

        Vector3 dir = lookAt - offset;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;

        transform.SetPositionAndRotation(
            anchor.TransformPoint(offset + shake),
            anchor.rotation * Quaternion.LookRotation(dir));
    }

    /// <summary>Shakes the camera. Negative values use the Inspector settings.</summary>
    public void Shake(float intensity = -1f, float duration = -1f)
    {
        _shakeAmount    = intensity >= 0f ? intensity : shakeIntensity;
        _shakeLength    = duration  >= 0f ? duration  : shakeDuration;
        _shakeRemaining = _shakeLength;
    }
}
