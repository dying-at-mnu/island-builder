using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    public enum BeachSlope { Flat, Linear, SteepUpper, SteepLower, SCurve }

    public class BeachTool : ITool
    {
        public string ToolId      => "beach";
        public float  BrushRadius => 0f;
        public bool   AlwaysUpdate => !HasLasso;

        private readonly ITerrainReader _reader;
        private readonly ITerrainWriter _writer;

        private readonly List<Vector3> _polygon = new();
        private bool    _drawing;
        private Vector3 _lastAdded;
        private const float MinDist = 3f;

        public BeachSlope Slope { get; set; } = BeachSlope.SCurve;

        public bool HasPolygon => _polygon.Count >= 3;
        public bool HasLasso   => !_drawing && _polygon.Count >= 3;
        public bool IsDrawing  => _drawing;
        public IReadOnlyList<Vector3> Polygon => _polygon;

        public event Action PreviewChanged;
        public event Action Activated;
        public event Action Deactivated;

        public BeachTool(ITerrainReader reader, ITerrainWriter writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public void OnActivate()   { Activated?.Invoke();  PreviewChanged?.Invoke(); }
        public void OnDeactivate() { _drawing = false;     Deactivated?.Invoke(); }

        public void OnMouseDown(RaycastHit hit)
        {
            if (HasLasso) return;
            _polygon.Clear();
            _drawing   = true;
            _lastAdded = hit.point;
            _polygon.Add(hit.point);
            PreviewChanged?.Invoke();
        }

        public void OnMouseHeld(RaycastHit hit)
        {
            if (!_drawing) return;
            if (Vector3.Distance(hit.point, _lastAdded) >= MinDist)
            {
                _polygon.Add(hit.point);
                _lastAdded = hit.point;
                PreviewChanged?.Invoke();
            }
        }

        public void OnMouseUp()
        {
            _drawing = false;
            PreviewChanged?.Invoke();
        }

        public void NotifySettingsChanged() => PreviewChanged?.Invoke();

        public void ClearLasso()
        {
            _polygon.Clear();
            _drawing = false;
            PreviewChanged?.Invoke();
        }

        public void CreateBeach()
        {
            if (!HasLasso) return;
            var cells = ComputeBeachCells(step: 1);
            if (cells.Count == 0) return;

            int res    = _reader.Resolution;
            float[,] h = _reader.GetHeights(new RectInt(0, 0, res, res));

            foreach (var (hx, hz, targetNorm, _, cellCrestH) in cells)
            {
                // Reshape cells within the beach face (at or below the crest).
                // Cells above the crest are inland terrain — leave them alone.
                if (h[hz, hx] <= cellCrestH)
                    h[hz, hx] = targetNorm;
            }

            _writer.SetHeights(h, new Vector2Int(0, 0));
        }

        // ── Core algorithm ────────────────────────────────────────────────────

        /// <summary>
        /// Returns (hx, hz, targetNorm, t, crestH) for every cell inside the lasso.
        ///
        /// For each polygon edge, ray-cast perpendicular samples inward and find the
        /// first local maximum (the natural beach crest) for each sample point.
        /// Each interior cell finds its nearest polygon edge, linearly interpolates
        /// (crestH, dCrest) from that edge's samples, then:
        ///   t = d_to_edge / dCrest   →  0 at the water boundary, 1 at the crest
        ///   target = seaNorm + profile(t) × (crestH − seaNorm)
        ///
        /// Using the cell's nearest edge (not nearest sample point) eliminates Voronoi
        /// discontinuities; linear interpolation along the edge keeps crestH and dCrest
        /// smooth. In CreateBeach we SET h = target for cells ≤ crestH so the slope is
        /// truly continuous with no "max()" steps.
        /// </summary>
        public List<(int hx, int hz, float targetNorm, float t, float cellCrestH)>
            ComputeBeachCells(int step = 1)
        {
            var result = new List<(int, int, float, float, float)>(4096);
            if (_polygon.Count < 3) return result;

            int   res    = _reader.Resolution;
            float cw     = _reader.CellWidth, cl = _reader.CellLength;
            float worldY = _reader.WorldSize.y;
            if (worldY <= 0f || res <= 1) return result;

            float seaNorm  = _reader.SeaLevelOffset / worldY;
            float[,] hmap  = _reader.GetHeights(new RectInt(0, 0, res, res));

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in _polygon)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            int ix0 = Mathf.Clamp(Mathf.FloorToInt(minX / cw), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt (maxX / cw), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt(minZ / cl), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt (maxZ / cl), 0, res - 1);

            int polyN = _polygon.Count;

            // ── Pass 1: per-edge boundary samples via perpendicular ray-casts ──
            // Each sample stores (tEdge, crestH, dCrest) where tEdge ∈ [0,1] is
            // the normalised parameter along the edge (0 = vertex A, 1 = vertex B).
            float sampleInterval = step * Mathf.Max(cw, cl);
            float rayStep        = Mathf.Max(cw, cl);
            float maxRayDist     = Mathf.Max(maxX - minX, maxZ - minZ) * 0.75f;

            // edgeSamples[i] = sorted list of (tEdge, crestH, dCrest) for edge i
            var edgeSamples = new List<(float tE, float crestH, float dCrest)>[polyN];
            for (int i = 0; i < polyN; i++)
                edgeSamples[i] = new List<(float, float, float)>();

            for (int ei = 0; ei < polyN; ei++)
            {
                Vector3 A = _polygon[ei], B = _polygon[(ei + 1) % polyN];
                float ex = B.x - A.x, ez = B.z - A.z;
                float edgeLen = Mathf.Sqrt(ex * ex + ez * ez);
                if (edgeLen < 0.001f) continue;

                // Inward-pointing perpendicular normal.
                Vector2 eDir = new Vector2(ex, ez).normalized;
                Vector2 nrm  = new Vector2(-eDir.y, eDir.x);
                Vector2 mid  = new Vector2((A.x + B.x) * .5f, (A.z + B.z) * .5f);
                if (!IsInsidePoly(mid.x + nrm.x * .5f, mid.y + nrm.y * .5f))
                    nrm = -nrm;

                int numSamples = Mathf.Max(1, Mathf.CeilToInt(edgeLen / sampleInterval));
                for (int s = 0; s <= numSamples; s++)
                {
                    float tE  = (float)s / numSamples;
                    var   pos = new Vector2(A.x + ex * tE, A.z + ez * tE);

                    // March inward to find the first local maximum (beach crest).
                    float crestH    = seaNorm;
                    float crestDist = rayStep;
                    float prevH     = seaNorm;
                    bool  climbing  = false;

                    for (float d = rayStep; d <= maxRayDist; d += rayStep)
                    {
                        float wx = pos.x + nrm.x * d, wz = pos.y + nrm.y * d;
                        if (!IsInsidePoly(wx, wz)) break;

                        int sx = Mathf.Clamp(Mathf.FloorToInt(wx / cw), 0, res - 1);
                        int sz = Mathf.Clamp(Mathf.FloorToInt(wz / cl), 0, res - 1);
                        float h = hmap[sz, sx];

                        if (h > crestH) { crestH = h; crestDist = d; climbing = true; }
                        else if (climbing && h < prevH - 1e-4f) break; // slope reversed
                        prevH = h;
                    }

                    edgeSamples[ei].Add((tE, crestH, Mathf.Max(crestDist, rayStep)));
                }
            }

            // ── Pass 2: collect cells ─────────────────────────────────────────
            var allCells = new List<(int hx, int hz, float wx, float wz)>();
            for (int hz = iz0; hz <= iz1; hz += step)
            for (int hx = ix0; hx <= ix1; hx += step)
            {
                float wx = (hx + 0.5f) * cw, wz = (hz + 0.5f) * cl;
                if (IsInsidePoly(wx, wz))
                    allCells.Add((hx, hz, wx, wz));
            }

            // ── Pass 3: target height per cell ────────────────────────────────
            foreach (var (hx, hz, wx, wz) in allCells)
            {
                // Find the nearest polygon edge and the parameter t along it.
                float nearestDist = float.MaxValue;
                int   nearestEdge = 0;
                float nearestTE   = 0f;

                for (int ei = 0; ei < polyN; ei++)
                {
                    Vector3 A = _polygon[ei], B = _polygon[(ei + 1) % polyN];
                    float dx = B.x - A.x, dz = B.z - A.z;
                    float len2 = dx * dx + dz * dz;
                    float tE = len2 > 1e-9f
                        ? Mathf.Clamp01(((wx - A.x) * dx + (wz - A.z) * dz) / len2)
                        : 0f;
                    float qx = A.x + tE * dx - wx, qz = A.z + tE * dz - wz;
                    float d  = qx * qx + qz * qz;
                    if (d < nearestDist) { nearestDist = d; nearestEdge = ei; nearestTE = tE; }
                }

                float dBoundary = Mathf.Sqrt(nearestDist); // true geometric dist to edge

                // Interpolate (crestH, dCrest) linearly along the nearest edge.
                var   es     = edgeSamples[nearestEdge];
                float crestH = seaNorm, dCrest = dBoundary + rayStep;

                if (es.Count == 1)
                {
                    crestH = es[0].crestH;
                    dCrest = es[0].dCrest;
                }
                else if (es.Count > 1)
                {
                    // Find the two samples that bracket nearestTE.
                    int lo = 0;
                    for (int k = 0; k < es.Count - 1; k++)
                        if (es[k].tE <= nearestTE) lo = k;

                    int hi = Mathf.Min(lo + 1, es.Count - 1);
                    if (lo == hi)
                    {
                        crestH = es[lo].crestH;
                        dCrest = es[lo].dCrest;
                    }
                    else
                    {
                        float span  = es[hi].tE - es[lo].tE;
                        float blend = span > 1e-6f ? (nearestTE - es[lo].tE) / span : 0f;
                        crestH = Mathf.Lerp(es[lo].crestH, es[hi].crestH, blend);
                        dCrest = Mathf.Lerp(es[lo].dCrest, es[hi].dCrest, blend);
                    }
                }

                // t = 0 at the boundary (water edge), 1 at the crest.
                float t              = Mathf.Clamp01(dBoundary / Mathf.Max(dCrest, 1e-4f));
                float heightAboveSea = Mathf.Max(0f, crestH - seaNorm);
                float targetNorm     = seaNorm + ApplySlope(t, Slope) * heightAboveSea;

                result.Add((hx, hz, targetNorm, t, crestH));
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public bool IsInsidePoly(float wx, float wz)
        {
            int n = _polygon.Count;
            if (n < 3) return false;
            bool inside = false;
            int j = n - 1;
            for (int i = 0; i < n; i++)
            {
                float xi = _polygon[i].x, zi = _polygon[i].z;
                float xj = _polygon[j].x, zj = _polygon[j].z;
                if (((zi > wz) != (zj > wz)) &&
                    (wx < (xj - xi) * (wz - zi) / (zj - zi) + xi))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        // t = 0 at water edge (boundary), t = 1 at crest (full height).
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
