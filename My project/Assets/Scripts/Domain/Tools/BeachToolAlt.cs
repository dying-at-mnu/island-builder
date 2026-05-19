using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    public enum BeachAltPhase
    {
        DrawingInner,   // user is drawing the crest line
        InnerReady,     // crest line done — camera moves freely, handles editable
        DrawingOuter,   // user is drawing the water-boundary line
        BothReady,      // both lines done — handles editable on both, can apply
    }

    /// <summary>
    /// Alt beach tool: the user draws two Catmull-Rom curves.
    ///   Inner curve = beach crest (highest point, where sand starts)
    ///   Outer curve = water boundary (sea level, where sand ends)
    /// Both curves show draggable anchor handles.
    /// The beach slope is computed between the two curves.
    /// </summary>
    public class BeachToolAlt : ITool
    {
        public string ToolId      => "beachalt";
        public float  BrushRadius => 0f;
        // AlwaysUpdate for all phases except BothReady (where camera should move freely).
        public bool   AlwaysUpdate => _phase != BeachAltPhase.BothReady;

        private readonly ITerrainReader _reader;
        private readonly ITerrainWriter _writer;

        private bool    _drawing;
        private Vector3 _lastAdded;
        private const float MinDist = 3f;

        public BeachAltPhase Phase     => _phase;
        private BeachAltPhase _phase   = BeachAltPhase.DrawingInner;

        public BeachSpline InnerCurve  { get; } = new();
        public BeachSpline OuterCurve  { get; } = new();
        public BeachSlope  Slope       { get; set; } = BeachSlope.SCurve;

        public event Action PreviewChanged;
        public event Action Activated;
        public event Action Deactivated;

        public BeachToolAlt(ITerrainReader reader, ITerrainWriter writer)
        {
            _reader = reader;
            _writer = writer;
            InnerCurve.Changed += () => PreviewChanged?.Invoke();
            OuterCurve.Changed += () => PreviewChanged?.Invoke();
        }

        public void OnActivate()   { Activated?.Invoke();  PreviewChanged?.Invoke(); }
        public void OnDeactivate() { _drawing = false;     Deactivated?.Invoke(); }

        // ── Mouse handlers ────────────────────────────────────────────────────

        public void OnMouseDown(RaycastHit hit)
        {
            // Dragging while InnerReady automatically starts the outer curve.
            if (_phase == BeachAltPhase.InnerReady)
                _phase = BeachAltPhase.DrawingOuter;

            if (_phase != BeachAltPhase.DrawingInner &&
                _phase != BeachAltPhase.DrawingOuter) return;

            _drawing   = true;
            _lastAdded = hit.point;
            ActiveCurve.Clear();
            ActiveCurve.AddPoint(hit.point, 0f);
        }

        public void OnMouseHeld(RaycastHit hit)
        {
            if (!_drawing) return;
            ActiveCurve.AddPoint(hit.point, MinDist);
        }

        public void OnMouseUp()
        {
            if (!_drawing) return;
            _drawing = false;

            if (_phase == BeachAltPhase.DrawingInner && InnerCurve.HasCurve)
                _phase = BeachAltPhase.InnerReady;
            else if (_phase == BeachAltPhase.DrawingOuter && OuterCurve.HasCurve)
                _phase = BeachAltPhase.BothReady;

            PreviewChanged?.Invoke();
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        public void BeginDrawOuter()
        {
            if (_phase != BeachAltPhase.InnerReady) return;
            _phase = BeachAltPhase.DrawingOuter;
            PreviewChanged?.Invoke();
        }

        public void ClearAll()
        {
            _drawing = false;
            _phase   = BeachAltPhase.DrawingInner;
            InnerCurve.Clear();
            OuterCurve.Clear();
            PreviewChanged?.Invoke();
        }

        public void ClearOuter()
        {
            OuterCurve.Clear();
            _phase = BeachAltPhase.InnerReady;
            PreviewChanged?.Invoke();
        }

        public void NotifySettingsChanged() => PreviewChanged?.Invoke();

        // ── Beach creation ────────────────────────────────────────────────────

        public void CreateBeach()
        {
            if (_phase != BeachAltPhase.BothReady) return;
            var cells = ComputeBeachCells(step: 1);
            if (cells.Count == 0) return;

            int res    = _reader.Resolution;
            float[,] h = _reader.GetHeights(new RectInt(0, 0, res, res));

            foreach (var (hx, hz, targetNorm, _, _) in cells)
                if (h[hz, hx] < targetNorm)
                    h[hz, hx] = targetNorm;

            _writer.SetHeights(h, new Vector2Int(0, 0));
        }

        // ── Core: compute target heights between the two curves ───────────────

        public List<(int hx, int hz, float targetNorm, float t, float crestH)>
            ComputeBeachCells(int step = 1)
        {
            var result = new List<(int, int, float, float, float)>(4096);
            if (!InnerCurve.HasCurve || !OuterCurve.HasCurve) return result;

            int   res    = _reader.Resolution;
            float cw     = _reader.CellWidth, cl = _reader.CellLength;
            float worldY = _reader.WorldSize.y;
            if (worldY <= 0f || res <= 1) return result;

            float seaNorm = _reader.SeaLevelOffset / worldY;
            float[,] hmap = _reader.GetHeights(new RectInt(0, 0, res, res));

            // Bounding box of both curves combined.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var curve in new[] { InnerCurve, OuterCurve })
            foreach (var p in curve.Anchors)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            int ix0 = Mathf.Clamp(Mathf.FloorToInt(minX / cw), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt (maxX / cw), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt(minZ / cl), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt (maxZ / cl), 0, res - 1);

            // Pre-evaluate both curves at decent resolution.
            const int EvalSamples = 120;
            var innerPts = InnerCurve.Evaluate(EvalSamples);
            var outerPts = OuterCurve.Evaluate(EvalSamples);

            for (int hz = iz0; hz <= iz1; hz += step)
            for (int hx = ix0; hx <= ix1; hx += step)
            {
                float wx = (hx + 0.5f) * cw, wz = (hz + 0.5f) * cl;

                float dInner = MinDist2D(wx, wz, innerPts, out Vector3 nearInner);
                float dOuter = MinDist2D(wx, wz, outerPts, out Vector3 nearOuter);

                // Corridor test: the cell is "between" the two curves only when its
                // nearest inner point and nearest outer point are on opposite sides of it
                // (the two direction vectors have a negative dot product).
                float diX = nearInner.x - wx, diZ = nearInner.z - wz;
                float doX = nearOuter.x - wx, doZ = nearOuter.z - wz;
                if (diX * doX + diZ * doZ > 0f) continue; // same side → outside corridor

                float total = dInner + dOuter;
                if (total < 0.001f) continue;

                // t = 0 at the outer (water) boundary, t = 1 at the inner (crest).
                float t = Mathf.Clamp01(dOuter / total);

                // Height reference: terrain at the nearest inner-curve point.
                int nx = Mathf.Clamp(Mathf.FloorToInt(nearInner.x / cw), 0, res - 1);
                int nz = Mathf.Clamp(Mathf.FloorToInt(nearInner.z / cl), 0, res - 1);
                float innerH = hmap[nz, nx];

                float heightAboveSea = Mathf.Max(0f, innerH - seaNorm);
                float targetNorm     = seaNorm + ApplySlope(t, Slope) * heightAboveSea;

                result.Add((hx, hz, targetNorm, t, innerH));
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private BeachSpline ActiveCurve =>
            _phase == BeachAltPhase.DrawingInner ? InnerCurve : OuterCurve;

        private static float MinDist2D(float wx, float wz, Vector3[] pts,
                                        out Vector3 nearest)
        {
            float min = float.MaxValue;
            nearest   = pts.Length > 0 ? pts[0] : Vector3.zero;
            foreach (var p in pts)
            {
                float dx = p.x - wx, dz = p.z - wz;
                float d2 = dx * dx + dz * dz;
                if (d2 < min) { min = d2; nearest = p; }
            }
            return Mathf.Sqrt(min);
        }

        private static float ApplySlope(float t, BeachSlope slope) => slope switch
        {
            BeachSlope.Flat       => 1f,
            BeachSlope.Linear     => t,
            BeachSlope.SteepUpper => t * t,
            BeachSlope.SteepLower => Mathf.Sqrt(t),
            BeachSlope.SCurve     => t * t * (3f - 2f * t),
            _                     => t,
        };

        public VisualElement GetParameterPanel() => null;
    }
}
