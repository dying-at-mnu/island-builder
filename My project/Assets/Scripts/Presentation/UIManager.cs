using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;
using IslandBuilder.Rendering;

namespace IslandBuilder.Presentation
{
    [AddComponentMenu("Island Builder/UI Manager")]
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private PanelSettings  _panelSettings;
        [SerializeField] private TerrainManager _terrainManager;
        [SerializeField] private WaterRenderer  _waterRenderer;
        [SerializeField] private ToolRegistry   _toolRegistry;

        private ImportManager         _importManager;
        private ExportManager         _exportManager;
        private VolumeCalculator      _volumeCalculator;
        private SandLayerSerializer   _sandLayerSerializer;
        private UndoManager           _undoManager;
        private ToolParameterGUI      _paramGui;

        // Cached for forwarding to ToolParameterGUI stats panel.
        private float _lastVolume;
        private float _lastMassKg;
        private float _lastCostUsd;

        private readonly Dictionary<string, Button> _toolButtons = new();
        private Label _measureLabel;

        private VisualElement _loadingOverlay;
        private VisualElement _loadingFill;

        // ── Public API ────────────────────────────────────────────────────────

        public void Initialise(ImportManager         importManager,
                               TerrainManager        terrainManager   = null,
                               WaterRenderer         waterRenderer    = null,
                               ToolRegistry          toolRegistry     = null,
                               VolumeCalculator      volumeCalculator = null,
                               CostEstimator         costEstimator    = null,
                               ExportManager         exportManager       = null,
                               SandHighlightRenderer sandHighlight       = null,
                               SandLayerSerializer   sandLayerSerializer = null,
                               UndoManager           undoManager         = null)
        {
            _importManager       = importManager;
            _exportManager       = exportManager;
            _sandLayerSerializer = sandLayerSerializer;
            _undoManager         = undoManager;
            _volumeCalculator = volumeCalculator;
            // sandHighlight is now managed by ToolParameterGUI (terrain settings panel)
            if (terrainManager != null) _terrainManager = terrainManager;
            if (waterRenderer  != null) _waterRenderer  = waterRenderer;
            if (toolRegistry   != null) _toolRegistry   = toolRegistry;

            if (_importManager != null)
            {
                _importManager.ImportProgress += SetImportProgress;
                _importManager.ScaleNeeded    += (unit, cb) => _paramGui?.ShowScaleDialog(unit, cb);
                _importManager.ImportCompleted += (td, _) =>
                {
                    float span    = Mathf.Max(td.size.x, td.size.z);
                    if (span <= 0f) return;
                    float minR    = span / 1000f;
                    float maxR    = span * 0.5f;
                    float defaultR = maxR * 0.2f;
                    _paramGui?.SetBrushRange(minR, maxR);
                    _toolRegistry?.SetDefaultBrushRadius(defaultR);
                    _paramGui?.SetTerrainUnit(
                        _importManager.LastUnitScaleFactor,
                        _importManager.LastDetectedUnit,
                        _importManager.LastContourInterval);
                };
            }

            if (volumeCalculator != null)
                volumeCalculator.VolumeChanged += SetVolume;

            if (costEstimator != null)
                costEstimator.CostChanged += SetMassAndCost;

            WireToolButtons();
        }

        public void BindParamGui(ToolParameterGUI paramGui) => _paramGui = paramGui;

        public void SetVolume(float cubicMetres)
        {
            _lastVolume = cubicMetres;
            _paramGui?.SetStats(_lastVolume, _lastMassKg, _lastCostUsd);
        }

        public void SetMassAndCost(float massKg, float costUsd)
        {
            _lastMassKg  = massKg;
            _lastCostUsd = costUsd;
            _paramGui?.SetStats(_lastVolume, _lastMassKg, _lastCostUsd);
        }

        public void SetImportProgress(float t)
        {
            if (_loadingOverlay == null) return;
            if (t < 0f)
            {
                _loadingOverlay.style.display = DisplayStyle.None;
            }
            else
            {
                _loadingOverlay.style.display = DisplayStyle.Flex;
                if (_loadingFill != null)
                    _loadingFill.style.width = Length.Percent(Mathf.Clamp01(t) * 100f);
            }
        }

