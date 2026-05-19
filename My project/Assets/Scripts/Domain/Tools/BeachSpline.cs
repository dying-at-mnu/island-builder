using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Catmull-Rom spline built from a list of anchor points.
    /// Anchor points are added while the user drags; moving an anchor
    /// updates the smooth curve automatically (no separate tangent handles needed).
    /// </summary>
    public class BeachSpline
    {
        private readonly List<Vector3> _anchors = new();

        public IReadOnlyList<Vector3> Anchors  => _anchors;
        public bool HasCurve => _anchors.Count >= 2;
        public bool HasPoint => _anchors.Count >= 1;

        public event Action Changed;

        // ── Editing ───────────────────────────────────────────────────────────

        public void Clear() { _anchors.Clear(); Changed?.Invoke(); }

        public void AddPoint(Vector3 p, float minDist = 3f)
        {
            if (_anchors.Count == 0 ||
                Vector3.Distance(p, _anchors[_anchors.Count - 1]) >= minDist)
            {
                _anchors.Add(p);
                Changed?.Invoke();
            }
        }

        public void MoveAnchor(int index, Vector3 newPos)
        {
            if (index < 0 || index >= _anchors.Count) return;
            _anchors[index] = newPos;
            Changed?.Invoke();
        }

        // ── Curve evaluation (Catmull-Rom) ────────────────────────────────────

        /// <summary>Evaluate a smooth point at t ∈ [0,1] along the spline.</summary>
        public Vector3 GetPoint(float t)
        {
            int n = _anchors.Count;
            if (n == 0) return Vector3.zero;
            if (n == 1) return _anchors[0];

            float scaled = Mathf.Clamp01(t) * (n - 1);
            int   seg    = Mathf.Min((int)scaled, n - 2);
            float lt     = scaled - seg;

            Vector3 p0 = _anchors[Mathf.Max(seg - 1, 0)];
            Vector3 p1 = _anchors[seg];
            Vector3 p2 = _anchors[Mathf.Min(seg + 1, n - 1)];
            Vector3 p3 = _anchors[Mathf.Min(seg + 2, n - 1)];
            return CatmullRom(p0, p1, p2, p3, lt);
        }

        /// <summary>Evaluate <paramref name="count"/> evenly-spaced world positions.</summary>
        public Vector3[] Evaluate(int count)
        {
            if (!HasCurve) return Array.Empty<Vector3>();
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++)
                pts[i] = GetPoint((float)i / (count - 1));
            return pts;
        }

        // ── Spatial queries ───────────────────────────────────────────────────

        public float DistanceTo(float wx, float wz, int samples = 80)
        {
            if (!HasCurve) return float.MaxValue;
            float min = float.MaxValue;
            for (int i = 0; i < samples; i++)
            {
                var p = GetPoint((float)i / (samples - 1));
                float dx = p.x - wx, dz = p.z - wz;
                float d2 = dx * dx + dz * dz;
                if (d2 < min) min = d2;
            }
            return Mathf.Sqrt(min);
        }

        /// <summary>Nearest world-space position on the curve to (wx, wz).</summary>
        public Vector3 NearestPoint(float wx, float wz, int samples = 80)
        {
            if (!HasCurve) return HasPoint ? _anchors[0] : Vector3.zero;
            float   min  = float.MaxValue;
            Vector3 best = _anchors[0];
            for (int i = 0; i < samples; i++)
            {
                var p = GetPoint((float)i / (samples - 1));
                float dx = p.x - wx, dz = p.z - wz;
                float d2 = dx * dx + dz * dz;
                if (d2 < min) { min = d2; best = p; }
            }
            return best;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1,
                                           Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
