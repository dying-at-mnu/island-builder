using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Toggleable terrain-conforming grid overlay. Grid spacing defaults to one
    /// unit of whatever coordinate system the last imported DXF used; the user
    /// can adjust the spacing with a slider or a typed value in the sidebar.
    /// </summary>
    [AddComponentMenu("Island Builder/Grid Renderer")]
    public class GridRenderer : MonoBehaviour
    {
        // ── Public state read by ToolParameterGUI ─────────────────────────────
        public bool   IsActive        { get; private set; }
        public bool   IsContourActive { get; private set; }
        public float  SpacingMetres   { get; private set; } = 100f;
        public string UnitName        { get; private set; } = "Metres";
        public float  UnitScale       { get; private set; } = 1f; // metres per display unit
        public float  DisplaySpacing  => SpacingMetres / Mathf.Max(1e-9f, UnitScale);

        // ── Private ───────────────────────────────────────────────────────────
        private TerrainManager            _terrain;
        private ImportManager             _importManager;
        private Material                  _mat;
        private Material                  _contourMat;
        private readonly List<GameObject> _lineObjs    = new();
        private readonly List<GameObject> _contourObjs = new();

        private bool  _terrainDirty;
        private float _lastRebuildTime;
        private const float RebuildDelay = 0.5f;

        private const int   MaxLinesPerDir = 80;
        private const float MinSpacingM    = 0.001f;

        // ── Public API ────────────────────────────────────────────────────────

        public void Bind(TerrainManager terrain, ImportManager importManager)
        {
            _terrain       = terrain;
            _importManager = importManager;
            _mat           = BuildMaterial();
            _contourMat    = BuildContourMaterial();
            importManager.ImportCompleted += OnImportCompleted;
            terrain.TerrainHeightsChanged += _ =>
            {
                if (IsActive)        _terrainDirty = true;
                if (IsContourActive) _terrainDirty = true;
            };
        }

        private void Update()
        {
            if (!_terrainDirty) return;
            if (Time.time - _lastRebuildTime < RebuildDelay) return;
            if (IsActive)        Rebuild();
            if (IsContourActive) RebuildContours();
            _terrainDirty    = false;
            _lastRebuildTime = Time.time;
        }

        public void SetActive(bool on)
        {
            IsActive = on;
            if (on) { IsContourActive = false; ClearContourLines(); Rebuild(); }
            else    ClearLines();
        }

        public void SetContourActive(bool on)
        {
            IsContourActive = on;
            if (on) { IsActive = false; ClearLines(); RebuildContours(); }
            else    ClearContourLines();
        }

        /// <summary>Sets spacing directly in metres — used by the save/load system.</summary>
        /// <summary>Updates the display unit without changing the physical spacing.</summary>
        public void SetUnit(string name, float scale)
        {
            UnitName  = string.IsNullOrEmpty(name) ? "Metres" : name;
            UnitScale = Mathf.Max(1e-9f, scale);
            if (IsActive) Rebuild();
        }

        public void SetSpacingMetres(float metres)
        {
            SpacingMetres = Mathf.Max(MinSpacingM, metres);
            if (IsActive) Rebuild();
        }

        public void SetDisplaySpacing(float displayUnits)
        {
            SpacingMetres = Mathf.Max(MinSpacingM, displayUnits * UnitScale);
            if (IsActive) Rebuild();
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void OnImportCompleted(TerrainData _, float __)
        {
            UnitName  = _importManager.LastDetectedUnit;
            UnitScale = Mathf.Max(1e-9f, _importManager.LastUnitScaleFactor);

            // Default spacing = contour interval in metres (raw Z-diff × unit scale).
            float interval = _importManager.LastContourInterval;
            SpacingMetres  = (interval > 0f)
                ? Mathf.Max(MinSpacingM, interval * UnitScale)
                : Mathf.Max(MinSpacingM, UnitScale);
            _contourLines = _importManager.LastContourLines ?? System.Array.Empty<ContourPolyline>();
            if (IsActive)        Rebuild();
            if (IsContourActive) RebuildContours();
        }

        private void Rebuild()
        {
            ClearLines();
            if (_terrain == null || _terrain.Resolution <= 1) return;

            var size  = _terrain.WorldSize;
            if (size.x <= 0f || size.z <= 0f) return;

            int res     = _terrain.Resolution;
            float cw    = _terrain.CellWidth;
            float cl    = _terrain.CellLength;
            float worldY = size.y;
            // Large Y offset ensures no z-fighting; ZTest Always in the material
            // means the lines render over sand highlight mesh and terrain alike.
            float yOff  = Mathf.Max(3f, worldY * 0.005f);
            float lw    = Mathf.Max(0.9f, Mathf.Min(size.x, size.z) * 0.0006f);

            float[,] h  = _terrain.GetHeights(new RectInt(0, 0, res, res));

            // Effective spacing — auto-increase if grid would exceed the line cap.
            float sp = Mathf.Max(MinSpacingM, SpacingMetres);
            sp = Mathf.Max(sp, size.x / MaxLinesPerDir);
            sp = Mathf.Max(sp, size.z / MaxLinesPerDir);

            // Samples per line — enough for terrain-conforming look without excess.
            int sampPerXLine = Mathf.Clamp(Mathf.CeilToInt(size.x / sp) + 2, 2, 128);
            int sampPerZLine = Mathf.Clamp(Mathf.CeilToInt(size.z / sp) + 2, 2, 128);

            // Lines parallel to X axis (constant Z values)
            for (float z = sp; z < size.z + sp * 0.01f; z += sp)
            {
                z = Mathf.Min(z, size.z);
                var lr = CreateLR(sampPerXLine, lw);
                for (int j = 0; j < sampPerXLine; j++)
                {
                    float x  = size.x * j / (sampPerXLine - 1);
                    int   hx = Mathf.Clamp(Mathf.RoundToInt(x / cw), 0, res - 1);
                    int   hz = Mathf.Clamp(Mathf.RoundToInt(z / cl), 0, res - 1);
                    lr.SetPosition(j, new Vector3(x, h[hz, hx] * worldY + yOff, z));
                }
                if (z >= size.z) break;
            }

            // Lines parallel to Z axis (constant X values)
            for (float x = sp; x < size.x + sp * 0.01f; x += sp)
            {
                x = Mathf.Min(x, size.x);
                var lr = CreateLR(sampPerZLine, lw);
                for (int j = 0; j < sampPerZLine; j++)
                {
                    float z  = size.z * j / (sampPerZLine - 1);
                    int   hx = Mathf.Clamp(Mathf.RoundToInt(x / cw), 0, res - 1);
                    int   hz = Mathf.Clamp(Mathf.RoundToInt(z / cl), 0, res - 1);
                    lr.SetPosition(j, new Vector3(x, h[hz, hx] * worldY + yOff, z));
                }
                if (x >= size.x) break;
            }
        }

        private LineRenderer CreateLR(int points, float width)
        {
            var go = new GameObject("GL");
            go.transform.SetParent(transform, false);
            _lineObjs.Add(go);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.positionCount     = points;
            lr.widthMultiplier   = width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.material          = _mat;
            lr.startColor        = lr.endColor = new Color(0.45f, 0.45f, 0.45f, 0.80f);
            return lr;
        }

        private void ClearLines()
        {
            foreach (var go in _lineObjs)
                if (go != null) Destroy(go);
            _lineObjs.Clear();
        }

        // ── Contour lines ──────────────────────────────────────────────────────

        private ContourPolyline[] _contourLines = System.Array.Empty<ContourPolyline>();

        private void RebuildContours()
        {
            ClearContourLines();
            if (_contourLines == null || _contourLines.Length == 0) return;
            if (_terrain == null || _terrain.Resolution <= 1) return;

            float worldY = _terrain.WorldSize.y;
            float yOff   = Mathf.Max(2f, worldY * 0.004f);
            float lw     = Mathf.Max(0.5f, Mathf.Min(_terrain.WorldSize.x, _terrain.WorldSize.z) * 0.0004f);

            var activeTerrain = Terrain.activeTerrain;

            foreach (var cl in _contourLines)
            {
                if (cl?.Points == null || cl.Points.Length < 2) continue;

                var go = new GameObject("CL");
                go.transform.SetParent(transform, false);
                _contourObjs.Add(go);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace     = true;
                lr.positionCount     = cl.Points.Length;
                lr.widthMultiplier   = lw;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows    = false;
                lr.material          = _contourMat;
                lr.startColor        = lr.endColor = new Color(0.45f, 0.45f, 0.45f, 0.80f);

                for (int i = 0; i < cl.Points.Length; i++)
                {
                    float wx = cl.Points[i].x;
                    float wz = cl.Points[i].y;
                    float wy = activeTerrain != null
                        ? activeTerrain.SampleHeight(new Vector3(wx, 0f, wz)) + yOff
                        : yOff;
                    lr.SetPosition(i, new Vector3(wx, wy, wz));
                }
            }
        }

        private void ClearContourLines()
        {
            foreach (var go in _contourObjs)
                if (go != null) Destroy(go);
            _contourObjs.Clear();
        }

        private static Material BuildContourMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            mat.renderQueue = (int)RenderQueue.Transparent - 1;
            return mat;
        }

        private static Material BuildMaterial()
        {
            // Grid uses normal depth testing (ZTest LEqual) so it doesn't draw
            // through the water surface — the water's transparency then composites
            // over the grid exactly as it would over the terrain, keeping the water
            // appearance identical whether the grid is on or off.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            // Sprites/Default defaults to ZTest LEqual — do NOT set unity_GUIZTestMode to Always.
            // Render just before transparent objects so water alpha-blends on top.
            mat.renderQueue = (int)RenderQueue.Transparent - 1; // 2999
            return mat;
        }

        internal static Material BuildAlwaysOnTopMat()
        {
            // RenderQueue.Overlay (4000) ensures lines draw after all scene geometry.
            // unity_GUIZTestMode is omitted — it is a built-in-pipeline hack that
            // causes URP 17 to misconfigure depth attachments on shadow/LUT passes.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private void OnDestroy()
        {
            ClearLines();
            ClearContourLines();
            if (_mat        != null) Destroy(_mat);
            if (_contourMat != null) Destroy(_contourMat);
        }
    }
}
