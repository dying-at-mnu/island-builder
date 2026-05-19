using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using IslandBuilder.Domain;
using IslandBuilder.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Orchestrates the import pipeline (SDD §3.3).
    ///
    /// Flow:
    ///   1. File picker returns an absolute path (OpenAndImport / ImportFromPath).
    ///   2. ImportManager selects the IImporter by file extension.
    ///   3. IImporter.Import() returns an ImportResult with a normalised float[,] heights.
    ///   4. TerrainManager.ApplyImportResult() sets the Unity Terrain state.
    ///   5. ImportCompleted is fired so CameraController and WaterRenderer can react.
    ///
    /// Runtime file picker: not yet implemented outside the Unity Editor.
    /// Replace the UNITY_EDITOR block with NativeFilePicker when that plugin is added.
    /// </summary>
    [AddComponentMenu("Island Builder/Import Manager")]
    public class ImportManager : MonoBehaviour
    {
        [SerializeField] private TerrainManager _terrainManager;
        [SerializeField] private WaterRenderer  _waterRenderer;


        /// <summary>Fired after TerrainManager has been updated with the new terrain.</summary>
        public event TerrainEvents.ImportCompletedHandler ImportCompleted;

        /// <summary>
        /// Progress during import: 0–1 while loading, -1 when complete (caller should hide bar).
        /// Always fired on the Unity main thread.
        /// </summary>
        public event System.Action<float> ImportProgress;

        /// <summary>
        /// Fired on the main thread when a file carries no unit information.
        /// Subscribers must call the provided callback with a metres-per-unit scale
        /// factor so the import can proceed.
        /// </summary>
        public event System.Action<string, System.Action<float>> ScaleNeeded;

        /// <summary>Unit name and scale from the most recent successful import.</summary>
        public string LastDetectedUnit    { get; private set; } = "Metres";
        public float  LastUnitScaleFactor { get; private set; } = 1f;

        /// <summary>Contour polylines from the most recent DXF import, or empty if none.</summary>
        public ContourPolyline[] LastContourLines    { get; private set; } = System.Array.Empty<ContourPolyline>();

        /// <summary>Raw Z-value difference between adjacent contour lines in the file's native units. 0 if not detected.</summary>
        public float LastContourInterval { get; private set; } = 0f;

        private IImporter[] _importers;

        private void Awake()
        {
            _importers = new IImporter[]
            {
                new DxfImporter(),
                new ObjImporter()
            };
        }

        /// <summary>
        /// Opens a platform file picker then imports the chosen file.
        /// In the Unity Editor this uses EditorUtility.OpenFilePanel.
        /// Also exposed as a right-click context menu item on the component.
        /// </summary>
        [ContextMenu("Open and Import")]
        public void OpenAndImport()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Import Terrain", "", "dxf,dwg,obj");
            if (!string.IsNullOrEmpty(path))
                ImportFromPath(path);
#else
            Debug.LogWarning("[ImportManager] Runtime file picker is not implemented. " +
                             "Call ImportFromPath(absolutePath) directly from UI, or integrate the NativeFilePicker plugin.");
#endif
        }

        /// <summary>
        /// Imports a terrain file from an absolute path. Runs the parser on a background
        /// thread and applies results on the main thread; fires ImportProgress throughout.
        /// </summary>
        public async void ImportFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"[ImportManager] File not found: {path}");
                return;
            }

            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            // For binary DWG files, attempt an automatic DWG→DXF conversion via
            // the free ODA File Converter before handing off to the DXF importer.
            string tempDxf = null;
            if (ext == "dwg" && DwgConverter.IsBinaryDwg(path))
            {
                ImportProgress?.Invoke(0.02f);
                tempDxf = DwgConverter.TryConvertToDxf(path);
                if (tempDxf != null)
                {
                    path = tempDxf;
                    ext  = "dxf";
                }
                // If conversion failed, continue with dwg — DxfImporter will show
                // a clear error explaining the ODA converter is not installed.
            }

            IImporter importer = FindImporter(ext);
            if (importer == null)
            {
                Debug.LogError($"[ImportManager] No importer registered for '.{ext}'. Supported: dxf, obj");
                if (tempDxf != null) TryDeleteTempFile(tempDxf);
                return;
            }

            if (_terrainManager == null)
            {
                Debug.LogError("[ImportManager] TerrainManager reference is not assigned.");
                return;
            }

            ImportProgress?.Invoke(0.05f);

            ImportResult result;
            try
            {
                // File parsing is CPU-bound; run it off the main thread.
                result = await Task.Run(() => importer.Import(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ImportManager] Import exception: {e.Message}");
                ImportProgress?.Invoke(-1f);
                TryDeleteTempFile(tempDxf);
                return;
            }

            if (!result.Success)
            {
                Debug.LogError($"[ImportManager] Import failed: {result.Error}");
                ImportProgress?.Invoke(-1f);
                TryDeleteTempFile(tempDxf);
                return;
            }

            ImportProgress?.Invoke(0.80f);

            // Ask the user for a scale factor when the file has no unit header.
            float finalUnitScale = result.AppliedUnitScale;
            if (result.NeedsScaleConfirmation && ScaleNeeded != null)
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<float>();
                ScaleNeeded.Invoke(result.DetectedUnit, scale => tcs.SetResult(scale));
                float userScale = await tcs.Task;
                if (userScale > 0f)
                {
                    finalUnitScale   = userScale;
                    result.WorldSize *= userScale;
                    result.SeaLevel  *= userScale;
                }
            }

            LastDetectedUnit    = result.DetectedUnit;
            LastUnitScaleFactor = Mathf.Max(1e-9f, finalUnitScale);
            LastContourLines    = result.ContourLines ?? System.Array.Empty<ContourPolyline>();
            LastContourInterval = result.ContourInterval;

            float displaySeaLevel = _terrainManager.ApplyImportResult(result);
            _terrainManager.SetSeaLevel(displaySeaLevel);
            _waterRenderer?.SetSeaLevel(displaySeaLevel);
            ImportCompleted?.Invoke(_terrainManager.TerrainData, displaySeaLevel);

            Debug.Log($"[ImportManager] Imported '{Path.GetFileName(path)}' — " +
                      $"{result.Resolution}×{result.Resolution}, " +
                      $"world {result.WorldSize.x:F0} × {result.WorldSize.z:F0} m.");

            ImportProgress?.Invoke(1.0f);
            await Task.Delay(500);
            ImportProgress?.Invoke(-1f);
            TryDeleteTempFile(tempDxf);
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }

        private IImporter FindImporter(string extension)
        {
            foreach (var imp in _importers)
                if (imp.SupportedExtension == extension)
                    return imp;
            // Route .dwg through the DXF importer so it can surface a clear
            // "convert to DXF first" message rather than "no importer registered".
            if (extension == "dwg")
                foreach (var imp in _importers)
                    if (imp.SupportedExtension == "dxf")
                        return imp;
            return null;
        }
    }
}