        public void SetMeasureText(string text)
        {
            if (_measureLabel == null) return;
            _measureLabel.text          = text;
            _measureLabel.style.display = string.IsNullOrEmpty(text)
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            var uiDoc = gameObject.AddComponent<UIDocument>();
            uiDoc.panelSettings = _panelSettings != null ? _panelSettings : BuildPanelSettings();

            var root = uiDoc.rootVisualElement;
            root.style.width  = Length.Percent(100);
            root.style.height = Length.Percent(100);

            BuildTopBar(root);
            BuildLeftToolbar(root);
            BuildMeasureLabel(root);
            BuildLoadingOverlay(root);

            if (_importManager != null)
                _importManager.ImportProgress += SetImportProgress;

            WireToolButtons();
            if (_toolRegistry?.ActiveTool != null)
                OnActiveToolChanged(_toolRegistry.ActiveTool);
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private void BuildTopBar(VisualElement root)
        {
            var bar = new VisualElement();
            bar.style.position        = Position.Absolute;
            bar.style.top             = 0;
            bar.style.left            = 0;
            bar.style.right           = 0;
            bar.style.height          = 36;
            bar.style.backgroundColor = Panel(0.85f);
            bar.style.flexDirection   = FlexDirection.Row;
            bar.style.alignItems      = Align.Center;
            bar.style.paddingLeft     = 10;
            bar.style.paddingRight    = 10;

            var btnImport    = MakeTopBarButton(ToolIconFactory.Import(),    "Import",    () => _importManager?.OpenAndImport());
            var btnExport    = MakeTopBarButton(ToolIconFactory.Export(),    "Export",    () => _exportManager?.Export(new ExportOptions { Type = ExportType.Obj }));
            var btnBaseline  = MakeEditToggle();
            var btnCalc      = MakeTopBarButton(ToolIconFactory.Calculate(), "Calculate", () => _volumeCalculator?.ForceRecalculate());
            var btnReset     = MakeTopBarButton(ToolIconFactory.Reset(),     "Reset",     () => _terrainManager?.ResetToBaseline());
            var btnUndo      = MakeTopBarButton(ToolIconFactory.Undo(),      "Undo",      () => _undoManager?.Undo(_terrainManager));
            var btnRedo      = MakeTopBarButton(ToolIconFactory.Redo(),      "Redo",      () => _undoManager?.Redo(_terrainManager));
            var btnSave      = MakeTopBarButton(ToolIconFactory.SaveLayer(), "Save Sand", () => _sandLayerSerializer?.Save());
            var btnLoad      = MakeTopBarButton(ToolIconFactory.LoadLayer(), "Load Sand", () => _sandLayerSerializer?.Load());

            RegisterTooltip(btnImport,    "Load terrain file (.dxf or .obj)");
            RegisterTooltip(btnExport,    "Export terrain mesh as OBJ");
            RegisterTooltip(btnBaseline,  "Toggle baseline protection on/off");
            RegisterTooltip(btnCalc,      "Calculate sand volume and cost");
            RegisterTooltip(btnReset,     "Reset terrain to imported baseline");
            RegisterTooltip(btnUndo,      "Undo last action  (Ctrl+Z)");
            RegisterTooltip(btnRedo,      "Redo last undone action  (Ctrl+Y)");
            RegisterTooltip(btnSave,      "Save sand edits to file");
            RegisterTooltip(btnLoad,      "Load saved sand edits from file");

            bar.Add(btnImport);    bar.Add(MakeSpacer(6));
            bar.Add(btnExport);    bar.Add(MakeSpacer(12));
            bar.Add(btnBaseline);  bar.Add(MakeSpacer(6));
            bar.Add(btnCalc);      bar.Add(MakeSpacer(6));
            bar.Add(btnReset);     bar.Add(MakeSpacer(6));
            bar.Add(btnUndo);      bar.Add(MakeSpacer(6));
            bar.Add(btnRedo);      bar.Add(MakeSpacer(12));
            bar.Add(btnSave);      bar.Add(MakeSpacer(6));
            bar.Add(btnLoad);

            var flex = new VisualElement();
            flex.style.flexGrow = 1;
            bar.Add(flex);

            bar.Add(MakeLabel("Exaggeration:", 0.8f));
            bar.Add(MakeSpacer(6));

            var exagLabel = MakeLabel("20×", 1f);
            exagLabel.style.width          = 36;
            exagLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            var exagSlider = new Slider(1f, 200f) { value = 20f };
            exagSlider.style.width  = 160;
            exagSlider.style.height = 20;
            exagSlider.RegisterValueChangedCallback(evt =>
            {
                float v = Mathf.Round(evt.newValue);
                exagLabel.text = $"{v:0}×";
                if (_terrainManager != null)
                {
                    float newSeaLevel = _terrainManager.SetVerticalExaggeration(v);
                    _waterRenderer?.SetSeaLevel(newSeaLevel);
                }
            });

            bar.Add(exagSlider);
            bar.Add(MakeSpacer(4));
            bar.Add(exagLabel);
            root.Add(bar);
        }

        private void BuildLeftToolbar(VisualElement root)
        {
            const int PanelW = 180;

            var toolbar = new VisualElement();
            toolbar.style.position        = Position.Absolute;
            toolbar.style.top             = 44;
            toolbar.style.bottom          = 0;
            toolbar.style.left            = 0;
            toolbar.style.width           = PanelW;
            toolbar.style.backgroundColor = Panel(0.85f);
            toolbar.style.flexDirection   = FlexDirection.Column;
            toolbar.style.alignItems      = Align.Stretch;
            toolbar.style.paddingTop      = 8;

            // ── Tool buttons (row-wrapped) ────────────────────────────────────
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection  = FlexDirection.Row;
            buttonRow.style.flexWrap       = Wrap.Wrap;
            buttonRow.style.justifyContent = Justify.Center;

            var tools = new (Texture2D icon, string tip, string id)[]
            {
                (ToolIconFactory.Camera(),  "Camera",  "camera"),
                (ToolIconFactory.Raise(),   "Raise",   "raise"),
                (ToolIconFactory.Erase(),   "Erase",   "erase"),
                (ToolIconFactory.Flatten(), "Flatten", "flatten"),
                (ToolIconFactory.Smooth(),  "Smooth",  "smooth"),
                (ToolIconFactory.Blend(),      "Blend",   "blend"),
                (ToolIconFactory.Beach(),      "Beach",   "beach"),
                (ToolIconFactory.FillHeight(), "Set Height", "fill"),
                (ToolIconFactory.Fill(),       "Dredge",  "dredge"),
                (ToolIconFactory.Clear(),      "Clear",   "clear"),
                (ToolIconFactory.Lasso(),      "Lasso",   "lasso"),
                (ToolIconFactory.Measure(),    "Measure",    "measure"),
                (ToolIconFactory.Grid(),       "Grid",       "grid"),
            };

            foreach (var (icon, tip, id) in tools)
            {
                var btn = MakeToolButton(icon, tip);
                _toolButtons[id] = btn;
                buttonRow.Add(btn);
            }

            toolbar.Add(buttonRow);
            root.Add(toolbar);
        }

        private void BuildLoadingOverlay(VisualElement root)
        {
            _loadingOverlay = new VisualElement();
            _loadingOverlay.style.position        = Position.Absolute;
            _loadingOverlay.style.top             = 0;
            _loadingOverlay.style.bottom          = 0;
            _loadingOverlay.style.left            = 0;
            _loadingOverlay.style.right           = 0;
            _loadingOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            _loadingOverlay.style.display         = DisplayStyle.None;
            _loadingOverlay.style.alignItems      = Align.Center;
            _loadingOverlay.style.justifyContent  = Justify.Center;

            var card = new VisualElement();
            card.style.width           = 320;
            card.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 0.97f);
            card.style.paddingTop      = card.style.paddingBottom = 22;
            card.style.paddingLeft     = card.style.paddingRight  = 22;
            card.style.borderTopLeftRadius     = card.style.borderTopRightRadius    = 6;
            card.style.borderBottomLeftRadius  = card.style.borderBottomRightRadius = 6;

            GUIStyle[] ts = { null };
            var textEl = new IMGUIContainer(() =>
            {
                ts[0] ??= new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 14,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white }
                };
                GUILayout.Label("Importing terrain…", ts[0]);
            });
            textEl.style.height = 22;
            textEl.style.width  = Length.Percent(100);
            card.Add(textEl);

            var track = new VisualElement();
            track.style.height          = 8;
            track.style.width           = Length.Percent(100);
            track.style.marginTop       = 12;
            track.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f, 1f);
            track.style.borderTopLeftRadius     = track.style.borderTopRightRadius    = 4;
            track.style.borderBottomLeftRadius  = track.style.borderBottomRightRadius = 4;

            _loadingFill = new VisualElement();
            _loadingFill.style.position        = Position.Absolute;
            _loadingFill.style.top             = 0;
            _loadingFill.style.bottom          = 0;
            _loadingFill.style.left            = 0;
            _loadingFill.style.width           = Length.Percent(0);
            _loadingFill.style.backgroundColor = new Color(0.25f, 0.65f, 0.95f, 1f);
            _loadingFill.style.borderTopLeftRadius     = _loadingFill.style.borderTopRightRadius    = 4;
            _loadingFill.style.borderBottomLeftRadius  = _loadingFill.style.borderBottomRightRadius = 4;

            track.Add(_loadingFill);
            card.Add(track);
            _loadingOverlay.Add(card);
            root.Add(_loadingOverlay);
        }

        private void BuildMeasureLabel(VisualElement root)
        {
            _measureLabel = new Label();
            _measureLabel.style.position        = Position.Absolute;
            _measureLabel.style.top             = 44;
            _measureLabel.style.right           = 0;
            _measureLabel.style.backgroundColor = Panel(0.80f);
            _measureLabel.style.color           = Color.white;
            _measureLabel.style.fontSize        = 13;
            _measureLabel.style.paddingTop      = _measureLabel.style.paddingBottom = 6;
            _measureLabel.style.paddingLeft     = _measureLabel.style.paddingRight  = 10;
            _measureLabel.style.display         = DisplayStyle.None;
            root.Add(_measureLabel);
        }

        // ── Wiring ────────────────────────────────────────────────────────────

        private bool _wired;

        private void WireToolButtons()
        {
            if (_wired || _toolRegistry == null || _toolButtons.Count == 0) return;
            _wired = true;

            foreach (var (id, btn) in _toolButtons)
            {
                string capturedId = id;
                btn.clicked    += () => _toolRegistry.SetActiveTool(capturedId);
                btn.SetEnabled(true);
            }

            _toolRegistry.ActiveToolChanged += OnActiveToolChanged;
        }

