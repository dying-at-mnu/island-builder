using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;
using IslandBuilder.Interaction;
using IslandBuilder.Presentation;
using IslandBuilder.Rendering;

namespace IslandBuilder.Editor
{
    /// <summary>
    /// One-time scene setup. Run via the menu: Island Builder ▶ Setup Scene
    ///
    /// Creates all required GameObjects as real scene objects so their Inspector
    /// values (scale, sea level, etc.) persist between Play mode sessions.
    /// The SceneBootstrapper is kept for wiring only — it finds these objects
    /// via FindFirstObjectByType and does not recreate them.
    ///
    /// Safe to re-run: existing objects and assets are reused, not duplicated.
    /// </summary>
    public static class IslandBuilderSetup
    {
        private const string PanelSettingsPath = "Assets/Settings/IslandBuilderUI.asset";

        [MenuItem("Island Builder/Setup Scene")]
        public static void SetupScene()
        {
            // ── Scene objects ─────────────────────────────────────────────────
            var terrainManager = EnsureSceneComponent<TerrainManager>("TerrainManager");
            var importManager  = EnsureSceneComponent<ImportManager>("ImportManager");
            var waterRenderer  = EnsureSceneComponent<WaterRenderer>("WaterRenderer");
            var uiManager      = EnsureSceneComponent<UIManager>("UIManager");

            // CameraController lives on the Main Camera.
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                mainCam = go.AddComponent<Camera>();
                Undo.RegisterCreatedObjectUndo(go, "Create Main Camera");
            }
            var cameraController = mainCam.GetComponent<CameraController>()
                                ?? AddWithUndo<CameraController>(mainCam.gameObject);
            var sculptController = mainCam.GetComponent<SculptController>()
                                ?? AddWithUndo<SculptController>(mainCam.gameObject);

            var toolRegistry = EnsureSceneComponent<ToolRegistry>("ToolRegistry");
            var undoManager  = EnsureSceneComponent<UndoManager>("UndoManager");
            var brushPreview = EnsureSceneComponent<BrushPreview>("BrushPreview");

            // ── Wire serialized references ────────────────────────────────────
            SetRef(importManager,    "_terrainManager", terrainManager);
            SetRef(importManager,    "_waterRenderer",  waterRenderer);
            SetRef(cameraController, "_importManager",  importManager);
            SetRef(waterRenderer,    "_importManager",  importManager);
            SetRef(uiManager,        "_terrainManager", terrainManager);
            SetRef(uiManager,        "_waterRenderer",  waterRenderer);
            SetRef(uiManager,        "_toolRegistry",   toolRegistry);
            SetRef(sculptController, "_terrainManager", terrainManager);
            SetRef(sculptController, "_toolRegistry",   toolRegistry);
            SetRef(sculptController, "_undoManager",    undoManager);
            SetRef(sculptController, "_brushPreview",   brushPreview);

            // ── PanelSettings asset for UI Toolkit ────────────────────────────
            var panelSettings = EnsurePanelSettings();
            if (panelSettings != null)
                SetRef(uiManager, "_panelSettings", panelSettings);

            // ── Save ──────────────────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log("[IslandBuilderSetup] Scene setup complete. " +
                      "Save the scene (Ctrl+S) to persist the objects.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static T EnsureSceneComponent<T>(string goName) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject(goName);
            Undo.RegisterCreatedObjectUndo(go, $"Create {goName}");
            return go.AddComponent<T>();
        }

        private static T AddWithUndo<T>(GameObject go) where T : Component
        {
            return Undo.AddComponent<T>(go);
        }

        private static void SetRef(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning($"[IslandBuilderSetup] Field '{fieldName}' not found on {target.GetType().Name}.");
            }
        }

        private static PanelSettings EnsurePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return existing;

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode          = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.fallbackDpi        = 96;

            // Search for Unity's built-in runtime theme.
            foreach (var guid in AssetDatabase.FindAssets("UnityDefaultRuntimeTheme"))
            {
                var path  = AssetDatabase.GUIDToAssetPath(guid);
                var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
                if (theme != null) { ps.themeStyleSheet = theme; break; }
            }

            System.IO.Directory.CreateDirectory("Assets/Settings");
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return ps;
        }
    }
}
