using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Renders a transparent yellow mesh that covers only the terrain cells where sand
    /// has been added above the imported baseline. The terrain itself is untouched.
    ///
    /// The overlay GameObject is repositioned to the terrain's world origin each
    /// rebuild so vertices computed in terrain-local coordinates are always correct.
    /// </summary>
    [AddComponentMenu("Island Builder/Sand Highlight Renderer")]
    public class SandHighlightRenderer : MonoBehaviour
    {
        private TerrainManager _terrainManager;
        private bool           _active;
        private bool           _dirty;
        private float          _lastUpdateTime;

        private MeshFilter   _filter;
        private MeshRenderer _renderer;
        private Mesh         _mesh;
        private Material     _material;
        private Texture2D    _colorTex;

        private const float UpdateInterval = 0.3f;
        private const float Threshold      = 0.0001f; // normalized noise floor (above 16-bit quant error)

        public void Bind(TerrainManager terrainManager)
        {
            _terrainManager = terrainManager;
            _terrainManager.TerrainHeightsChanged += _ => { if (_active) _dirty = true; };
            EnsureComponents();
        }

        public void SetHighlighting(bool on)
        {
            _active = on;
            EnsureComponents();
            _renderer.enabled = on;
            if (on)
                _dirty = true;
            else
                _mesh.Clear();
        }

        private void Update()
        {
            if (!_active || !_dirty) return;
            if (Time.time - _lastUpdateTime < UpdateInterval) return;
            RebuildMesh();
            _dirty          = false;
            _lastUpdateTime = Time.time;
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void EnsureComponents()
        {
            if (_mesh != null) return;

            _filter   = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();

            _mesh             = new Mesh { name = "SandHighlight" };
            _mesh.indexFormat = IndexFormat.UInt32;
            _filter.sharedMesh = _mesh;

            _material = BuildMaterial(out _colorTex);
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows    = false;
            _renderer.enabled = false;
        }

        private static Material BuildMaterial(out Texture2D tex)
        {
            // 1×1 yellow texture used as _MainTex fallback.
            tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(1f, 0.85f, 0.00f, 0.75f));
            tex.Apply();

            // ── Attempt 1: URP Unlit (preferred in URP projects) ────────────
            var urpShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpShader != null)
            {
                var mat = new Material(urpShader);
                mat.mainTexture = tex;
                // Configure alpha-blend transparency
                mat.SetFloat("_Surface",  1f);   // 0 = Opaque, 1 = Transparent
                mat.SetFloat("_Blend",    0f);   // 0 = Alpha blend
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetFloat("_ZWrite",   0f);
                mat.SetInt("_SrcBlend",  (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend",  (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                mat.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.color = new Color(1f, 0.85f, 0.00f, 0.75f);
                return mat;
            }

            // ── Attempt 2: Sprites/Default (always alpha-blended) ────────────
            var spritesShader = Shader.Find("Sprites/Default");
            if (spritesShader != null)
            {
                var mat = new Material(spritesShader);
                mat.color = new Color(1f, 0.85f, 0.00f, 0.75f);
                return mat;
            }

            // ── Fallback: Standard transparent ──────────────────────────────
            var std = new Material(Shader.Find("Standard"));
            std.SetFloat("_Mode", 3f);  // Transparent
            std.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            std.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            std.SetInt("_ZWrite", 0);
            std.renderQueue = (int)RenderQueue.Transparent;
            std.color = new Color(1f, 0.85f, 0.00f, 0.75f);
            return std;
        }

        private void RebuildMesh()
        {
            if (_terrainManager == null) return;
            int res = _terrainManager.Resolution;
            if (res <= 1) { _mesh.Clear(); return; }

            // Anchor overlay at the terrain's actual world position so that
            // vertices expressed in terrain-local space map to the right world positions.
            var terrain = _terrainManager.GetComponentInChildren<Terrain>();
            transform.position = terrain != null ? terrain.transform.position : Vector3.zero;

            float cw     = _terrainManager.CellWidth;
            float cl     = _terrainManager.CellLength;
            float worldY = _terrainManager.WorldSize.y;

            // YOffset: 0.1% of terrain height — scales with terrain, avoids z-fighting.
            float yOff = Mathf.Max(0.05f, worldY * 0.001f);

            var region = new RectInt(0, 0, res, res);
            float[,] h   = _terrainManager.GetHeights(region);
            float[,] bas = _terrainManager.GetBaseline(region);

            var verts = new List<Vector3>();
            var tris  = new List<int>();
            int n = res - 1;

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                // Quad corners: [z,x], [z,x+1], [z+1,x], [z+1,x+1]
                bool anyAdded = h[z,   x    ] - bas[z,   x    ] > Threshold
                             || h[z,   x + 1] - bas[z,   x + 1] > Threshold
                             || h[z+1, x    ] - bas[z+1, x    ] > Threshold
                             || h[z+1, x + 1] - bas[z+1, x + 1] > Threshold;
                if (!anyAdded) continue;

                int b = verts.Count;
                // Terrain-local coords: X=x*cw, Y=normalised_height*worldY, Z=z*cl
                verts.Add(new Vector3( x      * cw, h[z,   x    ] * worldY + yOff,  z      * cl));
                verts.Add(new Vector3((x + 1) * cw, h[z,   x + 1] * worldY + yOff,  z      * cl));
                verts.Add(new Vector3( x      * cw, h[z+1, x    ] * worldY + yOff, (z + 1) * cl));
                verts.Add(new Vector3((x + 1) * cw, h[z+1, x + 1] * worldY + yOff, (z + 1) * cl));

                // CCW winding for upward-facing normals (viewed from above).
                tris.Add(b);     tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh     != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_colorTex != null) Destroy(_colorTex);
        }
    }
}