private void OnActiveToolChanged(ITool tool)
        {
            var activeColor  = new Color(0.20f, 0.50f, 0.80f, 1f);
            var normalColor  = new Color(0.25f, 0.25f, 0.25f, 1f);
            var activeBorder = new Color(0.40f, 0.70f, 1.00f, 1f);
            var normalBorder = new Color(0.40f, 0.40f, 0.40f, 1f);

            foreach (var (id, btn) in _toolButtons)
            {
                bool active = id == tool.ToolId;
                btn.style.backgroundColor = active ? activeColor : normalColor;
                btn.style.borderTopColor  = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor  =
                    active ? activeBorder : normalBorder;
            }

            if (tool.ToolId != "measure" && tool.ToolId != "flatten" && tool.ToolId != "cut")
                SetMeasureText(string.Empty);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static PanelSettings BuildPanelSettings()
        {
            var s = ScriptableObject.CreateInstance<PanelSettings>();
            s.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2Int(1920, 1080);
            s.fallbackDpi         = 96;

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets(
                "UnityDefaultRuntimeTheme t:ThemeStyleSheet");
            if (guids.Length > 0)
            {
                var path  = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                var theme = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(path);
                if (theme != null) s.themeStyleSheet = theme;
            }
            else
            {
                Debug.LogWarning("[UIManager] UnityDefaultRuntimeTheme not found.");
            }
#endif
            return s;
        }

        private Button MakeEditToggle()
        {
            var (btn, iconEl, textLbl) = MakeTopBarButtonParts(ToolIconFactory.Lock());
            void Refresh()
            {
                bool locked = _terrainManager == null || _terrainManager.EnforceBaseline;
                textLbl.text = locked ? "Baseline: Protected" : "Baseline: Free";
                var col = locked ? new Color(0.45f, 0.85f, 0.45f) : new Color(1f, 0.70f, 0.30f);
                textLbl.style.color = col;
                iconEl.style.unityBackgroundImageTintColor = col;
                var border = locked ? new Color(0.30f, 0.65f, 0.30f) : new Color(0.75f, 0.50f, 0.20f);
                btn.style.borderTopColor = btn.style.borderBottomColor =
                btn.style.borderLeftColor = btn.style.borderRightColor = border;
            }
            Refresh();
            btn.clicked += () =>
            {
                if (_terrainManager != null)
                    _terrainManager.EnforceBaseline = !_terrainManager.EnforceBaseline;
                Refresh();
            };
            return btn;
        }



        private static Button MakeTopBarButton(Texture2D icon, string text, System.Action clicked)
        {
            var (btn, _, _) = MakeTopBarButtonParts(icon, text, clicked);
            return btn;
        }

        private void RegisterTooltip(VisualElement el, string text)
        {
            el.RegisterCallback<MouseEnterEvent>(evt =>
                _paramGui?.ShowTooltip(text, evt.mousePosition));
            el.RegisterCallback<MouseLeaveEvent>(_ =>
                _paramGui?.HideTooltip());
        }

        private static (Button btn, VisualElement icon, Label label) MakeTopBarButtonParts(
            Texture2D icon, string text = "", System.Action clicked = null)
        {
            var btn = clicked != null ? new Button(clicked) : new Button();
            btn.style.flexDirection   = FlexDirection.Row;
            btn.style.alignItems      = Align.Center;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            btn.style.borderTopWidth  = btn.style.borderBottomWidth =
            btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor  = btn.style.borderBottomColor =
            btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f);
            btn.style.paddingLeft     = 8;
            btn.style.paddingRight    = 8;
            btn.style.paddingTop      = 0;
            btn.style.paddingBottom   = 0;
            btn.style.height          = 24;

            var ic = new VisualElement();
            ic.style.width  = 14;
            ic.style.height = 14;
            ic.style.flexShrink = 0;
            ic.style.unityBackgroundImageTintColor = Color.white;
            if (icon != null)
                ic.style.backgroundImage = new StyleBackground(icon);
            btn.Add(ic);

            var lbl = new Label(text);
            lbl.style.color          = Color.white;
            lbl.style.paddingTop     = 0;
            lbl.style.paddingBottom  = 0;
            lbl.style.paddingLeft    = string.IsNullOrEmpty(text) ? 0 : 5;
            lbl.style.paddingRight   = 0;
            btn.Add(lbl);

            return (btn, ic, lbl);
        }

        private static Button MakeButton(string text, System.Action clicked)
        {
            var btn = new Button(clicked) { text = text };
            btn.style.color           = Color.white;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            btn.style.borderTopWidth  = btn.style.borderBottomWidth =
            btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor  = btn.style.borderBottomColor =
            btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f);
            btn.style.paddingLeft  = 10;
            btn.style.paddingRight = 10;
            btn.style.height       = 24;
            return btn;
        }

        private static Button MakeToolButton(Texture2D icon, string tooltip)
        {
            var btn = new Button { tooltip = tooltip };
            btn.style.width           = 52;
            btn.style.height          = 52;
            btn.style.marginTop    = btn.style.marginBottom =
            btn.style.marginLeft   = btn.style.marginRight  = 2;
            btn.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            btn.style.borderTopWidth  = btn.style.borderBottomWidth =
            btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
            btn.style.borderTopColor  = btn.style.borderBottomColor =
            btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f);
            btn.style.flexDirection   = FlexDirection.Column;
            btn.style.alignItems      = Align.Center;
            btn.style.justifyContent  = Justify.Center;
            btn.style.paddingTop      = btn.style.paddingBottom = 3;

            var iconEl = new VisualElement();
            iconEl.style.width           = 26;
            iconEl.style.height          = 26;
            iconEl.style.backgroundImage = new StyleBackground(icon);

            var nameLbl = new Label(tooltip);
            nameLbl.style.color          = new Color(0.85f, 0.85f, 0.85f, 1f);
            nameLbl.style.fontSize       = 8;
            nameLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLbl.style.marginTop      = 2;

            btn.Add(iconEl);
            btn.Add(nameLbl);
            return btn;
        }

        private static Label MakeLabel(string text, float alpha)
        {
            var lbl = new Label(text);
            lbl.style.color = new Color(1f, 1f, 1f, alpha);
            return lbl;
        }


        private static VisualElement MakeSpacer(float width)
        {
            var s = new VisualElement();
            s.style.width = width;
            return s;
        }

        private static StyleColor Panel(float alpha) =>
            new StyleColor(new Color(0.12f, 0.12f, 0.12f, alpha));
    }
}
