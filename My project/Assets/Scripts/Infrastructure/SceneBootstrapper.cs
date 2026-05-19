using UnityEngine;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;
using IslandBuilder.Interaction;
using IslandBuilder.Presentation;
using IslandBuilder.Rendering;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Creates and wires all required Island Builder components at runtime.
    /// Runs automatically after the scene loads — no manual scene setup required.
    /// Skips creation of any component that already exists in the scene.
    /// </summary>
    internal static class SceneBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialise()
        {
            TerrainManager   terrainManager   = EnsureComponent<TerrainManager>("TerrainManager");
            LayerManager     layerManager     = EnsureComponent<LayerManager>("LayerManager");
            ImportManager    importManager    = EnsureComponent<ImportManager>("ImportManager");
            WaterRenderer    waterRenderer    = EnsureComponent<WaterRenderer>("WaterRenderer");
            ToolRegistry     toolRegistry     = EnsureComponent<ToolRegistry>("ToolRegistry");
            UndoManager      undoManager      = EnsureComponent<UndoManager>("UndoManager");
            VolumeCalculator      volumeCalculator = EnsureComponent<VolumeCalculator>("VolumeCalculator");
            CostEstimator         costEstimator    = EnsureComponent<CostEstimator>("CostEstimator");
            ExportManager         exportManager    = EnsureComponent<ExportManager>("ExportManager");
            SandHighlightRenderer   sandHighlight      = EnsureComponent<SandHighlightRenderer>("SandHighlight");
            TerrainExtensionRenderer terrainExtension  = EnsureComponent<TerrainExtensionRenderer>("TerrainExtension");
            SandLayerSerializer   sandLayerSerializer = EnsureComponent<SandLayerSerializer>("SandLayerSerializer");
            GridRenderer          gridRenderer        = EnsureComponent<GridRenderer>("GridRenderer");

            // Wire ImportManager → TerrainManager + WaterRenderer
            SetPrivateField(importManager, "_terrainManager", terrainManager);
            SetPrivateField(importManager, "_waterRenderer",  waterRenderer);

            // Camera setup: CameraController + SculptController share the Main Camera.
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                mainCam = camGo.AddComponent<Camera>();
            }

            CameraController cameraController = mainCam.GetComponent<CameraController>()
                                             ?? mainCam.gameObject.AddComponent<CameraController>();
            SculptController sculptController = mainCam.GetComponent<SculptController>()
                                             ?? mainCam.gameObject.AddComponent<SculptController>();

            BrushPreview brushPreview = EnsureComponent<BrushPreview>("BrushPreview");

            cameraController.BindImportManager(importManager);
            cameraController.BindToolRegistry(toolRegistry);
            waterRenderer.BindImportManager(importManager);
            waterRenderer.BindTerrainManager(terrainManager);

            SetPrivateField(sculptController, "_terrainManager", terrainManager);
            SetPrivateField(sculptController, "_toolRegistry",   toolRegistry);
            SetPrivateField(sculptController, "_undoManager",    undoManager);
            SetPrivateField(sculptController, "_brushPreview",   brushPreview);

            // Register all tools with the ToolRegistry.
            UIManager        uiManager        = EnsureComponent<UIManager>("UIManager");
            ToolParameterGUI toolParameterGUI = EnsureComponent<ToolParameterGUI>("ToolParameterGUI");

            var cameraTool  = new CameraTool();
            var raiseTool   = new RaiseTool(terrainManager, terrainManager);
            var eraseTool   = new EraseTool(terrainManager, terrainManager);
            var flattenTool = new FlattenTool(terrainManager, terrainManager, text => uiManager.SetMeasureText(text));
            var smoothTool    = new SmoothTool(terrainManager, terrainManager);
            var blendTool     = new BlendTool(terrainManager, terrainManager);
            var dredgeTool    = new FillTool(terrainManager, terrainManager,
                                    text => uiManager.SetMeasureText(text));
            var lassoTool     = new LassoTool();
            var clearTool     = new ClearTool(terrainManager, terrainManager);
            var fillTool      = new FillToHeightTool(terrainManager, terrainManager,
                                    text => uiManager.SetMeasureText(text));
            var measureTool     = new MeasureTool(terrainManager, text => uiManager.SetMeasureText(text));
            var measureRenderer = EnsureComponent<MeasurementRenderer>("MeasurementRenderer");
            var lassoRenderer   = EnsureComponent<LassoRenderer>("LassoRenderer");
            var beachTool       = new IslandBuilder.Domain.Tools.BeachTool(terrainManager, terrainManager);
            var beachRenderer   = EnsureComponent<IslandBuilder.Rendering.BeachRenderer>("BeachRenderer");
            var beachToolAlt    = new IslandBuilder.Domain.Tools.BeachToolAlt(terrainManager, terrainManager);
            var beachAltRenderer= EnsureComponent<IslandBuilder.Rendering.BeachAltRenderer>("BeachAltRenderer");
            var gridTool        = new GridTool();

            toolRegistry.Register(cameraTool);
            toolRegistry.Register(raiseTool);
            toolRegistry.Register(eraseTool);
            toolRegistry.Register(flattenTool);
            toolRegistry.Register(smoothTool);
            toolRegistry.Register(blendTool);
            toolRegistry.Register(fillTool);
            toolRegistry.Register(dredgeTool);
            toolRegistry.Register(clearTool);
            toolRegistry.Register(lassoTool);
            toolRegistry.Register(measureTool);
            toolRegistry.Register(beachTool);
            toolRegistry.Register(gridTool);

            var globalSettings = new IslandBuilder.Domain.Tools.GlobalToolSettings();
            foreach (var t in new BrushToolBase[]
            {
                raiseTool, eraseTool, flattenTool, smoothTool, blendTool,
                fillTool, dredgeTool, clearTool
            })
            {
                t.Lasso          = lassoTool;
                t.GlobalSettings = globalSettings;
            }
            toolParameterGUI.BindGlobalSettings(globalSettings);
            sculptController.BindGlobalSettings(globalSettings);
            sculptController.BindHandleEditing(lassoTool, lassoRenderer, measureTool, measureRenderer);
            beachRenderer.Bind(beachTool, terrainManager);
            beachAltRenderer.Bind(beachToolAlt, terrainManager);
            toolRegistry.Register(beachToolAlt);
            toolParameterGUI.BindBeachTool(beachTool, beachRenderer);
            toolParameterGUI.BindBeachAltTool(beachToolAlt, beachAltRenderer);
            toolParameterGUI.BindUndoManager(undoManager);
            sculptController.BindBeachAltHandles(beachToolAlt, beachAltRenderer);

            layerManager.Bind(terrainManager, importManager);
            measureRenderer.Bind(measureTool, terrainManager);
            lassoRenderer.Bind(lassoTool);
            volumeCalculator.Bind(terrainManager);
            costEstimator.Bind(volumeCalculator);
            exportManager.Bind(terrainManager);
            sandHighlight.Bind(terrainManager);
            terrainExtension.Bind(terrainManager, importManager);
            sandLayerSerializer.Bind(terrainManager);
            sandLayerSerializer.BindAppComponents(measureTool, gridRenderer,
                                                  lassoTool, toolParameterGUI);
            gridRenderer.Bind(terrainManager, importManager);

            toolParameterGUI.Bind(toolRegistry);
            toolParameterGUI.BindGridRenderer(gridRenderer);
            toolParameterGUI.BindLassoRenderer(lassoRenderer);
            toolParameterGUI.BindMeasureTool(measureTool);
            toolParameterGUI.BindLassoTool(lassoTool);
            toolParameterGUI.BindLayerManager(layerManager);
            toolParameterGUI.BindWaterRenderer(waterRenderer);
            toolParameterGUI.BindSandHighlightRenderer(sandHighlight);
            toolParameterGUI.BindTerrainManagerForColors(terrainManager);
            uiManager.BindParamGui(toolParameterGUI);
            uiManager.Initialise(importManager, terrainManager, waterRenderer,
                                 toolRegistry, volumeCalculator, costEstimator, exportManager,
                                 sandHighlight, sandLayerSerializer, undoManager);

            toolRegistry.SetActiveTool("camera");

            Debug.Log("[SceneBootstrapper] Island Builder components initialised.");
        }

        private static T EnsureComponent<T>(string goName) where T : Component
        {
            T existing = Object.FindFirstObjectByType<T>();
            if (existing != null) return existing;

            var go = new GameObject(goName);
            return go.AddComponent<T>();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
                field.SetValue(target, value);
            else
                Debug.LogWarning($"[SceneBootstrapper] Field '{fieldName}' not found on {target.GetType().Name}.");
        }
    }
}
