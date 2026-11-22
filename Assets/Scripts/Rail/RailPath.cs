using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level path defined by its children: each child is a node, in hierarchy order.
/// To edit it, move, duplicate or reorder the children in the scene.
///
/// The path is built in Awake and treated as static during play.
/// If you move nodes at runtime, call Rebuild().
/// </summary>
public class RailPath : MonoBehaviour
{
    [Tooltip("Connects the last node back to the first (circuit).")]
    public bool closed = false;

    [Tooltip("Smooth curve (Catmull-Rom) instead of straight segments.")]
    public bool smooth = true;

    [SerializeField] Color gizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    CurvePath _curve;

    public CurvePath Curve
    {
        get
        {
            if (_curve == null) Rebuild();
            return _curve;
        }
    }

    public float Length => Curve.Length;

    void Awake() => Rebuild();
    void OnValidate() => _curve = null;

    public void Rebuild() => _curve = new CurvePath(GetNodes(), closed, smooth);

    public Vector3 PointAt(float distance)   => Curve.Evaluate(distance);
    public Vector3 TangentAt(float distance) => Curve.Tangent(distance);

    List<Vector3> GetNodes()
    {
        var nodes = new List<Vector3>(transform.childCount);
        foreach (Transform child in transform) nodes.Add(child.position);
        return nodes;
    }

    void OnDrawGizmos()
    {
        // In the editor it is rebuilt on every repaint so changes show up immediately
        var curve = Application.isPlaying ? Curve : new CurvePath(GetNodes(), closed, smooth);
        var s = curve.Samples;

        Gizmos.color = gizmoColor;
        for (int i = 1; i < s.Count; i++) Gizmos.DrawLine(s[i - 1], s[i]);
        foreach (Transform child in transform) Gizmos.DrawWireSphere(child.position, 0.6f);

        if (transform.childCount > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.GetChild(0).position, 0.8f);
        }
    }
}
