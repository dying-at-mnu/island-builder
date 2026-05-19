using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;
using static IslandBuilder.Domain.ColorTables;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Volumetric water mesh: top face (sea level) + bottom face (seafloor) + side walls.
    /// The mesh extends beyond the terrain footprint; heights outside the terrain are
    /// clamped to the nearest terrain edge so the open-ocean floor continues smoothly.
    /// A grey rectangle on the water surface marks the actual terrain boundary.
    /// </summary>
    [AddComponentMenu("Island Builder/Water Renderer")]
    public class WaterRenderer : MonoBehaviour
    {
        [SerializeField] private ImportManager _importManager;

        [Header("Appearance")]
        [SerializeField, Range(0f, 1f)] private float _opacity    = 1.00f;
        [SerializeField, Range(0f, 1f)] private float _smoothness = 0.15f;

        private int _colorTableIndex = 0;

        private const float ExtendFraction     = 1.0f;
        private const int   MaxSamplesPerAxis = 128; // side walls
        private const int   TopFaceSamples    = 512; // top face — high-res for smooth colour bands
        private const int   BottomFaceSamples = 512; // seafloor face

        private TerrainManager _terrainManager;
        private MeshFilter     _meshFilter;
        private MeshRenderer   _meshRenderer;
        private Mesh           _mesh;
        private Material       _mat;
        private LineRenderer   _boundary;
        private Material       _boundaryMat;
        private float          _seaLevel         = 0f;
        private float          _terrainUnitScale = 1f;
        private bool           _meshDirty        = true;
        private float          _lastRebuild      = -999f;
        private const float    RebuildDelay      = 2f;

        // ── Binding ───────────────────────────────────────────────────────────

        public void BindImportManager(ImportManager im)
        {
            if (_importManager != null) _importManager.ImportCompleted -= OnImportCompleted;
            _importManager = im;
            if (_importManager != null) _importManager.ImportCompleted += OnImportCompleted;
        }

        public void BindTerrainManager(TerrainManager tm)
        {
            _terrainManager = tm;
            tm.TerrainHeightsChanged += _ => _meshDirty = true;
        }

        public void SetTerrainScale(float metresPerUnit)
        {
            float c = Mathf.Max(1e-9f, metresPerUnit);
            if (Mathf.Approximately(c, _terrainUnitScale)) return;
            _terrainUnitScale = c;
            _meshDirty        = true;
            _lastRebuild      = Time.time - RebuildDelay;
        }

        public void SetOpacity(float opacity)
        {
            _opacity     = Mathf.Clamp01(opacity);
            _meshDirty   = true;
            _lastRebuild = Time.time - RebuildDelay;
        }

        private bool _showTopFace  = true;
        private bool _showSeafloor = false;

        public void SetShowTopFace(bool show)  { _showTopFace  = show; _meshDirty = true; _lastRebuild = Time.time - RebuildDelay; }
        public void SetShowSeafloor(bool show) { _showSeafloor = show; _meshDirty = true; _lastRebuild = Time.time - RebuildDelay; }

        public void SetColorTable(int index)
        {
            _colorTableIndex = Mathf.Clamp(index, 0, ColorTables.Water.Length - 1);
            _meshDirty       = true;
            _lastRebuild     = Time.time - RebuildDelay;
        }

        public void SetSeaLevel(float metres)
        {
            _seaLevel    = metres;
            _meshDirty   = true;
            _lastRebuild = -999f;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()  { if (_importManager != null) _importManager.ImportCompleted += OnImportCompleted; }
        private void OnDisable() { if (_importManager != null) _importManager.ImportCompleted -= OnImportCompleted; }

        private void Start()
        {
            // Water volume mesh
            _meshFilter   = gameObject.AddComponent<MeshFilter>();
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _mesh         = new Mesh { name = "WaterVolume", indexFormat = IndexFormat.UInt32 };
            _meshFilter.sharedMesh = _mesh;
            _mat = BuildWaterMaterial();
            _meshRenderer.sharedMaterial = _mat;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows    = false;
            _meshRenderer.enabled = false;

            // Terrain boundary indicator
            var bGo = new GameObject("TerrainBoundary");
            bGo.transform.SetParent(transform, false);
            _boundary = bGo.AddComponent<LineRenderer>();
            _boundary.useWorldSpace     = true;
            _boundary.loop              = true;
            _boundary.positionCount     = 0; // set dynamically after import
            _boundary.widthMultiplier   = 4f;
            _boundary.shadowCastingMode = ShadowCastingMode.Off;
            _boundary.receiveShadows    = false;
            _boundaryMat = GridRenderer.BuildAlwaysOnTopMat();
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.65f, 0.65f, 0.65f), 0f),
                        new GradientColorKey(new Color(0.65f, 0.65f, 0.65f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.95f, 1f) });
            _boundary.colorGradient   = grad;
            _boundary.sharedMaterial  = _boundaryMat;
            _boundary.enabled         = false;
        }

        private void Update()
        {
            if (_meshDirty && Time.time - _lastRebuild >= RebuildDelay)
                RebuildMesh();
        }

        private Material BuildWaterMaterial()
        {
            // URP Particles/Lit supports vertex colours + transparency + PBR lighting.
            // Falls back to unlit Sprites/Default if the shader isn't in the project.
            var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.SetFloat("_Surface",       1f);  // Transparent
                mat.SetFloat("_Blend",         0f);  // Alpha blend
                mat.SetFloat("_Cull",          0f);  // Off — double-sided
                mat.SetFloat("_ZWrite",        0f);
                // URP 14+ splits blend factors into RGB + Alpha channels.
                mat.SetFloat("_SrcBlend",      5f);  // SrcAlpha
                mat.SetFloat("_DstBlend",     10f);  // OneMinusSrcAlpha
                mat.SetFloat("_SrcBlendAlpha", 1f);  // One
                mat.SetFloat("_DstBlendAlpha",10f);  // OneMinusSrcAlpha
                mat.SetFloat("_Smoothness",    _smoothness);
                mat.SetFloat("_Metallic",      0f);
                mat.SetFloat("_ColorMode",     0f);  // Multiply vertex colour × base
                mat.SetColor("_BaseColor",     Color.white);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
                return mat;
            }
            // Fallback — unlit but correctly transparent.
            var fb = new Material(Shader.Find("Sprites/Default"));
            fb.renderQueue = (int)RenderQueue.Transparent;
            return fb;
        }

        private void OnDestroy()
        {
            if (_mesh        != null) Destroy(_mesh);
            if (_mat         != null) Destroy(_mat);
            if (_boundaryMat != null) Destroy(_boundaryMat);
        }

        // ── Import handler ────────────────────────────────────────────────────

        private void OnImportCompleted(TerrainData td, float displaySeaLevel)
        {
            _meshDirty   = true;
            _lastRebuild = -999f;
            RebuildMesh();
        }

        // ── Mesh building ─────────────────────────────────────────────────────

        private static readonly List<Vector3> _v = new();
        private static readonly List<int>     _t = new();
        private static readonly List<Color>   _c = new();

        private void RebuildMesh()
        {
            _meshDirty   = false;
            _lastRebuild = Time.time;

            if (_terrainManager == null || _seaLevel <= 0f)
            {
                _mesh?.Clear(); _meshRenderer.enabled = false; _boundary.enabled = false; return;
            }

            int   res    = _terrainManager.Resolution;
            float worldY = _terrainManager.WorldSize.y;
            if (res <= 1 || worldY <= 0f) return;

            float seaNorm    = Mathf.Clamp01(_seaLevel / worldY);
            float unitSc     = Mathf.Max(1e-9f, _terrainUnitScale);
            // worldY / VE gives the unexaggerated real-world height range in metres.
            // Z-values in the file are already real-world depths, so no unit scale applied.
            float vExag      = Mathf.Max(1f, _terrainManager.VerticalExaggeration);
            float depthScale = worldY;

            float[,] h = _terrainManager.GetHeights(new RectInt(0, 0, res, res));

            // Terrain grid cells
            int   step = Mathf.Max(1, (res - 1) / MaxSamplesPerAxis);
            int   nx   = Mathf.CeilToInt((float)(res - 1) / step);
            int   nz   = nx;
            float cw   = _terrainManager.CellWidth  * step;
            float cl   = _terrainManager.CellLength * step;

            // Extension: ocean extends this many cells beyond the terrain edge.
            int ext = Mathf.Max(4, Mathf.RoundToInt(nx * ExtendFraction));

            _v.Clear(); _t.Clear(); _c.Clear();

            float zBias = worldY * 0.001f; // small lift to prevent Z-fighting

            // ── Main pass: top face + side walls + vertical columns ───────────
            // Covers the full grid including the extended ocean area.
            // Outside the terrain footprint, heightmap indices are clamped to the
            // nearest edge so the ocean floor continues smoothly outward.
            for (int zi = -ext; zi < nz + ext; zi++)
            for (int xi = -ext; xi < nx + ext; xi++)
            {
                int hx0 = Mathf.Clamp(xi * step,       0, res - 1);
                int hx1 = Mathf.Clamp((xi + 1) * step, 0, res - 1);
                int hz0 = Mathf.Clamp(zi * step,       0, res - 1);
                int hz1 = Mathf.Clamp((zi + 1) * step, 0, res - 1);

                float h00 = h[hz0, hx0], h10 = h[hz0, hx1];
                float h01 = h[hz1, hx0], h11 = h[hz1, hx1];

                if (h00 >= seaNorm && h10 >= seaNorm &&
                    h01 >= seaNorm && h11 >= seaNorm) continue;

                float wx0 = xi * cw, wx1 = (xi + 1) * cw;
                float wz0 = zi * cl, wz1 = (zi + 1) * cl;

                float by00 = Mathf.Min(h00, seaNorm) * worldY;
                float by10 = Mathf.Min(h10, seaNorm) * worldY;
                float by01 = Mathf.Min(h01, seaNorm) * worldY;
                float by11 = Mathf.Min(h11, seaNorm) * worldY;

                Color c00 = DepthColor((seaNorm - Mathf.Min(h00, seaNorm)) * depthScale);
                Color c10 = DepthColor((seaNorm - Mathf.Min(h10, seaNorm)) * depthScale);
                Color c01 = DepthColor((seaNorm - Mathf.Min(h01, seaNorm)) * depthScale);
                Color c11 = DepthColor((seaNorm - Mathf.Min(h11, seaNorm)) * depthScale);

                // Top face — extended ocean area only; terrain footprint uses the
                // high-res pass below for smooth colour bands.
                if (_showTopFace && (xi < 0 || xi >= nx || zi < 0 || zi >= nz))
                    AddQuad(wx0, _seaLevel, wz0,  wx1, _seaLevel, wz0,
                            wx0, _seaLevel, wz1,  wx1, _seaLevel, wz1,
                            c00, c10, c01, c11, facingUp: true);

                // Side walls.
                if (xi + 1 >= nx + ext || !HasWater(xi + 1, zi, step, res, seaNorm, h))
                    AddWall(new Vector3(wx1, by10, wz0), new Vector3(wx1, by11, wz1),
                            new Vector3(wx1, _seaLevel, wz0), new Vector3(wx1, _seaLevel, wz1),
                            c10, c11);

                if (xi - 1 < -ext || !HasWater(xi - 1, zi, step, res, seaNorm, h))
                    AddWall(new Vector3(wx0, by00, wz0), new Vector3(wx0, by01, wz1),
                            new Vector3(wx0, _seaLevel, wz0), new Vector3(wx0, _seaLevel, wz1),
                            c00, c01);

                if (zi + 1 >= nz + ext || !HasWater(xi, zi + 1, step, res, seaNorm, h))
                    AddWall(new Vector3(wx0, by01, wz1), new Vector3(wx1, by11, wz1),
                            new Vector3(wx0, _seaLevel, wz1), new Vector3(wx1, _seaLevel, wz1),
                            c01, c11);

                if (zi - 1 < -ext || !HasWater(xi, zi - 1, step, res, seaNorm, h))
                    AddWall(new Vector3(wx0, by00, wz0), new Vector3(wx1, by10, wz0),
                            new Vector3(wx0, _seaLevel, wz0), new Vector3(wx1, _seaLevel, wz0),
                            c00, c10);


                // Seafloor for the extended ocean area.
                // The terrain footprint gets a higher-resolution pass below.
                if (_showSeafloor && (xi < 0 || xi >= nx || zi < 0 || zi >= nz))
                    AddQuad(wx0, by00 + zBias, wz0,  wx1, by10 + zBias, wz0,
                            wx0, by01 + zBias, wz1,  wx1, by11 + zBias, wz1,
                            c00, c10, c01, c11, facingUp: false);
            }

            // ── High-res top face — terrain footprint only ───────────────────
            // Sampled at full heightmap resolution so depth-colour bands are as
            // smooth as the source data, with no coarse-grid staircase artefacts.
            if (_showTopFace)
            {
                int   tStep = Mathf.Max(1, (res - 1) / TopFaceSamples);
                int   tnx   = Mathf.CeilToInt((float)(res - 1) / tStep);
                float tcw   = _terrainManager.CellWidth  * tStep;
                float tcl   = _terrainManager.CellLength * tStep;

                for (int zi = 0; zi < tnx; zi++)
                for (int xi = 0; xi < tnx; xi++)
                {
                    int hx0 = Mathf.Min(xi * tStep,       res - 1);
                    int hx1 = Mathf.Min((xi + 1) * tStep, res - 1);
                    int hz0 = Mathf.Min(zi * tStep,       res - 1);
                    int hz1 = Mathf.Min((zi + 1) * tStep, res - 1);

                    float th00 = h[hz0, hx0], th10 = h[hz0, hx1];
                    float th01 = h[hz1, hx0], th11 = h[hz1, hx1];

                    if (th00 >= seaNorm && th10 >= seaNorm &&
                        th01 >= seaNorm && th11 >= seaNorm) continue;

                    float twx0 = xi * tcw, twx1 = (xi + 1) * tcw;
                    float twz0 = zi * tcl, twz1 = (zi + 1) * tcl;

                    Color tc00 = DepthColor((seaNorm - Mathf.Min(th00, seaNorm)) * depthScale);
                    Color tc10 = DepthColor((seaNorm - Mathf.Min(th10, seaNorm)) * depthScale);
                    Color tc01 = DepthColor((seaNorm - Mathf.Min(th01, seaNorm)) * depthScale);
                    Color tc11 = DepthColor((seaNorm - Mathf.Min(th11, seaNorm)) * depthScale);

                    AddQuad(twx0, _seaLevel, twz0,  twx1, _seaLevel, twz0,
                            twx0, _seaLevel, twz1,  twx1, _seaLevel, twz1,
                            tc00, tc10, tc01, tc11, facingUp: true);
                }
            }

            // ── High-res seafloor — terrain footprint only ────────────────────
            // Rendered at up to BottomFaceSamples resolution so the seafloor
            // contours match the actual survey data rather than the coarser main
            // grid used for the top face and columns.
            if (_showSeafloor)
            {
                int   bStep = Mathf.Max(1, (res - 1) / BottomFaceSamples);
                int   bnx   = Mathf.CeilToInt((float)(res - 1) / bStep);
                float bcw   = _terrainManager.CellWidth  * bStep;
                float bcl   = _terrainManager.CellLength * bStep;

                for (int zi = 0; zi < bnx; zi++)
                for (int xi = 0; xi < bnx; xi++)
                {
                    int hx0 = Mathf.Min(xi * bStep,       res - 1);
                    int hx1 = Mathf.Min((xi + 1) * bStep, res - 1);
                    int hz0 = Mathf.Min(zi * bStep,       res - 1);
                    int hz1 = Mathf.Min((zi + 1) * bStep, res - 1);

                    float fh00 = h[hz0, hx0], fh10 = h[hz0, hx1];
                    float fh01 = h[hz1, hx0], fh11 = h[hz1, hx1];

                    if (fh00 >= seaNorm && fh10 >= seaNorm &&
                        fh01 >= seaNorm && fh11 >= seaNorm) continue;

                    float bwx0 = xi * bcw, bwx1 = (xi + 1) * bcw;
                    float bwz0 = zi * bcl, bwz1 = (zi + 1) * bcl;

                    float fby00 = Mathf.Min(fh00, seaNorm) * worldY;
                    float fby10 = Mathf.Min(fh10, seaNorm) * worldY;
                    float fby01 = Mathf.Min(fh01, seaNorm) * worldY;
                    float fby11 = Mathf.Min(fh11, seaNorm) * worldY;

                    Color bc00 = DepthColor((seaNorm - Mathf.Min(fh00, seaNorm)) * depthScale);
                    Color bc10 = DepthColor((seaNorm - Mathf.Min(fh10, seaNorm)) * depthScale);
                    Color bc01 = DepthColor((seaNorm - Mathf.Min(fh01, seaNorm)) * depthScale);
                    Color bc11 = DepthColor((seaNorm - Mathf.Min(fh11, seaNorm)) * depthScale);

                    AddQuad(bwx0, fby00 + zBias, bwz0,  bwx1, fby10 + zBias, bwz0,
                            bwx0, fby01 + zBias, bwz1,  bwx1, fby11 + zBias, bwz1,
                            bc00, bc10, bc01, bc11, facingUp: false);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetTriangles(_t, 0);
            _mesh.SetColors(_c);
            _mesh.RecalculateBounds();
            _mesh.RecalculateNormals();
            _meshRenderer.enabled = _v.Count > 0;

            // ── Terrain boundary — convex hull of the imported survey points ───
            float bY   = _seaLevel + Mathf.Max(1f, worldY * 0.002f);
            var hull   = _terrainManager.TerrainBoundary;
            if (hull != null && hull.Count >= 3)
            {
                _boundary.positionCount = hull.Count;
                for (int i = 0; i < hull.Count; i++)
                    _boundary.SetPosition(i, new Vector3(hull[i].x, bY, hull[i].y));
                _boundary.enabled = true;
            }
            else
            {
                // Fallback: simple rectangle if no hull available.
                float terrX = _terrainManager.CellWidth  * (res - 1);
                float terrZ = _terrainManager.CellLength * (res - 1);
                _boundary.positionCount = 4;
                _boundary.SetPosition(0, new Vector3(0,     bY, 0));
                _boundary.SetPosition(1, new Vector3(terrX, bY, 0));
                _boundary.SetPosition(2, new Vector3(terrX, bY, terrZ));
                _boundary.SetPosition(3, new Vector3(0,     bY, terrZ));
                _boundary.enabled = true;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Color DepthColor(float depthUnits)
        {
            Color c = SampleWater(_colorTableIndex, depthUnits);
            return new Color(c.r, c.g, c.b, c.a * _opacity);
        }

        private static bool HasWater(int xi, int zi, int step, int res, float seaNorm, float[,] h)
        {
            int hx0 = Mathf.Clamp(xi * step,       0, res - 1);
            int hx1 = Mathf.Clamp((xi + 1) * step, 0, res - 1);
            int hz0 = Mathf.Clamp(zi * step,       0, res - 1);
            int hz1 = Mathf.Clamp((zi + 1) * step, 0, res - 1);
            return h[hz0, hx0] < seaNorm || h[hz0, hx1] < seaNorm ||
                   h[hz1, hx0] < seaNorm || h[hz1, hx1] < seaNorm;
        }

        private void AddWall(Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr,
                             Color cb, Color ct)
        {
            int b = _v.Count;
            _v.Add(bl); _v.Add(br); _v.Add(tl); _v.Add(tr);
            _c.Add(cb); _c.Add(cb); _c.Add(ct); _c.Add(ct);
            _t.Add(b); _t.Add(b+2); _t.Add(b+1);
            _t.Add(b+1); _t.Add(b+2); _t.Add(b+3);
        }


        private void AddQuad(float x0, float y0, float z0, float x1, float y1, float z1,
                             float x2, float y2, float z2, float x3, float y3, float z3,
                             Color c0, Color c1, Color c2, Color c3, bool facingUp)
        {
            int b = _v.Count;
            _v.Add(new Vector3(x0,y0,z0)); _v.Add(new Vector3(x1,y1,z1));
            _v.Add(new Vector3(x2,y2,z2)); _v.Add(new Vector3(x3,y3,z3));
            _c.Add(c0); _c.Add(c1); _c.Add(c2); _c.Add(c3);
            if (facingUp)
            { _t.Add(b); _t.Add(b+2); _t.Add(b+1); _t.Add(b+1); _t.Add(b+2); _t.Add(b+3); }
            else
            { _t.Add(b); _t.Add(b+1); _t.Add(b+2); _t.Add(b+1); _t.Add(b+3); _t.Add(b+2); }
        }
    }
}
