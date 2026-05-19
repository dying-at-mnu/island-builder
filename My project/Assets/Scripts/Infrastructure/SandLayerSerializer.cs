using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;
using IslandBuilder.Rendering;
using IslandBuilder.Presentation;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Saves and loads sand edits plus app settings (grid, terrain scale, water
    /// opacity, measurement lines) to a .sandlyr file.
    ///
    /// v1: delta heights only.
    /// v2: adds grid settings, terrain settings, and measurement lines.
    ///     Loading any file always resets transient state (lasso, existing lines, etc.).
    /// </summary>
    [AddComponentMenu("Island Builder/Sand Layer Serializer")]
    public class SandLayerSerializer : MonoBehaviour
    {
        private static readonly byte[] Magic   = { (byte)'S', (byte)'N', (byte)'D', (byte)'L' };
        private const           int    Version = 2;
        private const           string Ext     = "sandlyr";

        private ITerrainReader   _reader;
        private ITerrainWriter   _writer;
        private MeasureTool      _measureTool;
        private GridRenderer     _gridRenderer;
        private LassoTool        _lassoTool;
        private ToolParameterGUI _paramGui;

        public void Bind(TerrainManager terrainManager)
        {
            _reader = terrainManager;
            _writer = terrainManager;
        }

        public void BindAppComponents(MeasureTool mt, GridRenderer gr,
                                      LassoTool lt, ToolParameterGUI pg)
        {
            _measureTool  = mt;
            _gridRenderer = gr;
            _lassoTool    = lt;
            _paramGui     = pg;
        }

        // ── Save ──────────────────────────────────────────────────────────────

        public void Save()
        {
            if (_reader == null) return;
            string path = PickSavePath();
            if (string.IsNullOrEmpty(path)) return;

            int res = _reader.Resolution;
            if (res <= 1) { Debug.LogWarning("[SandLayerSerializer] No terrain loaded."); return; }

            var region   = new RectInt(0, 0, res, res);
            float[,] h   = _reader.GetHeights(region);
            float[,] bas = _writer.GetBaseline(region);

            try
            {
                using var bw = new BinaryWriter(File.Open(path, FileMode.Create));
                foreach (byte b in Magic) bw.Write(b);
                bw.Write(Version);
                bw.Write(res);
                bw.Write(_reader.WorldSize.x);
                bw.Write(_reader.WorldSize.z);

                // Delta heights
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        bw.Write(Mathf.Max(0f, h[z, x] - bas[z, x]));

                // ── v2: grid settings ──────────────────────────────────────────
                bw.Write(_gridRenderer?.IsActive     ?? false);
                bw.Write(_gridRenderer?.SpacingMetres ?? 1f);

                // ── v2: terrain / appearance settings ─────────────────────────
                if (_paramGui != null)
                {
                    ToolParameterGUI.TerrainSnapshot ts = _paramGui.GetTerrainSettings();
                    bw.Write(ts.WaterOpacity);
                    bw.Write(ts.TerrainScale);
                    bw.Write(ts.UnitName ?? "Metres");
                    bw.Write(ts.SelectedUnit);
                    bw.Write(ts.WaterColorTable);
                    bw.Write(ts.TerrainColorTable);
                }
                else
                {
                    bw.Write(1f);  bw.Write(1f);
                    bw.Write("Metres"); bw.Write(0);
                    bw.Write(0);   bw.Write(0); // colour table defaults
                }

                // ── v2: measurement lines ─────────────────────────────────────
                var lines = _measureTool?.Lines
                    ?? (IReadOnlyList<MeasureLine>)System.Array.Empty<MeasureLine>();
                bw.Write(lines.Count);
                foreach (var ln in lines)
                {
                    bw.Write(ln.Start.x); bw.Write(ln.Start.y); bw.Write(ln.Start.z);
                    bw.Write(ln.End.x);   bw.Write(ln.End.y);   bw.Write(ln.End.z);
                }

                Debug.Log($"[SandLayerSerializer] Saved v{Version} → {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SandLayerSerializer] Save failed: {e.Message}");
            }
        }

        // ── Load ──────────────────────────────────────────────────────────────

        public void Load()
        {
            if (_reader == null || _writer == null) return;
            string path = PickLoadPath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using var br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read));

                for (int i = 0; i < Magic.Length; i++)
                    if (br.ReadByte() != Magic[i])
                        throw new InvalidDataException("Not a valid .sandlyr file.");

                int version = br.ReadInt32();
                if (version < 1 || version > Version)
                    throw new InvalidDataException($"Unsupported file version {version}.");

                int savedRes = br.ReadInt32();
                br.ReadSingle(); // reserved: savedX
                br.ReadSingle(); // reserved: savedZ

                var delta = new float[savedRes, savedRes];
                for (int z = 0; z < savedRes; z++)
                    for (int x = 0; x < savedRes; x++)
                        delta[z, x] = br.ReadSingle();

                // Always reset transient state before applying loaded data.
                _lassoTool?.ClearSelection();
                _measureTool?.ClearLines();

                ApplyDelta(delta, savedRes);

                if (version >= 2)
                {
                    // Grid
                    bool  gridActive   = br.ReadBoolean();
                    float gridSpacingM = br.ReadSingle();
                    _gridRenderer?.SetSpacingMetres(gridSpacingM);
                    _gridRenderer?.SetActive(gridActive);

                    // Terrain / appearance
                    float  opacity     = br.ReadSingle();
                    float  tScale      = br.ReadSingle();
                    string unitName    = br.ReadString();
                    int    selUnit     = br.ReadInt32();
                    // Colour tables (added after initial v2; try-catch keeps old files loadable)
                    int waterCT = 0, terrainCT = 0;
                    try { waterCT = br.ReadInt32(); terrainCT = br.ReadInt32(); } catch { }
                    _paramGui?.ApplyTerrainSettings(opacity, tScale, unitName, selUnit,
                                                    waterCT, terrainCT);

                    // Measurement lines
                    int lineCount = br.ReadInt32();
                    var loaded    = new List<MeasureLine>(lineCount);
                    for (int i = 0; i < lineCount; i++)
                    {
                        var s = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        var e = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        loaded.Add(new MeasureLine(s, e));
                    }
                    _measureTool?.SetLines(loaded);
                }
                else
                {
                    // v1 file: reset everything to defaults.
                    _gridRenderer?.SetActive(false);
                    _paramGui?.ApplyTerrainSettings(1f, 1f, "Metres", 0);
                }

                Debug.Log($"[SandLayerSerializer] Loaded v{version} from {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SandLayerSerializer] Load failed: {e.Message}");
            }
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void ApplyDelta(float[,] delta, int savedRes)
        {
            int currRes = _reader.Resolution;
            if (currRes <= 1) { Debug.LogWarning("[SandLayerSerializer] No terrain loaded."); return; }

            var region   = new RectInt(0, 0, currRes, currRes);
            float[,] bas = _writer.GetBaseline(region);
            var newH     = new float[currRes, currRes];

            for (int z = 0; z < currRes; z++)
            for (int x = 0; x < currRes; x++)
            {
                float d = savedRes > 1
                    ? SampleBilinear(delta, savedRes,
                        (float)z / (currRes - 1) * (savedRes - 1),
                        (float)x / (currRes - 1) * (savedRes - 1))
                    : delta[0, 0];
                newH[z, x] = Mathf.Clamp01(bas[z, x] + Mathf.Max(0f, d));
            }

            _writer.SetHeights(newH, Vector2Int.zero);
        }

        private static float SampleBilinear(float[,] data, int res, float fz, float fx)
        {
            int z0 = Mathf.Clamp((int)fz, 0, res - 1);
            int x0 = Mathf.Clamp((int)fx, 0, res - 1);
            int z1 = Mathf.Min(z0 + 1, res - 1);
            int x1 = Mathf.Min(x0 + 1, res - 1);
            float tz = fz - z0, tx = fx - x0;
            return Mathf.Lerp(
                Mathf.Lerp(data[z0, x0], data[z0, x1], tx),
                Mathf.Lerp(data[z1, x0], data[z1, x1], tx), tz);
        }

        private static string PickSavePath()
        {
#if UNITY_EDITOR
            return EditorUtility.SaveFilePanel("Save Sand Layer", "", "sandlayer", Ext);
#else
            return Path.Combine(Application.persistentDataPath, "sandlayer." + Ext);
#endif
        }

        private static string PickLoadPath()
        {
#if UNITY_EDITOR
            return EditorUtility.OpenFilePanel("Load Sand Layer", "", Ext);
#else
            return Path.Combine(Application.persistentDataPath, "sandlayer." + Ext);
#endif
        }
    }
}
