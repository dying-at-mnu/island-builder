using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;
using IslandBuilder.Interaction;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Generates a solid terrain mesh in the area surrounding the imported terrain,
    /// matching the water extension boundaries.  Heights outside the terrain are
    /// clamped to the nearest terrain edge so the ground continues seamlessly.
    ///
    /// This mesh also carries a MeshCollider + TerrainExtensionCollider so the
    /// Lasso tool can raycast beyond the terrain boundary.
    ///
    /// Sculpting tools operate only on the Unity Terrain (TerrainCollider), so
    /// adding sand outside the imported boundary is naturally impossible.
    /// </summary>
    [AddComponentMenu("Island Builder/Terrain Extension Renderer")]
    public class TerrainExtensionRenderer : MonoBehaviour
    {
        private TerrainManager _terrain;
        private MeshFilter     _filter;
        private MeshRenderer   _renderer;
        private MeshCollider   _collider;
        private Mesh           _mesh;
        private Material       _mat;

        // Must match WaterRenderer.ExtendFraction so the extension fills exactly
        // to the water boundary.
        private const float ExtendFraction = 1.0f;
        private const int   MaxSamples     = 128;

        private bool  _dirty       = true;
        private float _lastRebuild = -999f;
        private const float Delay  = 2f;

        // ── Binding ───────────────────────────────────────────────────────────

        public void Bind(TerrainManager tm, ImportManager im)
        {
            _terrain = tm;
            tm.TerrainHeightsChanged += _ => _dirty = true;
            im.ImportCompleted       += (_, __) =>
            {
                _dirty       = true;
                _lastRebuild = Time.time - Delay;
                Rebuild();
            };
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            _filter   = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _collider = gameObject.AddComponent<MeshCollider>();

            _mesh = new Mesh { name = "TerrainExtension", indexFormat = IndexFormat.UInt32 };
            _filter.sharedMesh = _mesh;

            _mat = BuildMaterial();
            _renderer.sharedMaterial    = _mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows    = false;
            _renderer.enabled           = false;

            // Marker so SculptController can identify this collider.
            gameObject.AddComponent<TerrainExtensionCollider>();
        }

        private void Update()
        {
            if (_dirty && Time.time - _lastRebuild >= Delay)
                Rebuild();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_mat  != null) Destroy(_mat);
        }

        // ── Mesh building ─────────────────────────────────────────────────────

        private static readonly List<Vector3> _v = new();
        private static readonly List<int>     _t = new();

        private void Rebuild()
        {
            _dirty       = false;
            _lastRebuild = Time.time;

            if (_terrain == null) return;
            int   res    = _terrain.Resolution;
            float worldY = _terrain.WorldSize.y;
            if (res <= 1 || worldY <= 0f) return;

            float[,] h = _terrain.GetHeights(new RectInt(0, 0, res, res));

            int   step = Mathf.Max(1, (res - 1) / MaxSamples);
            int   nx   = Mathf.CeilToInt((float)(res - 1) / step);
            int   nz   = nx;
            float cw   = _terrain.CellWidth  * step;
            float cl   = _terrain.CellLength * step;
            int   ext  = Mathf.Max(4, Mathf.RoundToInt(nx * ExtendFraction));

            _v.Clear(); _t.Clear();

            for (int zi = -ext; zi < nz + ext; zi++)
            for (int xi = -ext; xi < nx + ext; xi++)
            {
                // Skip cells that are fully inside the terrain footprint —
                // the Unity Terrain component renders those.
                if (xi >= 0 && xi < nx && zi >= 0 && zi < nz) continue;

                int hx0 = Mathf.Clamp(xi * step,       0, res - 1);
                int hx1 = Mathf.Clamp((xi + 1) * step, 0, res - 1);
                int hz0 = Mathf.Clamp(zi * step,       0, res - 1);
                int hz1 = Mathf.Clamp((zi + 1) * step, 0, res - 1);

                float y00 = h[hz0, hx0] * worldY;
                float y10 = h[hz0, hx1] * worldY;
                float y01 = h[hz1, hx0] * worldY;
                float y11 = h[hz1, hx1] * worldY;

                float wx0 = xi * cw, wx1 = (xi + 1) * cw;
                float wz0 = zi * cl, wz1 = (zi + 1) * cl;

                int b = _v.Count;
                _v.Add(new Vector3(wx0, y00, wz0));
                _v.Add(new Vector3(wx1, y10, wz0));
                _v.Add(new Vector3(wx0, y01, wz1));
                _v.Add(new Vector3(wx1, y11, wz1));

                // CW from above = upward normal.
                _t.Add(b);   _t.Add(b+2); _t.Add(b+1);
                _t.Add(b+1); _t.Add(b+2); _t.Add(b+3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            // Reset collider to force a rebuild.
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;

            _renderer.enabled = _v.Count > 0;
        }

        private static Material BuildMaterial()
        {
            var sh  = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh) { color = Color.white };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);
            return mat;
        }
    }
}
