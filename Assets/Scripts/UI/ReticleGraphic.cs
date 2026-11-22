using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the reticle procedurally (no sprites) from a ReticleConfig:
/// cross, ring of arcs, center dot, outline and hitmarker.
/// Driven by ReticleUI; you don't need to touch it directly.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class ReticleGraphic : MaskableGraphic
{
    ReticleConfig _cfg = new ReticleConfig();
    float _gap;
    Color _tint = Color.white;
    float _hit;        // 0-1, hitmarker intensity
    bool  _hitLethal;

    /// <summary>Updates the parameters and only redraws if something changed.</summary>
    public void Set(ReticleConfig cfg, float gap, Color tint, float hit, bool hitLethal)
    {
#if !UNITY_EDITOR   // in the editor it always redraws, so Inspector tweaks show up instantly
        if (cfg == _cfg && Mathf.Abs(gap - _gap) < 0.05f && tint == _tint &&
            Mathf.Abs(hit - _hit) < 0.01f && hitLethal == _hitLethal)
            return;
#endif
        _cfg = cfg ?? new ReticleConfig();
        _gap = gap;
        _tint = tint;
        _hit = hit;
        _hitLethal = hitLethal;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_cfg == null) return;

        if (_cfg.outline && _cfg.outlineThickness > 0f)
        {
            Color c = _cfg.outlineColor;
            c.a *= _tint.a;
            Draw(vh, c, _cfg.outlineThickness);
        }
        Draw(vh, _tint, 0f);
    }

    void Draw(VertexHelper vh, Color c, float e)
    {
        float w = _cfg.thickness + e * 2f;
        float gap = _gap;

        if (_cfg.centerDot && _cfg.dotSize > 0f)
            Square(vh, Vector2.zero, _cfg.dotSize * 0.5f + e, c);

        if (_cfg.style == ReticleStyle.Cross)
        {
            Vector2[] dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            foreach (var d in dirs)
                Line(vh, d * (gap - e), d * (gap + _cfg.lineLength + e), w, c);
        }
        else
        {
            int n = Mathf.Max(1, _cfg.arcCount);
            float step = 360f / n;
            float half = step * _cfg.arcFill * 0.5f;
            float extraAngle = gap > 0f ? e / gap * Mathf.Rad2Deg : 0f;
            for (int k = 0; k < n; k++)
            {
                float center = 90f + _cfg.arcRotation + step * k;
                Arc(vh, gap, w, center - half - extraAngle, center + half + extraAngle, c);
            }
        }

        // Hitmarker: 4 diagonal strokes; bigger and red if lethal
        if (_hit > 0f)
        {
            Color hc = _hitLethal ? new Color(1f, 0.25f, 0.2f, c.a) : new Color(1f, 1f, 1f, c.a);
            if (e > 0f) hc = c;
            hc.a *= _hit;

            float r0 = Mathf.Max(6f, gap * 0.35f) + (1f - _hit) * 4f;
            float r1 = r0 + (_hitLethal ? 11f : 8f);
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + 90f * k) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Line(vh, d * (r0 - e), d * (r1 + e), w, hc);
            }
        }
    }

    // ─── Primitives ──────────────────────────────────────────────────────────
    static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero);
        vh.AddVert(b, col, Vector2.zero);
        vh.AddVert(c, col, Vector2.zero);
        vh.AddVert(d, col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }

    static void Square(VertexHelper vh, Vector2 center, float r, Color c) =>
        Quad(vh, center + new Vector2(-r, -r), center + new Vector2(-r, r),
                 center + new Vector2(r, r),   center + new Vector2(r, -r), c);

    static void Line(VertexHelper vh, Vector2 p0, Vector2 p1, float width, Color c)
    {
        Vector2 d = p1 - p0;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
        Quad(vh, p0 - n, p0 + n, p1 + n, p1 - n, c);
    }

    static void Arc(VertexHelper vh, float radius, float width, float from, float to, Color c)
    {
        int segs = Mathf.Clamp(Mathf.CeilToInt((to - from) / 6f), 2, 64);
        float rIn = Mathf.Max(0f, radius - width * 0.5f), rOut = radius + width * 0.5f;
        for (int s = 0; s < segs; s++)
        {
            float a0 = Mathf.Lerp(from, to, (float)s / segs) * Mathf.Deg2Rad;
            float a1 = Mathf.Lerp(from, to, (float)(s + 1) / segs) * Mathf.Deg2Rad;
            Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            Quad(vh, d0 * rIn, d0 * rOut, d1 * rOut, d1 * rIn, c);
        }
    }
}
