using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Renders the beach lasso outline while the user draws, then adds a
    /// transparent fill-preview once the lasso is complete. Both are hidden
    /// when a different tool is active.
    /// </summary>
    [AddComponentMenu("Island Builder/Beach Renderer")]
    public class BeachRenderer : MonoBehaviour
    {
        private BeachTool      _tool;
        private ITerrainReader _reader;

        // ── Outline (visible during and after drawing) ─────────────────────────
        private LineRenderer _outline;

        // ── Fill preview (visible only once lasso is closed) ─────────────────
        private MeshFilter   _filter;
        private MeshRenderer _renderer;
        private Mesh         _mesh;
        private Material     _fillMat;
        private Material     _lineMat;

        private bool _toolActive;
        private const int   PreviewStep = 4;
        private const float YOff        = 1f;

        // ── Binding ───────────────────────────────────────────────────────────

        public void Bind(BeachTool tool, ITerrainReader reader)
        {
            _tool   = tool;
            _reader = reader;

            // Materials
            _lineMat = BuildLineMaterial();
            _fillMat = BuildFillMaterial();

            // Outline LineRenderer
            _outline = gameObject.AddComponent<LineRenderer>();
            _outline.useWorldSpace     = true;
            _outline.loop              = true;
            _outline.widthMultiplier   = 3f;
            _outline.shadowCastingMode = ShadowCastingMode.Off;
            _outline.receiveShadows    = false;
            _outline.sharedMaterial    = _lineMat;
            _outline.startColor        = _outline.endColor = new Color(0.90f, 0.70f, 0.20f, 1f);
            _outline.enabled           = false;

            // Fill mesh
            _filter   = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _mesh     = new Mesh { name = "BeachPreview", indexFormat = IndexFormat.UInt32 };
            _filter.sharedMesh          = _mesh;
            _renderer.sharedMaterial    = _fillMat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows    = false;
            _renderer.enabled           = false;

            // Events
            tool.PreviewChanged += Rebuild;
            tool.Activated      += OnToolActivated;
            tool.Deactivated    += OnToolDeactivated;
        }

        private void OnToolActivated()
        {
            _toolActive = true;
            Rebuild();
        }

        private void OnToolDeactivated()
        {
            _toolActive       = false;
            _outline.enabled  = false;
            _renderer.enabled = false;
            _mesh.Clear();
        }

        private void Rebuild()
        {
            if (_tool == null || !_toolActive) return;

            var poly = _tool.Polygon;

            // ── Outline ── always visible when there are points ───────────────
            if (poly.Count >= 2)
            {
                _outline.enabled       = true;
                _outline.positionCount = poly.Count;
                var terrain = Terrain.activeTerrain;
                for (int i = 0; i < poly.Count; i++)
                {
                    float y = terrain != null
                        ? terrain.SampleHeight(new Vector3(poly[i].x, 0f, poly[i].z)) + YOff
                        : poly[i].y + YOff;
                    _outline.SetPosition(i, new Vector3(poly[i].x, y, poly[i].z));
                }
            }
            else
            {
                _outline.enabled = false;
            }

            // ── Fill preview ── only when lasso is closed ─────────────────────
            if (!_tool.HasLasso)
            {
                _mesh.Clear();
                _renderer.enabled = false;
                return;
            }

            var cells = _tool.ComputeBeachCells(PreviewStep);
            if (cells.Count == 0) { _mesh.Clear(); _renderer.enabled = false; return; }

            BuildFillMesh(cells);
            _renderer.enabled = true;
        }

        private void BuildFillMesh(List<(int hx, int hz, float targetNorm, float t, float unused)> cells)
        {
            float cw     = _reader.CellWidth;
            float cl     = _reader.CellLength;
            float worldY = _reader.WorldSize.y;
            float yOff   = Mathf.Max(0.5f, worldY * 0.001f);
            var   terrain = Terrain.activeTerrain;
            // Step-sized quads fill the preview without gaps; the inside test uses
            // cell centres so boundary cells are already confirmed inside the lasso.
            float halfW  = cw * PreviewStep * 0.52f;
            float halfL  = cl * PreviewStep * 0.52f;
            // Color gradient: sea-edge (t=0) → blue-green; shoreline (t=1) → sandy tan.
            var colLow  = new Color(0.35f, 0.70f, 0.65f, 0.45f); // sea-edge
            var colHigh = new Color(0.87f, 0.72f, 0.45f, 0.60f); // dry beach

            var verts  = new List<Vector3>(cells.Count * 4);
            var tris   = new List<int>(cells.Count * 6);
            var colors = new List<Color>(cells.Count * 4);

            foreach (var (hx, hz, targetNorm, t, _) in cells)
            {
                float cx  = (hx + 0.5f) * cw, cz = (hz + 0.5f) * cl;
                float ty  = targetNorm * worldY + yOff;
                int   b   = verts.Count;
                Color col = Color.Lerp(colLow, colHigh, t);

                AddVert(verts, cx - halfW, ty, cz - halfL, terrain, yOff);
                AddVert(verts, cx + halfW, ty, cz - halfL, terrain, yOff);
                AddVert(verts, cx - halfW, ty, cz + halfL, terrain, yOff);
                AddVert(verts, cx + halfW, ty, cz + halfL, terrain, yOff);

                for (int i = 0; i < 4; i++) colors.Add(col);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetColors(colors);
            _mesh.RecalculateBounds();
        }

        private static void AddVert(List<Vector3> verts, float x, float ty, float z,
                                     Terrain terrain, float yOff)
        {
            if (terrain != null)
            {
                float surface = terrain.SampleHeight(new Vector3(x, 0f, z)) + yOff;
                ty = Mathf.Max(ty, surface);
            }
            verts.Add(new Vector3(x, ty, z));
        }

        // ── Materials ─────────────────────────────────────────────────────────

        private static Material BuildLineMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private static Material BuildFillMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color       = Color.white;
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        private void OnDestroy()
        {
            if (_mesh    != null) Destroy(_mesh);
            if (_fillMat != null) Destroy(_fillMat);
            if (_lineMat != null) Destroy(_lineMat);
        }
    }
}
