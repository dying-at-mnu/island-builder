using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain.Tools;

namespace IslandBuilder.Rendering
{
    [AddComponentMenu("Island Builder/Lasso Renderer")]
    public class LassoRenderer : MonoBehaviour
    {
        private LassoTool    _lasso;
        private LineRenderer _ring;
        private Material     _mat;

        private readonly List<LineRenderer> _handles      = new();
        private readonly List<int>          _handleIndices = new(); // polygon index per handle
        private bool  _showHandles;

        private const float YOff        = 1f;
        private const float HandleRadius = 3f;
        private const int   HandleSegs   = 10;

        /// <summary>Minimum world-space distance between consecutive handles (metres).</summary>
        public float HandleSpacingMetres { get; set; } = 10f;

        public void Bind(LassoTool lasso)
        {
            _lasso = lasso;
            lasso.SelectionChanged += OnSelectionChanged;

            _mat  = BuildMaterial();
            _ring = gameObject.AddComponent<LineRenderer>();
            _ring.useWorldSpace     = true;
            _ring.loop              = true;
            _ring.widthMultiplier   = 3f;
            _ring.shadowCastingMode = ShadowCastingMode.Off;
            _ring.receiveShadows    = false;

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.20f, 1.00f, 0.30f), 0f),
                        new GradientColorKey(new Color(0.10f, 0.80f, 0.20f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            _ring.colorGradient  = grad;
            _ring.sharedMaterial = _mat;
            _ring.enabled        = false;
        }

        public void SetShowHandles(bool show) => _showHandles = show;

        /// <summary>Returns the polygon index that corresponds to visible handle <paramref name="handleIndex"/>.</summary>
        public int GetPolygonIndex(int handleIndex) =>
            handleIndex >= 0 && handleIndex < _handleIndices.Count
                ? _handleIndices[handleIndex] : -1;

        public int HandleCount => _handleIndices.Count;

        private void Update()
        {
            if (_lasso == null) return;
            var poly = _lasso.Polygon;

            // ── Sync ring positions every frame so handle moves reflect immediately ──
            if (poly.Count >= 2)
            {
                _ring.enabled = true;
                if (_ring.positionCount != poly.Count)
                    _ring.positionCount = poly.Count;
                for (int i = 0; i < poly.Count; i++)
                    _ring.SetPosition(i, SurfacePoint(poly[i]));
            }
            else
            {
                _ring.enabled = false;
            }

            // ── Recompute subsampled handle indices ───────────────────────────
            _handleIndices.Clear();
            if (poly.Count > 0)
            {
                _handleIndices.Add(0);
                Vector3 last = poly[0];
                float sp2 = HandleSpacingMetres * HandleSpacingMetres;
                for (int i = 1; i < poly.Count; i++)
                {
                    float dx = poly[i].x - last.x, dz = poly[i].z - last.z;
                    if (dx * dx + dz * dz >= sp2)
                    {
                        _handleIndices.Add(i);
                        last = poly[i];
                    }
                }
            }

            // ── Sync handle pool count ────────────────────────────────────────
            while (_handles.Count > _handleIndices.Count) RemoveLastHandle();
            while (_handles.Count < _handleIndices.Count) _handles.Add(CreateHandleLR());

            // ── Sync handle positions ─────────────────────────────────────────
            bool visible = _showHandles && poly.Count >= 3;
            for (int i = 0; i < _handles.Count; i++)
            {
                _handles[i].enabled = visible;
                if (visible) PlaceCircle(_handles[i], poly[_handleIndices[i]]);
            }
        }

        private void OnSelectionChanged() { /* ring/handles sync in Update() */ }

        private void PlaceCircle(LineRenderer lr, Vector3 centre)
        {
            Vector3 c = SurfacePoint(centre);
            lr.positionCount = HandleSegs;
            for (int i = 0; i < HandleSegs; i++)
            {
                float a = (float)i / HandleSegs * Mathf.PI * 2f;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * HandleRadius, 0f,
                                                   Mathf.Sin(a) * HandleRadius));
            }
        }

        private Vector3 SurfacePoint(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            float y = t != null ? t.SampleHeight(new Vector3(p.x, 0f, p.z)) + YOff : p.y + YOff;
            return new Vector3(p.x, y, p.z);
        }

        private LineRenderer CreateHandleLR()
        {
            var go = new GameObject("LH");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.widthMultiplier   = 2.5f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = _mat;
            lr.startColor        = lr.endColor = new Color(1f, 1f, 0.3f, 0.95f);
            lr.positionCount     = HandleSegs;
            lr.enabled           = false;
            return lr;
        }

        private void RemoveLastHandle()
        {
            int last = _handles.Count - 1;
            if (_handles[last] != null) Destroy(_handles[last].gameObject);
            _handles.RemoveAt(last);
        }

        private static Material BuildMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
