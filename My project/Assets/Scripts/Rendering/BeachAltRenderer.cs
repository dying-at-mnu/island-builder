using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Renders the two Catmull-Rom curves of BeachToolAlt and their anchor handles.
    /// Inner curve = gold, Outer curve = cyan.
    /// Anchor handles = small yellow circles, shown when the tool is active.
    /// A transparent preview mesh fills the corridor between the curves.
    /// </summary>
    [AddComponentMenu("Island Builder/Beach Alt Renderer")]
    public class BeachAltRenderer : MonoBehaviour
    {
        private BeachToolAlt   _tool;
        private ITerrainReader _reader;

        private LineRenderer _innerLine;
        private LineRenderer _outerLine;
        private MeshFilter   _previewFilter;
        private MeshRenderer _previewRenderer;
        private Mesh         _previewMesh;
        private Material     _lineMat;
        private Material     _previewMat;

        private readonly List<LineRenderer> _innerHandles = new();
        private readonly List<LineRenderer> _outerHandles = new();

        private bool _toolActive;

        private const int   CurveSegments  = 80;
        private const float HandleRadius   = 3f;
        private const int   HandleSegs     = 10;
        private const float YOff           = 1.2f;

        public void Bind(BeachToolAlt tool, ITerrainReader reader)
        {
            _tool   = tool;
            _reader = reader;
            _lineMat    = BuildLineMat();
            _previewMat = BuildPreviewMat();

            _innerLine = CreateLR(new Color(1f, 0.75f, 0.1f), 3f);
            _outerLine = CreateLR(new Color(0.2f, 0.9f, 1.0f), 3f);

            _previewFilter   = gameObject.AddComponent<MeshFilter>();
            _previewRenderer = gameObject.AddComponent<MeshRenderer>();
            _previewMesh     = new Mesh { name = "BeachAltPreview", indexFormat = IndexFormat.UInt32 };
            _previewFilter.sharedMesh       = _previewMesh;
            _previewRenderer.sharedMaterial = _previewMat;
            _previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _previewRenderer.receiveShadows    = false;
            _previewRenderer.enabled           = false;

            tool.PreviewChanged += Rebuild;
            tool.Activated      += OnActivated;
            tool.Deactivated    += OnDeactivated;
        }

        // ── Exposed handle info for SculptController ──────────────────────────

        public int InnerHandleCount => _innerHandles.Count;
        public int OuterHandleCount => _outerHandles.Count;

        // ── Visibility control ────────────────────────────────────────────────

        private void OnActivated()  { _toolActive = true;  Rebuild(); }
        private void OnDeactivated()
        {
            _toolActive = false;
            _innerLine.enabled = _outerLine.enabled = false;
            _previewRenderer.enabled = false;
            SetHandlesVisible(_innerHandles, false);
            SetHandlesVisible(_outerHandles, false);
        }

        // ── Rebuild ───────────────────────────────────────────────────────────

        private void Rebuild()
        {
            if (!_toolActive || _tool == null) return;
            var terrain = Terrain.activeTerrain;

            // Inner curve
            UpdateCurveLine(_innerLine, _tool.InnerCurve, terrain);
            UpdateHandlePool(_innerHandles, _tool.InnerCurve, terrain);

            // Outer curve
            UpdateCurveLine(_outerLine, _tool.OuterCurve, terrain);
            UpdateHandlePool(_outerHandles, _tool.OuterCurve, terrain);

            // Preview mesh
            bool showPreview = _tool.Phase == BeachAltPhase.BothReady ||
                               _tool.Phase == BeachAltPhase.DrawingOuter;
            if (showPreview)
                BuildPreviewMesh();
            else
            {
                _previewMesh.Clear();
                _previewRenderer.enabled = false;
            }
        }

        private void UpdateCurveLine(LineRenderer lr, BeachSpline spline, Terrain terrain)
        {
            if (!spline.HasCurve) { lr.enabled = false; return; }
            var pts = spline.Evaluate(CurveSegments);
            lr.enabled        = true;
            lr.positionCount  = pts.Length;
            for (int i = 0; i < pts.Length; i++)
            {
                float y = terrain != null
                    ? terrain.SampleHeight(new Vector3(pts[i].x, 0f, pts[i].z)) + YOff
                    : pts[i].y + YOff;
                lr.SetPosition(i, new Vector3(pts[i].x, y, pts[i].z));
            }
        }

        private void UpdateHandlePool(List<LineRenderer> handles, BeachSpline spline, Terrain terrain)
        {
            var anchors = spline.Anchors;
            while (handles.Count > anchors.Count) RemoveLastHandle(handles);
            while (handles.Count < anchors.Count) handles.Add(CreateHandleLR());

            bool visible = _toolActive && anchors.Count >= 1;
            for (int i = 0; i < handles.Count; i++)
            {
                handles[i].enabled = visible;
                if (visible) PlaceCircle(handles[i], anchors[i], terrain);
            }
        }

        private void PlaceCircle(LineRenderer lr, Vector3 centre, Terrain terrain)
        {
            float y = terrain != null
                ? terrain.SampleHeight(new Vector3(centre.x, 0f, centre.z)) + YOff
                : centre.y + YOff;
            var c = new Vector3(centre.x, y, centre.z);
            lr.positionCount = HandleSegs;
            for (int i = 0; i < HandleSegs; i++)
            {
                float a = (float)i / HandleSegs * Mathf.PI * 2f;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * HandleRadius, 0f,
                                                   Mathf.Sin(a) * HandleRadius));
            }
        }

        private void BuildPreviewMesh()
        {
            var cells = _tool.ComputeBeachCells(4);
            if (cells.Count == 0) { _previewMesh.Clear(); _previewRenderer.enabled = false; return; }

            float cw = _reader.CellWidth, cl = _reader.CellLength;
            float worldY = _reader.WorldSize.y;
            float yOff   = Mathf.Max(0.5f, worldY * 0.001f);
            float halfW  = cw * 4 * 0.52f, halfL = cl * 4 * 0.52f;
            var terrain  = Terrain.activeTerrain;

            var verts  = new List<Vector3>(cells.Count * 4);
            var tris   = new List<int>(cells.Count * 6);
            var colors = new List<Color>(cells.Count * 4);
            var colLow = new Color(0.35f, 0.70f, 0.65f, 0.40f);
            var colHigh= new Color(0.87f, 0.72f, 0.45f, 0.55f);

            foreach (var (hx, hz, targetNorm, t, _) in cells)
            {
                float cx = (hx + 0.5f) * cw, cz = (hz + 0.5f) * cl;
                float ty = targetNorm * worldY + yOff;
                int   b  = verts.Count;
                var   col = Color.Lerp(colLow, colHigh, t);

                AddVert(verts, cx-halfW, ty, cz-halfL, terrain, yOff);
                AddVert(verts, cx+halfW, ty, cz-halfL, terrain, yOff);
                AddVert(verts, cx-halfW, ty, cz+halfL, terrain, yOff);
                AddVert(verts, cx+halfW, ty, cz+halfL, terrain, yOff);
                for (int i = 0; i < 4; i++) colors.Add(col);
                tris.Add(b); tris.Add(b+2); tris.Add(b+1);
                tris.Add(b+1); tris.Add(b+2); tris.Add(b+3);
            }

            _previewMesh.Clear();
            _previewMesh.SetVertices(verts);
            _previewMesh.SetTriangles(tris, 0);
            _previewMesh.SetColors(colors);
            _previewMesh.RecalculateBounds();
            _previewRenderer.enabled = true;
        }

        private static void AddVert(List<Vector3> v, float x, float ty, float z,
                                     Terrain t, float yOff)
        {
            if (t != null) ty = Mathf.Max(ty, t.SampleHeight(new Vector3(x,0,z)) + yOff);
            v.Add(new Vector3(x, ty, z));
        }

        // ── Material / GO helpers ─────────────────────────────────────────────

        private LineRenderer CreateLR(Color col, float width)
        {
            var go = new GameObject("BAL");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = false;
            lr.widthMultiplier   = width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = _lineMat;
            lr.startColor        = lr.endColor = col;
            lr.enabled           = false;
            return lr;
        }

        private LineRenderer CreateHandleLR()
        {
            var go = new GameObject("BAH");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.widthMultiplier   = 2f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = _lineMat;
            lr.startColor        = lr.endColor = new Color(1f, 1f, 0.3f, 0.95f);
            lr.positionCount     = HandleSegs;
            lr.enabled           = false;
            return lr;
        }

        private static void RemoveLastHandle(List<LineRenderer> list)
        {
            int last = list.Count - 1;
            if (list[last] != null) Destroy(list[last].gameObject);
            list.RemoveAt(last);
        }

        private static void SetHandlesVisible(List<LineRenderer> list, bool on)
        {
            foreach (var lr in list) if (lr != null) lr.enabled = on;
        }

        private static Material BuildLineMat()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private static Material BuildPreviewMat()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color       = Color.white;
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        private void OnDestroy()
        {
            while (_innerHandles.Count > 0) RemoveLastHandle(_innerHandles);
            while (_outerHandles.Count > 0) RemoveLastHandle(_outerHandles);
            if (_previewMesh != null) Destroy(_previewMesh);
            if (_lineMat     != null) Destroy(_lineMat);
            if (_previewMat  != null) Destroy(_previewMat);
        }
    }
}
