using System.IO;
using UnityEngine;
using IslandBuilder.Domain;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandBuilder.Infrastructure
{
    [AddComponentMenu("Island Builder/Export Manager")]
    public class ExportManager : MonoBehaviour
    {
        private IExporter[]    _exporters;
        private ITerrainReader _reader;

        public void Bind(ITerrainReader reader)
        {
            _reader    = reader;
            _exporters = new IExporter[] { new ObjExporter(reader) };
        }

        public void Export(ExportOptions opts)
        {
            if (_reader == null || _reader.Resolution <= 1)
            {
                Debug.LogWarning("[ExportManager] No terrain loaded — nothing to export.");
                return;
            }

            string path = ResolvePath(opts);
            if (string.IsNullOrEmpty(path)) return;

            foreach (var exp in _exporters)
            {
                if (exp.SupportedType != opts.Type) continue;
                bool ok = exp.Export(path, opts);
                Debug.Log(ok
                    ? $"[ExportManager] Exported to: {path}"
                    : $"[ExportManager] Export failed: {path}");
                return;
            }

            Debug.LogWarning($"[ExportManager] No exporter registered for {opts.Type}.");
        }

        private static string ResolvePath(ExportOptions opts)
        {
#if UNITY_EDITOR
            string ext  = opts.Type == ExportType.Obj ? "obj" : "png";
            string name = opts.Type == ExportType.Obj ? "terrain" : "terrain_map";
            return EditorUtility.SaveFilePanel("Export Terrain", "", name, ext);
#else
            string ext = opts.Type == ExportType.Obj ? ".obj" : ".png";
            return Path.Combine(Application.persistentDataPath, "terrain" + ext);
#endif
        }
    }
}
