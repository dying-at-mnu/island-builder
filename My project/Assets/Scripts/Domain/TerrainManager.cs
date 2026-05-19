using System.Collections.Generic;
using UnityEngine;
using IslandBuilder.Infrastructure;

namespace IslandBuilder.Domain
{
    [AddComponentMenu("Island Builder/Terrain Manager")]
    public class TerrainManager : MonoBehaviour, ITerrainReader, ITerrainWriter
    {
        [SerializeField] private float _defaultSeaLevel = 0f;

        // Imported terrain occupies the bottom 1/HeadroomFactor of the normalised
        // height range so the user can build sand far above the original terrain.
        private const float HeadroomFactor = 4f;

        /// <summary>
        /// Multiplies the terrain's display height after the minimum-scale floor is
        /// applied. Values above 1 exaggerate vertical relief; 1 = no exaggeration.
        /// Tweak in the Inspector after importing to taste — re-import to apply.
        /// </summary>
        [SerializeField, Range(1f, 200f)] private float _verticalExaggeration = 20f;

        private Terrain      _terrain;
        private TerrainData  _terrainData;
        private float[,]     _baseline;
        private float        _seaLevelOffset;
        private TerrainLayer _defaultLayer;
        private Texture2D    _defaultLayerTex;
        private int          _terrainColorTable = 0;
        private float        _colorUnitScale    = 1f; // metres per display unit, kept in sync with WaterRenderer
        private Vector2[]    _terrainBoundary;

        /// <summary>Convex hull of the imported survey points in world XZ metres, or null if not yet imported.</summary>
        public IReadOnlyList<Vector2> TerrainBoundary => _terrainBoundary;

        // Stored at import time for live exaggeration updates.
        private float _baseDisplayY      = 0f; // terrain height before exaggeration
        private float _seaLevelFraction  = 0f; // seaLevel / worldSize.y at import time
        private float _realHeightY       = 0f; // actual survey elevation range in metres

        // Default flat terrain shown before any file is imported.
        private const int   DefaultResolution = 513;
        private const float DefaultSize       = 1000f; // metres

        public event TerrainEvents.TerrainHeightsChangedHandler TerrainHeightsChanged;

        // ── ITerrainReader ────────────────────────────────────────────────────

        public Vector3 WorldSize           => _terrainData != null ? _terrainData.size : Vector3.zero;
        public int     Resolution          => _terrainData != null ? _terrainData.heightmapResolution : 0;
        public float   SeaLevelOffset      => _seaLevelOffset;
        public float   VerticalExaggeration => Mathf.Max(1f, _verticalExaggeration);
        public float   CellWidth           => Resolution > 1 ? WorldSize.x / (Resolution - 1) : 0f;
        /// <summary>Actual survey elevation range in metres, unaffected by display exaggeration.</summary>
        public float   RealHeightY         => _realHeightY > 0f ? _realHeightY : WorldSize.y / Mathf.Max(1f, VerticalExaggeration);
        public float   CellLength          => Resolution > 1 ? WorldSize.z / (Resolution - 1) : 0f;

        public float[,] GetHeights(RectInt region)
        {
            if (_terrainData == null) return new float[0, 0];
            return _terrainData.GetHeights(region.x, region.y, region.width, region.height);
        }

        // ── ITerrainWriter ────────────────────────────────────────────────────

        /// <summary>
        /// When true (default), SetHeights clamps every cell to its imported baseline value
        /// so no tool can lower terrain below the original survey data.
        /// </summary>
        public bool EnforceBaseline { get; set; } = true;

        public void SetHeights(float[,] patch, Vector2Int origin)
        {
            if (_terrainData == null) return;
            int rows = patch.GetLength(0), cols = patch.GetLength(1);
            if (EnforceBaseline && _baseline != null)
            {
                // Clone so the caller's array (e.g. an undo entry) is never mutated.
                var clamped = (float[,])patch.Clone();
                for (int z = 0; z < rows; z++)
                    for (int x = 0; x < cols; x++)
                        clamped[z, x] = Mathf.Max(clamped[z, x], _baseline[origin.y + z, origin.x + x]);
                _terrainData.SetHeights(origin.x, origin.y, clamped);
            }
            else
            {
                _terrainData.SetHeights(origin.x, origin.y, patch);
            }
            TerrainHeightsChanged?.Invoke(new RectInt(origin.x, origin.y, cols, rows));
        }

