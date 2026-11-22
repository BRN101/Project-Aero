using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Curve sampled by distance (not by parameter t), so traversing it at a constant
/// speed produces uniform motion.
///
/// Supports straight segments (polyline) or smooth ones (Catmull-Rom), open or closed.
/// Used by RailPath (level path) and Enemy (custom paths).
/// </summary>
public class CurvePath
{
    readonly Vector3[] _samples;
    readonly float[]   _cumulative;
    readonly bool      _closed;

    public float Length { get; }
    public bool  Closed => _closed;

    public CurvePath(IList<Vector3> points, bool closed, bool smooth, int samplesPerSegment = 16)
    {
        int n = points?.Count ?? 0;
        _closed = closed && n > 2;

        if (n < 2)
        {
            _samples    = new[] { n == 1 ? points[0] : Vector3.zero };
            _cumulative = new[] { 0f };
            return;
        }

        int segments = _closed ? n : n - 1;
        int steps    = smooth ? Mathf.Max(2, samplesPerSegment) : 1;
        var list     = new List<Vector3>(segments * steps + 1);

        for (int i = 0; i < segments; i++)
        {
            Vector3 p0 = P(points, i - 1), p1 = P(points, i), p2 = P(points, i + 1), p3 = P(points, i + 2);
            for (int s = 0; s < steps; s++)
            {
                float t = (float)s / steps;
                list.Add(smooth ? CatmullRom(p0, p1, p2, p3, t) : Vector3.Lerp(p1, p2, t));
            }
        }
        list.Add(_closed ? points[0] : points[n - 1]);

        _samples    = list.ToArray();
        _cumulative = new float[_samples.Length];
        for (int i = 1; i < _samples.Length; i++)
            _cumulative[i] = _cumulative[i - 1] + Vector3.Distance(_samples[i - 1], _samples[i]);
        Length = _cumulative[_cumulative.Length - 1];
    }

    Vector3 P(IList<Vector3> pts, int i)
    {
        int n = pts.Count;
        if (_closed) return pts[((i % n) + n) % n];
        return pts[Mathf.Clamp(i, 0, n - 1)];
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    /// <summary>Normalizes a distance: wraps if closed, clamps if open.</summary>
    public float NormalizeDistance(float d) =>
        _closed && Length > 0f ? Mathf.Repeat(d, Length) : Mathf.Clamp(d, 0f, Length);

    /// <summary>Position at a given distance from the start of the curve.</summary>
    public Vector3 Evaluate(float distance)
    {
        if (_samples.Length == 1 || Length <= 0f) return _samples[0];
        float d = NormalizeDistance(distance);

        int lo = 0, hi = _cumulative.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_cumulative[mid] <= d) lo = mid; else hi = mid;
        }
        float span = _cumulative[hi] - _cumulative[lo];
        float t = span > 0f ? (d - _cumulative[lo]) / span : 0f;
        return Vector3.Lerp(_samples[lo], _samples[hi], t);
    }

    /// <summary>Normalized curve direction at a given distance.</summary>
    public Vector3 Tangent(float distance)
    {
        const float h = 0.5f;
        float a = distance - h, b = distance + h;
        if (!_closed) { a = Mathf.Max(0f, a); b = Mathf.Min(Length, b); }
        Vector3 dir = Evaluate(b) - Evaluate(a);
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
    }

    /// <summary>Sampled points (for drawing gizmos).</summary>
    public IReadOnlyList<Vector3> Samples => _samples;
}