        public float[,] GetBaseline(RectInt region)
        {
            if (_baseline == null) return new float[region.height, region.width];
            var result = new float[region.height, region.width];
            for (int z = 0; z < region.height; z++)
                for (int x = 0; x < region.width; x++)
                    result[z, x] = _baseline[region.y + z, region.x + x];
            return result;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Exposes underlying TerrainData so ImportManager can pass it to ImportCompleted.</summary>
        public TerrainData TerrainData => _terrainData;

        /// <summary>
        /// Writes a pre-composited heightmap directly to terrain data without baseline
        /// clamping. Used by LayerManager during recomposition.
        /// </summary>
        /// <summary>Writes a full composited heightmap. Used by LayerManager.</summary>
        public void DirectSetHeights(float[,] heights)
            => DirectSetHeights(heights, new RectInt(0, 0, Resolution, Resolution));

        /// <summary>Writes a composited patch at <paramref name="region"/>. Used by LayerManager.</summary>
        public void DirectSetHeights(float[,] heights, RectInt region)
        {
            if (_terrainData == null) return;
            _terrainData.SetHeights(region.x, region.y, heights);
            _terrain?.Flush();
            TerrainHeightsChanged?.Invoke(region);
        }

        /// <summary>
        /// Writes the stored baseline back to the terrain, discarding all user edits.
        /// Fires TerrainHeightsChanged for the full terrain so listeners (VolumeCalculator,
        /// SandHighlightRenderer, etc.) update immediately.
        /// </summary>
        public void ResetToBaseline()
        {
            if (_terrainData == null || _baseline == null) return;
            _terrainData.SetHeights(0, 0, _baseline);
            TerrainHeightsChanged?.Invoke(new RectInt(0, 0, Resolution, Resolution));
        }

        public void SetSeaLevel(float metres)
        {
            _seaLevelOffset = metres;
        }

        /// <summary>
        /// Updates vertical exaggeration live (no re-import needed).
        /// Resizes the terrain's Y axis and returns the new display sea level.
        /// </summary>
        public float SetVerticalExaggeration(float exaggeration)
        {
            _verticalExaggeration = Mathf.Clamp(exaggeration, 1f, 200f);
            if (_terrainData == null || _baseDisplayY <= 0f) return 0f;

            float newY       = _baseDisplayY * Mathf.Max(1f, _verticalExaggeration) * HeadroomFactor;
            float newSeaLvl  = _seaLevelFraction * _baseDisplayY * Mathf.Max(1f, _verticalExaggeration);
            _terrainData.size = new Vector3(_terrainData.size.x, newY, _terrainData.size.z);
            _seaLevelOffset  = newSeaLvl;
            _terrain?.Flush();

            return newSeaLvl;
        }

        /// <summary>
        /// Creates or reconfigures the Unity Terrain from an ImportResult.
        /// Called by ImportManager after a successful parse. Stores a baseline
        /// copy of the heights (never modified) and fires TerrainHeightsChanged
        /// for the full terrain so downstream listeners initialise correctly.
        /// </summary>
        /// <returns>
        /// The sea level Y position in display-space metres. Callers should pass
        /// this value to WaterRenderer rather than using result.SeaLevel directly,
        /// because the display Y may be scaled up to ensure visible terrain relief.
        /// </returns>
        public float ApplyImportResult(ImportResult result)
        {
            EnsureTerrain();

            if (_terrainData != null)
                Destroy(_terrainData);

            _terrainData = new TerrainData();
            _terrainData.heightmapResolution = result.Resolution;

            // 1. Ensure a minimum display height (2 % of horizontal) so relief is
            //    visible regardless of the data's unit system.
            // 2. Apply vertical exaggeration on top of that floor.
            float minDisplayY = Mathf.Max(result.WorldSize.x, result.WorldSize.z) * 0.02f;
            float baseDisplayY = Mathf.Max(result.WorldSize.y, minDisplayY);
            float exaggeratedY = baseDisplayY * Mathf.Max(1f, _verticalExaggeration);
            // Multiply terrain Y by HeadroomFactor and divide stored heights by the
            // same amount so the terrain surface sits at the same world position but
            // the user has HeadroomFactor times the original range to build upward.
            float displayY  = exaggeratedY * HeadroomFactor;
            Vector3 displaySize = new Vector3(result.WorldSize.x, displayY, result.WorldSize.z);

            Debug.Log($"[TerrainManager] Vertical: real {result.WorldSize.y:F1} m → " +
                      $"base {baseDisplayY:F1} m → ×{_verticalExaggeration:F1} exaggeration " +
                      $"= display {exaggeratedY:F1} m (×{HeadroomFactor} headroom = {displayY:F1} m total).");

            _terrainData.size = displaySize;

            // Scale imported heights to [0, 1/HeadroomFactor] so world positions are
            // unchanged: h_scaled * displayY = h_original * exaggeratedY.
            int res = result.Resolution;
            var scaledHeights = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    scaledHeights[z, x] = result.Heights[z, x] / HeadroomFactor;
            _terrainData.SetHeights(0, 0, scaledHeights);

            _terrain.terrainData = _terrainData;
            var col = _terrain.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = _terrainData;

            ApplyDefaultLayer(_terrainData);
            RebuildTerrainColorTexture(_terrainData);

            _terrainBoundary = result.TerrainBoundary;

            _baseline        = scaledHeights;
            _seaLevelOffset  = _defaultSeaLevel;
            _baseDisplayY    = baseDisplayY;
            _realHeightY     = result.WorldSize.y;
            // Fraction stores seaLevel as a fraction of exaggeratedY (without headroom)
            // so SetVerticalExaggeration can recompute correctly.
            _seaLevelFraction = (result.WorldSize.y > 0f) ? result.SeaLevel / result.WorldSize.y : 0f;

            // Sea-level world Y: seaLevel_metres * exaggeratedY / worldRange
            // (HeadroomFactor cancels: scaledH * displayY = h * exaggeratedY).
            float displaySeaLevel = result.WorldSize.y > 0f
                ? result.SeaLevel * exaggeratedY / result.WorldSize.y
                : result.SeaLevel;

            // Diagnostic: log actual height range so misconfigurations are easy to spot.
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int z = 0; z < result.Resolution; z++)
                for (int x = 0; x < result.Resolution; x++)
                {
                    float h = result.Heights[z, x];
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            Debug.Log($"[TerrainManager] Import applied — " +
                      $"{result.Resolution}×{result.Resolution}, " +
                      $"display size {displaySize.x:F0}×{displaySize.z:F0}×{displaySize.y:F1} m, " +
                      $"heights {minH:F4}–{maxH:F4}, display sea level {displaySeaLevel:F1} m.");

            _terrain.Flush();

            TerrainHeightsChanged?.Invoke(
                new RectInt(0, 0, result.Resolution, result.Resolution));

            return displaySeaLevel;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            CreateDefaultTerrain();
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void CreateDefaultTerrain()
        {
            // Build TerrainData first, then let Unity create the Terrain + TerrainCollider
            // pair via the official factory method. Doing AddComponent<Terrain>() without
            // pre-assigned TerrainData causes a MissingComponentException internally.
            _terrainData = new TerrainData();
            _terrainData.heightmapResolution = DefaultResolution;
            _terrainData.size = new Vector3(DefaultSize, DefaultSize * 0.1f, DefaultSize);

            var go = Terrain.CreateTerrainGameObject(_terrainData);
            go.transform.SetParent(transform);
            go.name = "Terrain";

            _terrain  = go.GetComponent<Terrain>();
            _baseline = new float[DefaultResolution, DefaultResolution];
            ApplyDefaultLayer(_terrainData);
        }

        private void EnsureTerrain()
        {
            if (_terrain != null) return;
            CreateDefaultTerrain();
        }

        public void SetTerrainColorTable(int index)
        {
            _terrainColorTable = Mathf.Clamp(index, 0, ColorTables.CombinedTerrainNames.Length - 1);
            if (_terrainData != null) RebuildTerrainColorTexture(_terrainData);
        }

        /// <summary>Keeps the depth colour scale consistent with the water renderer's unit scale.</summary>
        public void SetColorUnitScale(float metresPerUnit)
        {
            _colorUnitScale = Mathf.Max(1e-9f, metresPerUnit);
            if (_terrainData != null) RebuildTerrainColorTexture(_terrainData);
        }

        /// <summary>Generates a height-tinted texture and applies it to the terrain layer.</summary>
        private void RebuildTerrainColorTexture(TerrainData td)
        {
            if (td == null || Resolution <= 1) return;
            int res = Resolution;
            int texSize = Mathf.Min(res, 256);

            float[,] h = td.GetHeights(0, 0, res, res);
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++)
            { if (h[z,x] < minH) minH = h[z,x]; if (h[z,x] > maxH) maxH = h[z,x]; }
            float range = Mathf.Max(1e-5f, maxH - minH);

            // Depth below sea level in display units, used when a water colour table is selected.
            // worldY / VE removes vertical exaggeration so the colour stops match real metres.
            float worldY     = td.size.y;
            float vExag      = Mathf.Max(1f, VerticalExaggeration);
            float seaNorm    = worldY > 0f ? _seaLevelOffset / worldY : 0f;
            float depthScale = worldY;

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGB24, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            for (int py = 0; py < texSize; py++)
            for (int px = 0; px < texSize; px++)
            {
                int   hx    = Mathf.Clamp(Mathf.RoundToInt((float)px/(texSize-1)*(res-1)), 0, res-1);
                int   hz    = Mathf.Clamp(Mathf.RoundToInt((float)py/(texSize-1)*(res-1)), 0, res-1);
                float hval  = h[hz, hx];
                float t     = (hval - minH) / range;
                float depth = (seaNorm - hval) * depthScale; // positive = below sea level
                tex.SetPixel(px, py, ColorTables.SampleTerrainCombined(_terrainColorTable, t, depth));
            }
            tex.Apply(false);

            // Swap into the existing layer without recreating it.
            if (_defaultLayerTex != null) Destroy(_defaultLayerTex);
            _defaultLayerTex = tex;
            if (_defaultLayer != null)
            {
                _defaultLayer.diffuseTexture = tex;
                td.terrainLayers = new[] { _defaultLayer };
            }
        }

        /// <summary>
        /// Assigns a single sand-coloured TerrainLayer to the given TerrainData so
        /// Unity (URP) does not fall back to its checkerboard no-texture debug pattern.
        /// </summary>
        private void ApplyDefaultLayer(TerrainData td)
        {
            // Destroy previous layer assets to avoid leaking on re-import.
            if (_defaultLayer    != null) Destroy(_defaultLayer);
            if (_defaultLayerTex != null) Destroy(_defaultLayerTex);

            _defaultLayerTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var sandColor    = Color.white;
            var pixels       = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = sandColor;
            _defaultLayerTex.SetPixels(pixels);
            _defaultLayerTex.Apply();

            _defaultLayer = new TerrainLayer
            {
                diffuseTexture = _defaultLayerTex,
                // Tile once across the whole terrain so it reads as a solid colour.
                tileSize       = new Vector2(td.size.x > 0 ? td.size.x : DefaultSize,
                                             td.size.z > 0 ? td.size.z : DefaultSize)
            };

            td.terrainLayers = new[] { _defaultLayer };
        }
    }
}
