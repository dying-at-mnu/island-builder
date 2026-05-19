using UnityEngine;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;
using IslandBuilder.Rendering;

namespace IslandBuilder.Presentation
{
    /// <summary>
    /// Single IMGUI MonoBehaviour that renders all text the UI Toolkit cannot reliably
    /// display at runtime: tool name + sliders, sand volume/mass/cost stats, and the
    /// top-bar button tooltips.
    /// </summary>
    [AddComponentMenu("Island Builder/Tool Parameter GUI")]
    public class ToolParameterGUI : MonoBehaviour
    {
        [SerializeField] private ToolRegistry _toolRegistry;

        private const float RefHeight = 1080f;
        private const float RefWidth  = 1920f;

        // Dynamic: toolbar top 44 + padding 8 + rows of 56px buttons (3 per row).
        private float ButtonsEndLogY()
        {
            int n    = _toolRegistry?.ToolCount ?? 13;
            int rows = Mathf.Max(1, Mathf.CeilToInt(n / 3f));
            return 44f + 8f + rows * 56f;
        }

        // ── Sand stats ────────────────────────────────────────────────────────
        private float _volume;
        private float _massKg;
        private float _costUsd;
        private bool  _hasStats;

        // ── Brush radius range (updated on terrain import) ────────────────────
        private float _brushMinRadius = 1f;
        private float _brushMaxRadius = 5000f;

        // ── Measure tool (kept regardless of active tool for persistent legend) ─
        private MeasureTool         _measureTool;
        private LassoTool           _lassoTool;
        private WaterRenderer         _waterRenderer;
        private SandHighlightRenderer _sandHighlight;
        private TerrainManager        _terrainManager;

        // ── Terrain settings state ─────────────────────────────────────────────
        private bool   _highlightOn;
        private bool   _showTopFace      = true;
        private bool   _showSeafloor     = false;
        private int    _openDropdown     = 0;   // 0=none, 1=water, 2=terrain
        private Vector2 _dropScrollW, _dropScrollT;
        private float  _waterOpacity     = 1.00f;
        private string _waterOpacityText = "100";
        private float  _terrainUnitScale = 1f;
        private string _terrainUnitName  = "Metres";
        private string _terrainScaleText = "1";
        private string _terrainUnitText  = "Metres";
        private int    _selectedUnit       = 0; // index into UnitLabels
        private int    _waterColorTable   = 0;
        private int    _terrainColorTable = 0;
        // Values detected from the imported file (0 / empty = not found).
        private float  _fileUnitScale      = 0f;
        private float  _fileContourInterval = 0f;
        private string _fileUnitName       = "";
        private bool   _hasFileScale       = false;

        private static readonly string[] UnitLabels = { "Metres", "Feet", "Cm", "Mm", "Km", "Other" };
        private static readonly float[]  UnitScales = { 1f, 0.3048f, 0.01f, 0.001f, 1000f, -1f };

        // Estimated height of the terrain settings panel in logical pixels.
        // Used to offset measurement legend and layer panel below it.
        // Base height when both dropdowns are closed; expands by up to DropH when one is open.
        private const float TerrainSettingsBaseH = 410f; // scale slider removed
        private const float TerrainSettingsDropH = 200f;
        private const float TerrainSettingsLogH  = TerrainSettingsBaseH + TerrainSettingsDropH;
        private LayerManager _layerManager;
        private string[]     _layerNameEdits; // editable layer names
        private int          _editingNameIndex = -1;

        // ── Grid settings ─────────────────────────────────────────────────────
        private GridRenderer  _gridRenderer;
        private LassoRenderer _lassoRenderer;
        private string       _gridSpacingText = "1";

        // ── Beach tool ────────────────────────────────────────────────────────
        private IslandBuilder.Domain.Tools.BeachTool    _beachTool;
        private IslandBuilder.Rendering.BeachRenderer   _beachRenderer;
        private IslandBuilder.Domain.Tools.BeachToolAlt _beachToolAlt;
        private IslandBuilder.Rendering.BeachAltRenderer _beachAltRenderer;
        private IslandBuilder.Domain.UndoManager        _undoManager;

        // ── Global tool settings ───────────────────────────────────────────────
        private IslandBuilder.Domain.Tools.GlobalToolSettings _globalSettings;
        private string _editAboveText = "0";

        // ── Tooltip ───────────────────────────────────────────────────────────
        private string  _tooltipText;
        private Vector2 _tooltipScreenPos; // physical screen pixels

        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle    _titleStyle;
        private GUIStyle    _labelStyle;
        private GUIStyle    _hintStyle;
        private GUIStyle    _sampledStyle;
        private GUIStyle    _statsStyle;
        private GUIStyle    _tooltipStyle;
        private GUIStyle    _tooltipBoxStyle;
        private GUIStyle    _toolbarStyle;
        private GUIStyle    _legendStyle;
        private GUIStyle    _legendBtnStyle;
        private Texture2D[] _paletteTextures;

        // ── Public API ────────────────────────────────────────────────────────

        public void Bind(ToolRegistry registry) => _toolRegistry = registry;

        public void BindGridRenderer(GridRenderer gr)           => _gridRenderer    = gr;
        public void BindLassoRenderer(IslandBuilder.Rendering.LassoRenderer lr) => _lassoRenderer = lr;
        public void BindGlobalSettings(IslandBuilder.Domain.Tools.GlobalToolSettings gs) => _globalSettings = gs;
        public void BindBeachTool(IslandBuilder.Domain.Tools.BeachTool bt,
                                  IslandBuilder.Rendering.BeachRenderer br)
        { _beachTool = bt; _beachRenderer = br; }

        public void BindBeachAltTool(IslandBuilder.Domain.Tools.BeachToolAlt bat,
                                     IslandBuilder.Rendering.BeachAltRenderer bar)
        { _beachToolAlt = bat; _beachAltRenderer = bar; }

        public void BindUndoManager(IslandBuilder.Domain.UndoManager um) => _undoManager = um;

        // ── Terrain / appearance settings snapshot (used by save-load) ────────

        public struct TerrainSnapshot
        {
            public float  WaterOpacity;
            public float  TerrainScale;
            public string UnitName;
            public int    SelectedUnit;
            public int    WaterColorTable;
            public int    TerrainColorTable;
        }

        public TerrainSnapshot GetTerrainSettings() => new TerrainSnapshot
        {
            WaterOpacity     = _waterOpacity,
            TerrainScale     = _terrainUnitScale,
            UnitName         = _terrainUnitName,
            SelectedUnit     = _selectedUnit,
            WaterColorTable  = _waterColorTable,
            TerrainColorTable = _terrainColorTable,
        };

        public void ApplyTerrainSettings(float waterOpacity, float terrainScale,
                                         string unitName, int selectedUnit,
                                         int waterColorTable = 0, int terrainColorTable = 0)
        {
            _waterOpacity     = Mathf.Clamp01(waterOpacity);
            _waterOpacityText = (_waterOpacity * 100f).ToString("F0");
            _terrainUnitScale = Mathf.Max(1e-9f, terrainScale);
            _terrainUnitName  = string.IsNullOrWhiteSpace(unitName) ? "Metres" : unitName;
            _terrainUnitText  = _terrainUnitName;
            _terrainScaleText = _terrainUnitScale.ToString("G5");
            _selectedUnit     = Mathf.Clamp(selectedUnit, 0, UnitLabels.Length - 1);
            _waterColorTable  = Mathf.Clamp(waterColorTable, 0,
                IslandBuilder.Domain.ColorTables.WaterNames.Length - 1);
            _terrainColorTable = Mathf.Clamp(terrainColorTable, 0,
                IslandBuilder.Domain.ColorTables.TerrainNames.Length - 1);
            _waterRenderer?.SetOpacity(_waterOpacity);
            _waterRenderer?.SetTerrainScale(_terrainUnitScale);
            _terrainManager?.SetColorUnitScale(_terrainUnitScale);
            _gridRenderer?.SetUnit(_terrainUnitName, _terrainUnitScale);
            _waterRenderer?.SetColorTable(_waterColorTable);
            _terrainManager?.SetTerrainColorTable(_terrainColorTable);
        }
        public void BindMeasureTool(MeasureTool mt)           => _measureTool   = mt;
        public void BindLassoTool(LassoTool lt)               => _lassoTool     = lt;
        public void BindLayerManager(LayerManager lm)         => _layerManager  = lm;
        public void BindWaterRenderer(WaterRenderer wr)              => _waterRenderer  = wr;
        public void BindSandHighlightRenderer(SandHighlightRenderer shr) => _sandHighlight  = shr;
        public void BindTerrainManagerForColors(TerrainManager tm)   => _terrainManager = tm;

        public void SetTerrainUnit(float metresPerUnit, string unitName,
                                   float rawContourInterval = 0f)
        {
            bool unknown = unitName == "Unknown" || metresPerUnit <= 0f;

            // Remember what the file contained (or that nothing was found).
            _hasFileScale         = !unknown;
            _fileUnitScale        = unknown ? 0f : Mathf.Max(1e-9f, metresPerUnit);
            _fileContourInterval  = rawContourInterval;
            _fileUnitName         = unknown ? "" : unitName;

            // Use the raw Z-value difference as the scale when available;
            // otherwise fall back to the metres-per-unit conversion factor.
            float scaleToUse = (!unknown && rawContourInterval > 0f)
                ? rawContourInterval
                : (unknown ? 1f : _fileUnitScale);
            _terrainUnitScale = scaleToUse;
            _terrainUnitName  = unknown ? "Metres" : _fileUnitName;
            _terrainUnitText  = _terrainUnitName;
            _terrainScaleText = _terrainUnitScale.ToString("G5");

            // Sync the unit dropdown.
            _selectedUnit = UnitLabels.Length - 1; // Other
            for (int i = 0; i < UnitLabels.Length - 1; i++)
            {
                if (string.Equals(UnitLabels[i], _terrainUnitName,
                        System.StringComparison.OrdinalIgnoreCase))
                    { _selectedUnit = i; break; }
            }
            if (unknown) _selectedUnit = 0; // default Metres

            _waterRenderer?.SetTerrainScale(_terrainUnitScale);
            _terrainManager?.SetColorUnitScale(_terrainUnitScale);
            _gridRenderer?.SetUnit(_terrainUnitName, _terrainUnitScale);
        }

        /// <summary>
        /// Called once per import with the terrain's actual horizontal extents.
        /// Sets the brush-radius slider range to [size/100 … size].
        /// </summary>
        public void SetBrushRange(float minMetres, float maxMetres)
        {
            _brushMinRadius = Mathf.Max(0.01f, minMetres);
            _brushMaxRadius = Mathf.Max(_brushMinRadius + 0.01f, maxMetres);
        }

        public void SetStats(float volume, float massKg, float costUsd)
        {
            _volume   = volume;
            _massKg   = massKg;
            _costUsd  = costUsd;
            _hasStats = true;
        }

        public void ShowTooltip(string text, Vector2 panelPos)
        {
            _tooltipText = text;
            // Convert UI Toolkit logical coords to IMGUI physical screen coords.
            _tooltipScreenPos = new Vector2(
                panelPos.x * Screen.width  / RefWidth,
                panelPos.y * Screen.height / RefHeight);
        }

        public void HideTooltip() => _tooltipText = null;

        // ── Unity ─────────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.75f, 0.90f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                normal   = { textColor = Color.white }
            };
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                normal   = { textColor = new Color(0.65f, 0.65f, 0.65f) }
            };
            _sampledStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                normal   = { textColor = new Color(0.50f, 0.85f, 1.00f) }
            };
            _statsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                normal   = { textColor = new Color(0.85f, 0.95f, 0.85f) }
            };
            _tooltipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                normal   = { textColor = Color.white }
            };
            _tooltipBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0.10f, 0.10f, 0.10f, 0.92f)) }
            };
            _toolbarStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                normal   = { textColor = new Color(0.70f, 0.70f, 0.70f) },
                onNormal = { textColor = Color.white,
                             background = MakeTex(new Color(0.25f, 0.50f, 0.80f, 1f)) }
            };
            _legendStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                normal   = { textColor = Color.white }
            };
            _legendBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                normal   = { textColor = new Color(0.80f, 0.35f, 0.35f) }
            };
            _paletteTextures = new Texture2D[MeasureTool.Palette.Length];
            for (int i = 0; i < MeasureTool.Palette.Length; i++)
                _paletteTextures[i] = MakeTex(MeasureTool.Palette[i]);
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolParams();
            DrawStats();
            DrawTerrainSettings();
            DrawMeasureLegend();
            DrawLayerPanel();
            DrawGlobalSettings();
            DrawTooltip();
            DrawScaleDialog();
        }

        // ── Tool parameters (left sidebar) ────────────────────────────────────

        private void DrawToolParams()
        {
            var tool = _toolRegistry?.ActiveTool;
            if (tool == null) return;

            var areaRect = GetParamAreaRect();
            float scale  = Screen.height / RefHeight;

            // Separator line above params
            GUI.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            GUI.DrawTexture(
                new Rect(4f * scale, areaRect.y - 10f * scale, 172f * scale, 1f),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(areaRect);
            GUILayout.Label(DisplayName(tool.ToolId), _titleStyle);
            GUILayout.Space(6);

            if (tool is BrushToolBase brush)
                DrawBrushParams(brush);
            else if (tool.ToolId == "camera")
            {
                GUILayout.Label("Left-drag    orbit", _hintStyle);
                GUILayout.Label("Right-drag   pan",   _hintStyle);
                GUILayout.Label("Scroll         zoom", _hintStyle);
            }
            else if (tool.ToolId == "grid" && _gridRenderer != null)
            {
                DrawGridToolParams();
            }
            else if (tool.ToolId == "lasso" && _lassoTool != null)
            {
                DrawLassoToolParams();
            }
            else if (tool.ToolId == "fill" && _lassoTool != null)
            {
                DrawLassoStatusHint();
            }
            else if (tool.ToolId == "measure" && _measureTool != null)
            {
                DrawMeasureToolParams(_measureTool);
            }
            else if (tool.ToolId == "beach" && _beachTool != null)
            {
                DrawBeachParams(_beachTool);
            }
            else if (tool.ToolId == "beachalt" && _beachToolAlt != null)
            {
                DrawBeachAltParams(_beachToolAlt);
            }

            GUILayout.Space(10);
            GUILayout.Label(ToolDescription(tool.ToolId), _hintStyle);

            string sampledLine = tool switch
            {
                FlattenTool  ft when ft.TargetSampled => $"Target: {ft.TargetMetres:F1} m",
                FlattenTool                           => "Target: none\n(Ctrl+click to set)",
                CutPlaneTool ct when ct.CutSampled   => $"Cut at: {ct.CutMetres:F1} m",
                CutPlaneTool                          => "Cut at: none\n(Ctrl+click to set)",
                _                                     => null
            };
            if (sampledLine != null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(sampledLine, _sampledStyle);
                GUILayout.Space(6);
            }

            GUILayout.EndArea();
        }

        // ── Measurement legend (right side, shown whenever there are lines) ──────

        // ── Terrain settings panel (top-right, always visible) ────────────────────

        private void DrawTerrainSettings()
        {
            float scale  = Screen.height / RefHeight;
            float lh     = 28f * scale;
            float pad    =  8f * scale;
            float panelW = 260f * scale;
            float panelX = Screen.width - panelW - 8f * scale;
            float panelY = 50f * scale;
            float dropExtra = _openDropdown != 0 ? TerrainSettingsDropH * scale : 0f;
            float panelH = TerrainSettingsBaseH * scale + dropExtra;

            // Background
            GUI.color = new Color(0.10f, 0.10f, 0.10f, 0.88f);
            GUI.DrawTexture(new Rect(panelX - pad * 0.5f, panelY - pad * 0.5f,
                                     panelW + pad, panelH + pad), Texture2D.whiteTexture);
            GUI.color = new Color(0.40f, 0.40f, 0.40f, 1f);
            GUI.DrawTexture(new Rect(panelX - pad * 0.5f, panelY - pad * 0.5f,
                                     panelW + pad, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            int  lfs     = Mathf.RoundToInt(20f * scale);
            int  sfs     = Mathf.RoundToInt(17f * scale);
            float iconW  = lh * 0.9f;
            var lblSt    = new GUIStyle(_legendStyle)  { fontSize = lfs };
            var hintSt2  = new GUIStyle(_hintStyle)    { fontSize = Mathf.RoundToInt(17f * scale) };
            var titleSt2 = new GUIStyle(_titleStyle)   { fontSize = Mathf.RoundToInt(22f * scale) };
            var btnSt    = new GUIStyle(GUI.skin.button){ fontSize = sfs };
            var fieldSt  = new GUIStyle(GUI.skin.textField){ fontSize = lfs };

            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUILayout.Label("Terrain Settings", titleSt2);

            // ── Highlight sand ────────────────────────────────────────────────
            int hSel = GUILayout.Toolbar(_highlightOn ? 0 : 1,
                new[] { "Sand: Highlight", "Sand: Normal" }, _toolbarStyle);
            bool newHL = hSel == 0;
            if (newHL != _highlightOn)
            {
                _highlightOn = newHL;
                _sandHighlight?.SetHighlighting(_highlightOn);
            }

            GUILayout.Space(8);

            // ── Water opacity ─────────────────────────────────────────────────
            GUILayout.Label($"Water Opacity: {_waterOpacity * 100f:F0}%", lblSt);
            float newOp = GUILayout.HorizontalSlider(_waterOpacity, 0f, 1f);
            if (!Mathf.Approximately(newOp, _waterOpacity))
            {
                _waterOpacity     = newOp;
                _waterOpacityText = (_waterOpacity * 100f).ToString("F0");
                _waterRenderer?.SetOpacity(_waterOpacity);
            }
            GUILayout.BeginHorizontal();
            _waterOpacityText = GUILayout.TextField(_waterOpacityText, fieldSt,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Set%", btnSt, GUILayout.Width(iconW * 1.4f)))
            {
                if (float.TryParse(_waterOpacityText,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float pct))
                {
                    _waterOpacity = Mathf.Clamp01(pct / 100f);
                    _waterRenderer?.SetOpacity(_waterOpacity);
                }
                _waterOpacityText = (_waterOpacity * 100f).ToString("F0");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── File scale info ───────────────────────────────────────────────
            if (_hasFileScale)
            {
                GUILayout.Label($"File: {_fileUnitName}  ({_fileUnitScale:G4} m/unit)", hintSt2);
                if (GUILayout.Button("Revert to file default", btnSt))
                {
                    _terrainUnitScale = _fileContourInterval > 0f
                        ? _fileContourInterval : _fileUnitScale;
                    _terrainUnitName  = _fileUnitName;
                    _terrainUnitText  = _fileUnitName;
                    _terrainScaleText = _terrainUnitScale.ToString("G5");
                    _selectedUnit     = UnitLabels.Length - 1;
                    for (int i = 0; i < UnitLabels.Length - 1; i++)
                        if (string.Equals(UnitLabels[i], _fileUnitName,
                                System.StringComparison.OrdinalIgnoreCase))
                            { _selectedUnit = i; break; }
                    _waterRenderer?.SetTerrainScale(_terrainUnitScale);
            _terrainManager?.SetColorUnitScale(_terrainUnitScale);
            _gridRenderer?.SetUnit(_terrainUnitName, _terrainUnitScale);
                    // Do not show preview grid on revert.
                }
            }
            else
            {
                GUILayout.Label("No scale found in file.", hintSt2);
            }

            GUILayout.Space(4);

            // ── Unit picker (sets NAME only) ──────────────────────────────────
            GUILayout.Label("Unit", lblSt);
            int newSel = GUILayout.SelectionGrid(_selectedUnit, UnitLabels, 3, _toolbarStyle);
            if (newSel != _selectedUnit)
            {
                _selectedUnit = newSel;
                if (newSel < UnitLabels.Length - 1)
                {
                    // Snap to the standard scale for this unit.
                    _terrainUnitName  = UnitLabels[newSel];
                    _terrainUnitScale = UnitScales[newSel];
                    _terrainUnitText  = UnitLabels[newSel];
                    _terrainScaleText = _terrainUnitScale.ToString("G5");
                    _waterRenderer?.SetTerrainScale(_terrainUnitScale);
            _terrainManager?.SetColorUnitScale(_terrainUnitScale);
            _gridRenderer?.SetUnit(_terrainUnitName, _terrainUnitScale);
                }
            }

            // Custom unit name field.
            if (_selectedUnit == UnitLabels.Length - 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", hintSt2, GUILayout.Width(iconW));
                _terrainUnitText = GUILayout.TextField(_terrainUnitText, fieldSt,
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("OK", btnSt, GUILayout.Width(iconW * 0.8f)))
                {
                    _terrainUnitName = string.IsNullOrWhiteSpace(_terrainUnitText)
                        ? "Custom" : _terrainUnitText;
                    _gridRenderer?.SetUnit(_terrainUnitName, _terrainUnitScale);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);

            // ── Scale — read from file Z-value difference ─────────────────────
            if (_hasFileScale && _fileContourInterval > 0f)
                GUILayout.Label($"Scale: {_fileContourInterval:G4} {_fileUnitName}", lblSt);
            else if (_hasFileScale)
                GUILayout.Label($"Scale: 1 {_fileUnitName}", lblSt);
            else
                GUILayout.Label("Scale: not found in file", hintSt2);

            GUILayout.Space(8);

            // ── Water surface toggle ──────────────────────────────────────────
            bool newTopFace = GUILayout.Toggle(_showTopFace, "  Show Water Surface");
            if (newTopFace != _showTopFace)
            {
                _showTopFace = newTopFace;
                _waterRenderer?.SetShowTopFace(_showTopFace);
            }

            bool newSeafloor = GUILayout.Toggle(_showSeafloor, "  Show Seafloor");
            if (newSeafloor != _showSeafloor)
            {
                _showSeafloor = newSeafloor;
                _waterRenderer?.SetShowSeafloor(_showSeafloor);
            }

            GUILayout.Space(4);

            // ── Water colour table (dropdown) ─────────────────────────────────
            var waterNames = IslandBuilder.Domain.ColorTables.WaterNames;
            if (GUILayout.Button($"Water: {waterNames[_waterColorTable]}", _toolbarStyle))
                _openDropdown = _openDropdown == 1 ? 0 : 1;
            if (_openDropdown == 1)
            {
                float itemH = lh * 0.95f;
                float listH = Mathf.Min(waterNames.Length * itemH, TerrainSettingsDropH * scale);
                _dropScrollW = GUILayout.BeginScrollView(_dropScrollW, GUILayout.Height(listH));
                for (int i = 0; i < waterNames.Length; i++)
                {
                    bool sel = i == _waterColorTable;
                    var c = GUI.color; if (sel) GUI.color = new Color(0.4f, 0.75f, 1f);
                    if (GUILayout.Button(waterNames[i], _toolbarStyle))
                    { _waterColorTable = i; _waterRenderer?.SetColorTable(i); _openDropdown = 0; }
                    GUI.color = c;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(6);

            // ── Terrain colour table (dropdown, includes all water palettes) ──
            var terrainNames = IslandBuilder.Domain.ColorTables.CombinedTerrainNames;
            if (GUILayout.Button($"Terrain: {terrainNames[_terrainColorTable]}", _toolbarStyle))
                _openDropdown = _openDropdown == 2 ? 0 : 2;
            if (_openDropdown == 2)
            {
                float itemH = lh * 0.95f;
                float listH = Mathf.Min(terrainNames.Length * itemH, TerrainSettingsDropH * scale);
                _dropScrollT = GUILayout.BeginScrollView(_dropScrollT, GUILayout.Height(listH));
                for (int i = 0; i < terrainNames.Length; i++)
                {
                    bool sel = i == _terrainColorTable;
                    var c = GUI.color; if (sel) GUI.color = new Color(0.4f, 0.75f, 1f);
                    if (GUILayout.Button(terrainNames[i], _toolbarStyle))
                    { _terrainColorTable = i; _terrainManager?.SetTerrainColorTable(i); _openDropdown = 0; }
                    GUI.color = c;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// Sets the grid to show one-unit spacing and enables it, so the user can
        /// see what the current terrain scale looks like on the terrain.
        /// </summary>
        private void ApplyPreviewGrid()
        {
            if (_gridRenderer == null) return;
            _gridRenderer.SetActive(true);
            _gridRenderer.SetDisplaySpacing(1f); // 1 terrain unit
        }

        private void DrawMeasureLegend()
        {
            var mt = _measureTool;
            if (mt == null || mt.Lines.Count == 0) return;

            float scale  = Screen.height / RefHeight;
            float lh     = 28f * scale;
            float sw     = lh * 0.85f;
            float pad    = 8f * scale;
            float panelW = 230f * scale;
            float panelX = Screen.width - panelW - 10f * scale;
            // Offset below the terrain settings panel.
            float panelY = (50f + TerrainSettingsLogH + 8f) * scale;

            int   count  = mt.Lines.Count;
            float btnH   = lh + 4f * scale;
            float totalH = count * (lh + 4f * scale) + btnH + pad * 2f;

            // Dark background
            GUI.color = new Color(0.10f, 0.10f, 0.10f, 0.88f);
            GUI.DrawTexture(new Rect(panelX - pad, panelY - pad, panelW + pad, totalH + pad),
                            Texture2D.whiteTexture);
            GUI.color = new Color(0.40f, 0.40f, 0.40f, 1f);
            GUI.DrawTexture(new Rect(panelX - pad, panelY - pad, panelW + pad, 1f),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;

            _legendStyle.fontSize = Mathf.RoundToInt(20f * scale);

            // Panel title showing the active measurement unit.
            string gridUnit = _gridRenderer?.UnitName ?? "m";
            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, lh + pad * 0.5f));
            GUILayout.Label($"Measurements ({gridUnit})",
                new GUIStyle(_titleStyle) { fontSize = Mathf.RoundToInt(20f * scale) });
            GUILayout.EndArea();
            panelY += lh + pad * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float y   = panelY + i * (lh + 4f * scale);
                int   pi  = i % _paletteTextures.Length;

                // Colour swatch
                GUI.DrawTexture(new Rect(panelX, y + (lh - sw) * 0.5f, sw, sw),
                                _paletteTextures[pi]);

                // Distance label
                float gridScale = _gridRenderer != null && _gridRenderer.UnitScale > 0f
                    ? _gridRenderer.UnitScale : 1f;
                string label = $"Line {i + 1}:  {mt.Lines[i].Metres / gridScale:N1} {gridUnit}";
                GUI.Label(new Rect(panelX + sw + pad, y, panelW - sw - pad, lh),
                          label, _legendStyle);
            }

            // Clear All button
            float btnY = panelY + count * (lh + 4f * scale);
            _legendBtnStyle.fontSize = Mathf.RoundToInt(18f * scale);
            if (GUI.Button(new Rect(panelX, btnY, panelW, btnH), "Clear All Lines",
                           _legendBtnStyle))
                mt.ClearLines();
        }

        // ── Layer panel (right side, always visible) ─────────────────────────────

        private void DrawLayerPanel()
        {
            if (_layerManager == null) return;
            var layers = _layerManager.Layers;
            if (layers.Count == 0) return;

            float scale = Screen.height / RefHeight;
            float lh    = 28f * scale;
            float pad   =  8f * scale;
            float panelW = 260f * scale;
            float panelX = Screen.width - panelW - 8f * scale;

            // Push below measurement legend if it is active.
            // Start below terrain settings panel.
            float panelY = (50f + TerrainSettingsLogH + 8f) * scale;
            // Also below the measurement legend if it has lines.
            if (_measureTool != null && _measureTool.Lines.Count > 0)
                panelY += _measureTool.Lines.Count * (lh + 4f * scale) + lh + pad * 2f + 8f * scale;

            // Height = title + n rows + rename row (if editing) + bottom buttons
            bool editing = _editingNameIndex >= 0 && _editingNameIndex < layers.Count;
            float panelH = lh + layers.Count * (lh + 3f * scale) + (editing ? lh + 4f * scale : 0f)
                         + lh + pad * 2.5f;

            // Background
            GUI.color = new Color(0.10f, 0.10f, 0.10f, 0.88f);
            GUI.DrawTexture(new Rect(panelX - pad * 0.5f, panelY - pad * 0.5f,
                                     panelW + pad, panelH + pad), Texture2D.whiteTexture);
            GUI.color = new Color(0.40f, 0.40f, 0.40f, 1f);
            GUI.DrawTexture(new Rect(panelX - pad * 0.5f, panelY - pad * 0.5f,
                                     panelW + pad, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            int   lfs      = Mathf.RoundToInt(20f * scale);
            int   sfs      = Mathf.RoundToInt(17f * scale);
            float iconW    = lh * 0.95f;
            var activeSt   = new GUIStyle(_legendStyle) { fontSize = lfs, normal = { textColor = new Color(0.35f, 0.80f, 1.00f) } };
            var hiddenSt   = new GUIStyle(_legendStyle) { fontSize = lfs, normal = { textColor = new Color(0.45f, 0.45f, 0.45f) } };
            var normalSt   = new GUIStyle(_legendStyle) { fontSize = lfs };
            var btnSt      = new GUIStyle(GUI.skin.button) { fontSize = sfs };
            var redBtnSt   = new GUIStyle(btnSt) { normal = { textColor = new Color(1f, 0.40f, 0.40f) } };
            var fieldSt    = new GUIStyle(GUI.skin.textField) { fontSize = lfs };
            var titleSt    = new GUIStyle(_titleStyle)  { fontSize = Mathf.RoundToInt(22f * scale) };

            GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH));
            GUILayout.Label("Layers", titleSt);

            // Sync name edit buffer length
            if (_layerNameEdits == null || _layerNameEdits.Length != layers.Count)
            {
                _layerNameEdits = new string[layers.Count];
                for (int i = 0; i < layers.Count; i++) _layerNameEdits[i] = layers[i].Name;
            }

            // Rows: drawn top-to-bottom on screen = highest index first (top of stack).
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var  layer    = layers[i];
                bool isActive = i == _layerManager.ActiveIndex;

                GUILayout.BeginHorizontal();

                // ── Visibility toggle ─────────────────────────────────────────
                bool wasVis = layer.IsVisible;
                bool nowVis = GUILayout.Toggle(wasVis, wasVis ? "●" : "○",
                    GUILayout.Width(iconW));
                if (nowVis != wasVis) _layerManager.SetLayerVisible(i, nowVis);

                // ── Name button (activates; click again to rename) ─────────────
                var lockedSt = new GUIStyle(normalSt) { normal = { textColor = new Color(0.85f, 0.60f, 0.15f) } };
                var nameSt = isActive        ? activeSt
                           : !layer.IsVisible ? hiddenSt
                           : layer.IsLocked   ? lockedSt
                           : normalSt;
                if (GUILayout.Button(layer.Name, nameSt, GUILayout.ExpandWidth(true)))
                {
                    if (isActive && _editingNameIndex != i)
                    {
                        _editingNameIndex = i;
                        _layerNameEdits[i] = layer.Name;
                    }
                    else
                    {
                        _layerManager.SetActiveLayer(i);
                        _editingNameIndex = -1;
                    }
                }

                // ── Lock ─────────────────────────────────────────────────────
                bool isLocked = layer.IsLocked;
                var lockSt = new GUIStyle(btnSt);
                if (isLocked) lockSt.normal.textColor = new Color(1f, 0.65f, 0.10f);
                if (GUILayout.Button(isLocked ? "🔒" : "🔓", lockSt, GUILayout.Width(iconW)))
                    _layerManager.SetLayerLocked(i, !isLocked);

                // ── Reorder ▲▼ ───────────────────────────────────────────────
                if (GUILayout.Button("▲", btnSt, GUILayout.Width(iconW)))
                    _layerManager.MoveLayerUp(i);
                if (GUILayout.Button("▼", btnSt, GUILayout.Width(iconW)))
                    _layerManager.MoveLayerDown(i);

                // ── Trash (delete) ────────────────────────────────────────────
                if (layers.Count > 1)
                {
                    if (GUILayout.Button("✕", redBtnSt, GUILayout.Width(iconW)))
                    {
                        _layerManager.RemoveLayer(i);
                        if (_editingNameIndex == i) _editingNameIndex = -1;
                    }
                }

                GUILayout.EndHorizontal();

                // ── Inline rename field (shown when this layer's name is being edited) ──
                if (_editingNameIndex == i)
                {
                    GUILayout.BeginHorizontal();
                    if (_layerNameEdits == null || i >= _layerNameEdits.Length)
                        _layerNameEdits = new string[layers.Count];
                    _layerNameEdits[i] = GUILayout.TextField(_layerNameEdits[i], fieldSt,
                        GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("✓", btnSt, GUILayout.Width(iconW * 1.1f)))
                    {
                        _layerManager.RenameLayer(i, _layerNameEdits[i]);
                        _editingNameIndex = -1;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4f * scale);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add", btnSt))
                _layerManager.AddLayer();
            if (layers.Count > 1 && _layerManager.ActiveIndex > 0 &&
                GUILayout.Button("Merge ↓", btnSt))
                _layerManager.MergeDown(_layerManager.ActiveIndex);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawFillHeightToolParams(FillToHeightTool fht)
        {
            float maxH = Mathf.Max(1f, fht.MaxHeightMetres);
            float curH = fht.TargetMetres;

            GUILayout.Label($"Height: {curH:F1} m", _labelStyle);
            float newH = GUILayout.HorizontalSlider(curH, 0f, maxH);
            if (!Mathf.Approximately(newH, curH)) fht.SetTarget(newH);

            GUILayout.Space(8);
            GUILayout.Label("Direction", _labelStyle);
            int dirIdx = GUILayout.Toolbar((int)fht.Direction,
                new[] { "Both", "Fill", "Cut" }, _toolbarStyle);
            if (dirIdx != (int)fht.Direction) fht.Direction = (FlattenDir)dirIdx;

            GUILayout.Space(6);
            if (_lassoTool != null)
            {
                bool fel = _globalSettings?.FillEntireLasso ?? false;
                string hint = fel
                    ? (_lassoTool.HasSelection ? "Click: fill entire selection" : "Click: fill entire terrain")
                    : "Drag brush to fill";
                GUILayout.Label(hint, _hintStyle);
            }
            GUILayout.Label("Ctrl+click terrain to sample height", _hintStyle);
        }

        private void DrawFlattenParams(FlattenTool ft)
        {
            // ── Target height display ─────────────────────────────────────────
            GUILayout.Label(ft.TargetSampled
                ? $"Target: {ft.TargetMetres:F1} m"
                : "Target: none", _labelStyle);
            GUILayout.Label("Ctrl+click terrain to sample", _hintStyle);

            GUILayout.Space(8);

            // ── Direction ─────────────────────────────────────────────────────
            GUILayout.Label("Direction", _labelStyle);
            int dirIdx = GUILayout.Toolbar((int)ft.Dir,
                new[] { "Both", "Fill", "Cut" }, _toolbarStyle);
            if (dirIdx != (int)ft.Dir) ft.Dir = (FlattenDir)dirIdx;

            GUILayout.Space(8);
            GUILayout.Label($"Strength: {ft.Strength * 100f:F0}%", _labelStyle);
            float s = GUILayout.HorizontalSlider(ft.Strength, 0.01f, 1f);
            if (Mathf.Abs(s - ft.Strength) > 0.001f) ft.Strength = s;
            DrawSharpnessSlider(ft);
        }

        private void DrawLassoToolParams()
        {
            bool has = _lassoTool.HasSelection;
            GUILayout.Label(has ? "Selection active" : "Drag to draw selection", _hintStyle);

            if (has)
            {
                GUILayout.Space(8);
                GUILayout.Label("Tools apply:", _labelStyle);
                int sel = GUILayout.Toolbar(_lassoTool.InvertSelection ? 1 : 0,
                    new[] { "Inside", "Outside" }, _toolbarStyle);
                _lassoTool.InvertSelection = sel == 1;

                // ── Handle spacing ────────────────────────────────────────────
                if (_lassoRenderer != null)
                {
                    GUILayout.Space(6);
                    float sp = _lassoRenderer.HandleSpacingMetres;
                    GUILayout.Label($"Handle spacing: {sp:F0} m", _labelStyle);
                    float newSp = GUILayout.HorizontalSlider(sp, 1f, 100f);
                    if (!Mathf.Approximately(newSp, sp))
                        _lassoRenderer.HandleSpacingMetres = newSp;
                }

                GUILayout.FlexibleSpace();
                var clrSt = new GUIStyle(GUI.skin.button)
                    { fontSize = _hintStyle.fontSize,
                      normal   = { textColor = new Color(1f, 0.55f, 0.35f) } };
                if (GUILayout.Button("Clear Selection", clrSt))
                    _lassoTool.ClearSelection();
                GUILayout.Space(6);
            }
        }

        private void DrawLassoStatusHint()
        {
            bool has = _lassoTool.HasSelection;
            GUILayout.Label(has
                ? "Lasso active — click to fill"
                : "Draw a lasso first, or use as a brush.", _hintStyle);
        }

        private void DrawGridToolParams()
        {
            // ── Visibility toggle (single button) ─────────────────────────────
            bool wasActive = _gridRenderer.IsActive;
            bool newActive = GUILayout.Toggle(wasActive, wasActive ? "Grid: ON" : "Grid: OFF",
                                 _toolbarStyle);
            if (newActive != wasActive)
                _gridRenderer.SetActive(newActive);

            GUILayout.Space(10);

            // ── Spacing ───────────────────────────────────────────────────────
            string unit = _gridRenderer.UnitName;
            GUILayout.Label($"Spacing  ({unit})", _labelStyle);

            // Square-root slider for log-ish feel (fine control near zero, wide range up top).
            float disp    = _gridRenderer.DisplaySpacing;
            float sqrtVal = GUILayout.HorizontalSlider(
                Mathf.Sqrt(disp), Mathf.Sqrt(0.01f), Mathf.Sqrt(10000f));
            float newDisp = sqrtVal * sqrtVal;
            if (Mathf.Abs(newDisp - disp) > 0.001f)
            {
                _gridRenderer.SetDisplaySpacing(newDisp);
                _gridSpacingText = _gridRenderer.DisplaySpacing.ToString("G5");
            }

            GUILayout.Space(6);

            // Text field + Apply on one row.
            GUILayout.BeginHorizontal();
            var fieldSt = new GUIStyle(GUI.skin.textField) { fontSize = _labelStyle.fontSize };
            var applyBtnSt = new GUIStyle(GUI.skin.button)
                { fontSize = _labelStyle.fontSize, normal = { textColor = Color.white } };
            _gridSpacingText = GUILayout.TextField(_gridSpacingText, fieldSt,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Set", applyBtnSt, GUILayout.Width(44f * Screen.height / RefHeight)))
            {
                if (float.TryParse(_gridSpacingText,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f)
                    _gridRenderer.SetDisplaySpacing(v);
                _gridSpacingText = _gridRenderer.DisplaySpacing.ToString("G5");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"= {_gridRenderer.SpacingMetres:F3} m per cell", _hintStyle);

            GUILayout.Space(10);
            var resetSt = new GUIStyle(GUI.skin.button) { fontSize = _hintStyle.fontSize };
            if (GUILayout.Button("Reset to file default", resetSt))
            {
                _gridRenderer.SetDisplaySpacing(1f);
                _gridSpacingText = "1";
            }

            GUILayout.Space(10);

            // ── Contour lines ─────────────────────────────────────────────────
            bool wasContour = _gridRenderer.IsContourActive;
            bool newContour = GUILayout.Toggle(wasContour,
                wasContour ? "Contours: ON" : "Contours: OFF", _toolbarStyle);
            if (newContour != wasContour)
                _gridRenderer.SetContourActive(newContour);
        }

        private void DrawBrushParams(BrushToolBase brush)
        {
            // ── Brush radius (all tools) ──────────────────────────────────────
            float clampedR = Mathf.Clamp(brush.BrushRadius, _brushMinRadius, _brushMaxRadius);
            GUILayout.Label($"Brush Radius: {clampedR:F0} m", _labelStyle);
            float r = GUILayout.HorizontalSlider(clampedR, _brushMinRadius, _brushMaxRadius);
            if (Mathf.Abs(r - brush.BrushRadius) > 0.1f) brush.BrushRadius = r;

            GUILayout.Space(10);

            if (brush is FillToHeightTool fht)
            {
                DrawFillHeightToolParams(fht);
                return;
            }

            if (brush is FillTool dredge)
            {
                DrawDredgeParams(dredge);
                return;
            }

            if (brush is RaiseTool rt)
            {
                DrawRaiseParams(rt);
            }
            else if (brush is EraseTool et)
            {
                GUILayout.Label($"Strength: {et.Strength * 100f:F0}%", _labelStyle);
                float s = GUILayout.HorizontalSlider(et.Strength, 0.01f, 1f);
                if (Mathf.Abs(s - et.Strength) > 0.001f) et.Strength = s;
                DrawSharpnessSlider(et);
                DrawEraseShapeParams(et);
            }
            else if (brush is FlattenTool ft)
            {
                DrawFlattenParams(ft);
            }
            else if (brush.HasStrength)
            {
                GUILayout.Label($"Strength: {brush.Strength * 100f:F0}%", _labelStyle);
                float s = GUILayout.HorizontalSlider(brush.Strength, 0.01f, 1f);
                if (Mathf.Abs(s - brush.Strength) > 0.001f) brush.Strength = s;
                DrawSharpnessSlider(brush);
            }
            else
            {
                DrawSharpnessSlider(brush);
            }
        }

        private void DrawBeachAltParams(IslandBuilder.Domain.Tools.BeachToolAlt bt)
        {
            // ── Mode toggle ───────────────────────────────────────────────────
            if (GUILayout.Button("← Classic mode", new GUIStyle(GUI.skin.button)
                    { fontSize = _hintStyle.fontSize, normal = { textColor = Color.white } }))
                _toolRegistry?.SetActiveTool("beach");

            GUILayout.Space(6);

            // ── Phase status ──────────────────────────────────────────────────
            string phaseHint = bt.Phase switch
            {
                IslandBuilder.Domain.Tools.BeachAltPhase.DrawingInner =>
                    "Drag to draw the crest line (inner boundary)",
                IslandBuilder.Domain.Tools.BeachAltPhase.InnerReady =>
                    "Crest line done.\nDrag on terrain to draw the water boundary, or edit handles.",
                IslandBuilder.Domain.Tools.BeachAltPhase.DrawingOuter =>
                    "Drag to draw the water boundary (outer line)",
                _ => "Both lines drawn. Edit handles or create beach.",
            };
            GUILayout.Label(phaseHint, _hintStyle);
            GUILayout.Space(6);

            var phase = bt.Phase;

            if (phase == IslandBuilder.Domain.Tools.BeachAltPhase.InnerReady)
            {
                if (GUILayout.Button("Draw water boundary →", _toolbarStyle))
                    bt.BeginDrawOuter();
            }

            // ── Slope selector ────────────────────────────────────────────────
            GUILayout.Space(6);
            GUILayout.Label("Slope profile", _labelStyle);
            int cur = (int)bt.Slope;
            int sel = GUILayout.SelectionGrid(cur,
                new[] { "Flat", "Linear", "Steep Top", "Steep Base", "S-Curve" }, 2,
                new GUIStyle(_toolbarStyle) { fontSize = Mathf.RoundToInt(15f * Screen.height / RefHeight) });
            if (sel != cur) { bt.Slope = (IslandBuilder.Domain.Tools.BeachSlope)sel; bt.NotifySettingsChanged(); }

            GUILayout.Space(10);

            // ── Actions ───────────────────────────────────────────────────────
            var createSt = new GUIStyle(GUI.skin.button)
                { fontSize = _labelStyle.fontSize, normal = { textColor = new Color(0.3f, 1f, 0.5f) } };
            var clrSt = new GUIStyle(GUI.skin.button)
                { fontSize = _hintStyle.fontSize,  normal = { textColor = new Color(1f, 0.55f, 0.35f) } };

            bool canCreate = phase == IslandBuilder.Domain.Tools.BeachAltPhase.BothReady;
            GUI.enabled = canCreate;
            if (GUILayout.Button("Create Beach", createSt))
            {
                if (_undoManager != null && _terrainManager != null)
                {
                    int res = _terrainManager.Resolution;
                    var pre = _terrainManager.GetHeights(new RectInt(0, 0, res, res));
                    _undoManager.Push(new IslandBuilder.Domain.UndoEntry(pre, Vector2Int.zero, "beach"));
                }
                bt.CreateBeach();
            }
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (phase != IslandBuilder.Domain.Tools.BeachAltPhase.DrawingInner)
            {
                if (GUILayout.Button("Clear all", clrSt)) bt.ClearAll();
            }
            if (phase == IslandBuilder.Domain.Tools.BeachAltPhase.BothReady ||
                phase == IslandBuilder.Domain.Tools.BeachAltPhase.DrawingOuter)
            {
                if (GUILayout.Button("Redo outer", clrSt)) bt.ClearOuter();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBeachParams(IslandBuilder.Domain.Tools.BeachTool bt)
        {
            // Mode toggle: switch to two-curve alt mode
            if (GUILayout.Button("Curves mode →", new GUIStyle(GUI.skin.button)
                    { fontSize = _hintStyle.fontSize, normal = { textColor = Color.white } }))
                _toolRegistry?.SetActiveTool("beachalt");
            GUILayout.Space(4);

            string lassoStatus = bt.IsDrawing   ? "Drawing lasso…" :
                                  bt.HasLasso    ? "Lasso ready" :
                                                   "Drag to draw beach lasso";
            GUILayout.Label(lassoStatus, _hintStyle);
            GUILayout.Space(8);

            // ── Slope profile ─────────────────────────────────────────────────
            GUILayout.Label("Slope profile", _labelStyle);
            var slopeNames = new[] { "Flat", "Linear", "Steep Top", "Steep Base", "S-Curve" };
            int curSlope = (int)bt.Slope;
            int newSlope = GUILayout.SelectionGrid(curSlope, slopeNames, 2,
                new GUIStyle(_toolbarStyle) { fontSize = Mathf.RoundToInt(15f * Screen.height / RefHeight) });
            if (newSlope != curSlope)
            {
                bt.Slope = (IslandBuilder.Domain.Tools.BeachSlope)newSlope;
                bt.NotifySettingsChanged();
            }

            GUILayout.Space(10);

            // ── Actions ───────────────────────────────────────────────────────
            var createSt = new GUIStyle(GUI.skin.button)
            {
                fontSize = _labelStyle.fontSize,
                normal   = { textColor = new Color(0.3f, 1f, 0.5f) }
            };
            var clrSt = new GUIStyle(GUI.skin.button)
            {
                fontSize = _hintStyle.fontSize,
                normal   = { textColor = new Color(1f, 0.55f, 0.35f) }
            };

            GUI.enabled = bt.HasLasso;
            if (GUILayout.Button("Create Beach", createSt))
            {
                // Snapshot the terrain before applying so undo can revert exactly this step.
                if (_undoManager != null && _terrainManager != null)
                {
                    int res = _terrainManager.Resolution;
                    var pre = _terrainManager.GetHeights(new RectInt(0, 0, res, res));
                    _undoManager.Push(new IslandBuilder.Domain.UndoEntry(pre, Vector2Int.zero, "beach"));
                }
                bt.CreateBeach();
            }
            GUI.enabled = true;

            if (bt.HasPolygon)
            {
                if (GUILayout.Button("Clear lasso", clrSt))
                    bt.ClearLasso();
            }
        }

        private void DrawMeasureToolParams(MeasureTool mt)
        {
            float unitSc   = _gridRenderer != null && _gridRenderer.UnitScale > 0f
                ? _gridRenderer.UnitScale : 1f;
            string unitNm  = _gridRenderer?.UnitName ?? "m";

            // ── Mode selector ─────────────────────────────────────────────────
            int modeIdx = GUILayout.Toolbar((int)mt.Mode,
                new[] { "Distance", "Area" }, _toolbarStyle);
            if (modeIdx != (int)mt.Mode)
            {
                mt.Mode = (MeasureMode)modeIdx;
                mt.ClearArea();
            }

            GUILayout.Space(8);

            // ── Live readouts (all values in terrain units) ───────────────────
            if (mt.Mode == MeasureMode.Distance)
            {
                if (mt.IsDragging)
                {
                    GUILayout.Label($"Length: {mt.ActiveLineMetres / unitSc:F1} {unitNm}", _labelStyle);
                }
                else if (mt.HasHover)
                {
                    GUILayout.Label($"Height: {mt.HoverHeight / unitSc:F1} {unitNm}", _labelStyle);
                    GUILayout.Label($"Δ sea:  {mt.HoverDeltaSea / unitSc:+0.#;-0.#;0} {unitNm}", _hintStyle);
                }
                else
                {
                    GUILayout.Label("Hover for elevation", _hintStyle);
                }
            }
            else // Area
            {
                if (mt.HasHover)
                {
                    GUILayout.Label($"Height: {mt.HoverHeight / unitSc:F1} {unitNm}", _labelStyle);
                    GUILayout.Label($"Δ sea:  {mt.HoverDeltaSea / unitSc:+0.#;-0.#;0} {unitNm}", _hintStyle);
                }

                GUILayout.Space(6);

                if (mt.HasArea)
                {
                    float a = mt.AreaSqMetres / (unitSc * unitSc);
                    string u2 = $"{unitNm}²";
                    string areaStr = a >= 1_000_000f
                        ? $"{a / 1_000_000f:F3} k{u2}"
                        : $"{a:F0} {u2}";
                    GUILayout.Label($"Area: {areaStr}", _labelStyle);

                    var resetSt = new GUIStyle(GUI.skin.button)
                        { fontSize = _hintStyle.fontSize, normal = { textColor = Color.white } };
                    if (GUILayout.Button("Clear area", resetSt))
                        mt.ClearArea();
                }
                else
                {
                    GUILayout.Label("Drag to trace area", _hintStyle);
                }
            }
        }

        private void DrawDredgeParams(FillTool dredge)
        {
            float maxH = Mathf.Max(1f, dredge.MaxHeightMetres);
            float curH = dredge.TargetMetres;

            GUILayout.Label(dredge.HasCustomTarget
                ? $"Dredge height: {curH:F1} m"
                : $"Dredge height: sea level ({curH:F1} m)", _labelStyle);

            float newH = GUILayout.HorizontalSlider(curH, 0f, maxH);
            if (!Mathf.Approximately(newH, curH)) dredge.SetTarget(newH);

            var resetSt = new GUIStyle(GUI.skin.button)
                { fontSize = _hintStyle.fontSize, normal = { textColor = Color.white } };
            if (GUILayout.Button("Reset to sea level", resetSt))
                dredge.ResetToSeaLevel();

            GUILayout.Label("Ctrl+click terrain to sample height", _hintStyle);
        }

        private void DrawSharpnessSlider(BrushToolBase brush)
        {
            GUILayout.Space(8);
            GUILayout.Label($"Sharpness: {brush.Sharpness * 100f:F0}%", _labelStyle);
            float sh = GUILayout.HorizontalSlider(brush.Sharpness, 0f, 1f);
            if (Mathf.Abs(sh - brush.Sharpness) > 0.001f) brush.Sharpness = sh;
        }

        private void DrawRaiseParams(RaiseTool rt)
        {
            // ── Mode selector ─────────────────────────────────────────────────
            int newMode = GUILayout.Toolbar((int)rt.Mode,
                new[] { "Smooth", "Shape" }, _toolbarStyle);
            if (newMode != (int)rt.Mode) rt.Mode = (RaiseMode)newMode;

            GUILayout.Space(10);

            // ── Vertical strength (both modes) ────────────────────────────────
            GUILayout.Label($"Vertical: {rt.VerticalStrength * 100f:F0}%", _labelStyle);
            float v = GUILayout.HorizontalSlider(rt.VerticalStrength, 0.01f, 1f);
            if (Mathf.Abs(v - rt.VerticalStrength) > 0.001f) rt.VerticalStrength = v;

            GUILayout.Space(10);

            DrawSharpnessSlider(rt);

            if (rt.Mode == RaiseMode.Smooth)
            {
            }
            else
            {
                // ── Sharpness ─────────────────────────────────────────────────
                GUILayout.Label($"Sharpness: {rt.Sharpness * 100f:F0}%", _labelStyle);
                float sh = GUILayout.HorizontalSlider(rt.Sharpness, 0f, 1f);
                if (Mathf.Abs(sh - rt.Sharpness) > 0.001f) rt.Sharpness = sh;

                GUILayout.Space(10);

                // ── Shape selector ────────────────────────────────────────────
                GUILayout.Label("Shape", _labelStyle);
                int newShape = GUILayout.Toolbar((int)rt.Shape,
                    new[] { "Circle", "Square", "Star", "Triangle" }, _toolbarStyle);
                if (newShape != (int)rt.Shape) rt.Shape = (BrushShape)newShape;

                // ── Star point count ──────────────────────────────────────────
                if (rt.Shape == BrushShape.Star)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Points: {rt.StarPoints}", _labelStyle);
                    float p = GUILayout.HorizontalSlider(rt.StarPoints, 3f, 10f);
                    int pi  = Mathf.RoundToInt(p);
                    if (pi != rt.StarPoints) rt.StarPoints = pi;
                }
            }
        }

        private void DrawEraseShapeParams(EraseTool et)
        {
            GUILayout.Space(10);
            GUILayout.Label("Shape", _labelStyle);
            int newShape = GUILayout.Toolbar((int)et.Shape,
                new[] { "Circle", "Square", "Star", "Triangle" }, _toolbarStyle);
            if (newShape != (int)et.Shape) et.Shape = (BrushShape)newShape;

            if (et.Shape == BrushShape.Star)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Points: {et.StarPoints}", _labelStyle);
                float p  = GUILayout.HorizontalSlider(et.StarPoints, 3f, 10f);
                int   pi = Mathf.RoundToInt(p);
                if (pi != et.StarPoints) et.StarPoints = pi;
            }
        }

        // ── Stats (bottom-right of toolbar, below all buttons/params) ─────────

        private void DrawStats()
        {
            if (!_hasStats) return;

            float scale = Screen.height / RefHeight;
            float x     = 8f * scale;
            float w     = 172f * scale;
            float lh    = 26f * scale; // line height
            float pad   = 8f  * scale;
            float totalH = lh * 3 + pad * 2;
            float y     = Screen.height - totalH - 8f * scale;

            // Dark background strip
            GUI.color = new Color(0.10f, 0.10f, 0.10f, 0.82f);
            GUI.DrawTexture(new Rect(x - pad * 0.5f, y - pad * 0.5f,
                                     w + pad, totalH + pad), Texture2D.whiteTexture);
            GUI.color = new Color(0.30f, 0.30f, 0.30f, 1f);
            GUI.DrawTexture(new Rect(x - pad * 0.5f, y - pad * 0.5f,
                                     w + pad, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y,            w, lh), $"Volume:  {_volume:N0} m³",          _statsStyle);
            GUI.Label(new Rect(x, y + lh,       w, lh), $"Mass:    {_massKg / 1000f:N0} t",   _statsStyle);
            GUI.Label(new Rect(x, y + lh * 2f,  w, lh), $"Cost:    ${_costUsd:N0}",           _statsStyle);
        }

        // ── Scale dialog (shown when a DXF has no unit information) ──────────────

        private bool           _scaleDialogActive;
        private System.Action<float> _scaleCallback;
        private string         _customScaleText = "1";
        private int            _scalePreset     = -1; // -1 = none selected yet

        public void ShowScaleDialog(string detectedUnit, System.Action<float> callback)
        {
            _scaleCallback     = callback;
            _scaleDialogActive = true;
            _customScaleText   = "1";
            _scalePreset       = -1;
        }

        private void DrawScaleDialog()
        {
            if (!_scaleDialogActive) return;

            float scale  = Screen.height / RefHeight;
            float w      = 480f * scale;
            float h      = 300f * scale;
            float x      = (Screen.width  - w) * 0.5f;
            float y      = (Screen.height - h) * 0.5f;
            float pad    = 14f * scale;
            float btnH   = 36f * scale;
            int   lfs    = Mathf.RoundToInt(20f * scale);

            // Background + border
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.97f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.50f, 0.50f, 0.50f, 1f);
            GUI.DrawTexture(new Rect(x, y, w, 1f),       Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + h - 1f, w, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var titleSt  = new GUIStyle(GUI.skin.label)
                { fontSize = Mathf.RoundToInt(22f * scale), fontStyle = FontStyle.Bold,
                  normal = { textColor = new Color(0.85f, 0.90f, 1f) } };
            var msgSt    = new GUIStyle(GUI.skin.label)
                { fontSize = lfs, wordWrap = true,
                  normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
            var btnSt    = new GUIStyle(GUI.skin.button) { fontSize = lfs };
            var activeSt = new GUIStyle(btnSt)
                { normal = { textColor = Color.white,
                             background = MakeTex(new Color(0.25f, 0.50f, 0.80f)) } };
            var fieldSt  = new GUIStyle(GUI.skin.textField) { fontSize = lfs };
            var confirmSt = new GUIStyle(btnSt)
                { normal = { textColor = Color.white,
                             background = MakeTex(new Color(0.20f, 0.55f, 0.20f)) } };

            float cx = x + pad;
            float cy = y + pad;
            float fw = w - pad * 2f;

            GUI.Label(new Rect(cx, cy, fw, btnH), "Unknown DXF Units", titleSt);
            cy += btnH;
            GUI.Label(new Rect(cx, cy, fw, btnH * 1.5f),
                "The file contains no unit information. Select the unit used in this file:", msgSt);
            cy += btnH * 1.5f + pad * 0.5f;

            // Preset unit buttons — 4 across
            float bw = (fw - pad * 3f) / 4f;
            string[] labels = { "Metres", "Feet", "Centimetres", "Millimetres" };
            float[]  scales = { 1f,       0.3048f, 0.01f,        0.001f };
            for (int i = 0; i < 4; i++)
            {
                var st = (_scalePreset == i) ? activeSt : btnSt;
                if (GUI.Button(new Rect(cx + i * (bw + pad), cy, bw, btnH), labels[i], st))
                {
                    _scalePreset     = i;
                    _customScaleText = scales[i].ToString("G");
                }
            }
            cy += btnH + pad;

            // Custom scale field
            GUI.Label(new Rect(cx, cy, fw * 0.35f, btnH),
                "Custom (m/unit):", new GUIStyle(msgSt) { normal = { textColor = Color.white } });
            _customScaleText = GUI.TextField(
                new Rect(cx + fw * 0.37f, cy, fw * 0.30f, btnH), _customScaleText, fieldSt);
            cy += btnH + pad;

            // Confirm
            if (GUI.Button(new Rect(cx, cy, fw, btnH), "Confirm", confirmSt))
            {
                if (!float.TryParse(_customScaleText,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float chosen))
                    chosen = 1f;
                chosen = Mathf.Max(0.000001f, chosen);
                _scaleDialogActive = false;
                _scaleCallback?.Invoke(chosen);
                _scaleCallback = null;
            }
        }

        // ── Global tool settings panel (bottom-right) ─────────────────────────

        private void DrawGlobalSettings()
        {
            if (_globalSettings == null) return;

            float scale  = Screen.height / RefHeight;
            float lh     = 28f * scale;
            float pad    = 6f  * scale;
            float panelW = 265f * scale;

            // Height: title + edit-above toggle + (if enabled: slider + field row + sample btn) + FEL toggle
            float rows = 3f;
            if (_globalSettings.EditAboveEnabled) rows += 3f;
            float panelH = rows * (lh + 4f * scale) + pad * 2f;

            float panelX = Screen.width  - panelW - 8f * scale;
            float panelY = Screen.height - panelH - 8f * scale;

            GUI.color = new Color(0.10f, 0.10f, 0.10f, 0.88f);
            GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panelX + pad, panelY + pad,
                                         panelW - pad * 2f, panelH - pad * 2f));

            int lfs  = Mathf.RoundToInt(18f * scale);
            int tfs  = Mathf.RoundToInt(20f * scale);
            int sfs  = Mathf.RoundToInt(15f * scale);
            var titleSt = new GUIStyle(_titleStyle) { fontSize = tfs };
            var lblSt   = new GUIStyle(_labelStyle) { fontSize = lfs };
            var hintSt2 = new GUIStyle(_hintStyle)  { fontSize = sfs };
            var fieldSt = new GUIStyle(GUI.skin.textField) { fontSize = lfs };
            var btnSt   = new GUIStyle(GUI.skin.button)    { fontSize = lfs, normal = { textColor = Color.white } };

            GUILayout.Label("Global Settings", titleSt);

            // ── Edit Above Height ─────────────────────────────────────────────
            bool newEA = GUILayout.Toggle(_globalSettings.EditAboveEnabled,
                $"  Edit Above: {(_globalSettings.EditAboveEnabled ? $"{_globalSettings.EditAboveHeight:F0} m" : "off")}");
            if (newEA != _globalSettings.EditAboveEnabled)
            {
                _globalSettings.EditAboveEnabled = newEA;
                if (newEA) _editAboveText = _globalSettings.EditAboveHeight.ToString("F0");
            }

            if (_globalSettings.EditAboveEnabled)
            {
                float maxH = Mathf.Max(1f, _terrainManager?.WorldSize.y ?? 1000f);
                float newH = GUILayout.HorizontalSlider(_globalSettings.EditAboveHeight, 0f, maxH);
                if (!Mathf.Approximately(newH, _globalSettings.EditAboveHeight))
                {
                    _globalSettings.EditAboveHeight = newH;
                    _editAboveText = newH.ToString("F0");
                }
                GUILayout.BeginHorizontal();
                _editAboveText = GUILayout.TextField(_editAboveText, fieldSt,
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Set", btnSt, GUILayout.Width(44f * scale)))
                {
                    if (float.TryParse(_editAboveText,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float v))
                        _globalSettings.EditAboveHeight = Mathf.Clamp(v, 0f, maxH);
                    _editAboveText = _globalSettings.EditAboveHeight.ToString("F0");
                }
                GUILayout.EndHorizontal();

                // ── Sample button ─────────────────────────────────────────────
                bool sampling = _globalSettings.IsSamplingEditAbove;
                var prevColor = GUI.color;
                if (sampling) GUI.color = new Color(1f, 0.85f, 0.2f); // yellow when active
                if (GUILayout.Button(sampling ? "Ctrl+click terrain to sample…" : "Sample from terrain", btnSt))
                    _globalSettings.IsSamplingEditAbove = !sampling;
                GUI.color = prevColor;
            }

            // ── Fill Entire Lasso ─────────────────────────────────────────────
            bool newFEL = GUILayout.Toggle(_globalSettings.FillEntireLasso,
                "  Fill Entire Lasso");
            if (newFEL != _globalSettings.FillEntireLasso)
                _globalSettings.FillEntireLasso = newFEL;

            GUILayout.EndArea();
        }

        // ── Tooltip (hovers near the cursor over top-bar buttons) ─────────────

        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(_tooltipText)) return;

            float scale = Screen.height / RefHeight;
            float pad   = 8f * scale;
            float fs    = 19f * scale;

            // Measure text to size the box
            _tooltipStyle.fontSize = Mathf.RoundToInt(fs);
            Vector2 size = _tooltipStyle.CalcSize(new GUIContent(_tooltipText));
            size.x += pad * 2;
            size.y += pad;

            float tx = Mathf.Clamp(_tooltipScreenPos.x, 0, Screen.width  - size.x);
            float ty = _tooltipScreenPos.y + 28f * scale; // appear just below cursor
            ty = Mathf.Clamp(ty, 0, Screen.height - size.y);

            // Box background
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.93f);
            GUI.DrawTexture(new Rect(tx, ty, size.x, size.y), Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            GUI.DrawTexture(new Rect(tx, ty, size.x, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(tx + pad, ty + pad * 0.5f,
                               size.x - pad * 2, size.y), _tooltipText, _tooltipStyle);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Rect GetParamAreaRect()
        {
            float scale = Screen.height / RefHeight;
            float y     = (ButtonsEndLogY() + 12f) * scale;
            float w     = 164f * scale;
            // Reserve space for 3 stats lines at the bottom (≈ 90px at 1080p).
            float statsH = 90f * scale;
            float h      = Mathf.Max(10f, Screen.height - y - statsH - 10f);
            return new Rect(8f * scale, y, w, h);
        }

        private static Texture2D MakeTex(Color color)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        private static string ToolDescription(string id) => id switch
        {
            "camera"  => "Navigate the scene without making any edits.",
            "raise"   => "Smooth: gradient falloff from centre.\nShape: choose circle, square, or star.\nSharpness controls edge hardness.",
            "erase"   => "Lower terrain back toward the imported baseline.",
            "flatten" => "Blend terrain gradually toward a sampled elevation.\nCtrl+click to set target. Direction: Both/Fill/Cut controls which side moves.",
            "smooth"  => "Average neighbours to soften sharp edges.",
            "blend"   => "Drag from one area into another to smear heights across the boundary.",
            "dredge"  => "Raise cells below the dredge height to that height.\nDefaults to sea level. Ctrl+click or slider to set a custom height.",
            "clear"   => "Snap cells to baseline. With active lasso: clears the whole selection in one click.",
            "lasso"   => "Drag to draw a freehand selection.\nToggle Inside/Outside to control where tools apply.",
            "fill"    => "Sets terrain to a sampled height within the brush (or entire lasso with Fill Entire Lasso).\nCtrl+click to sample. Direction: Fill = raise, Cut = lower, Both = snap.",
            "measure"    => "Hover to read elevation.\nClick and drag to measure distance.\nLines persist on screen.",
            "beach"      => "Drag to draw a lasso around a coastline area.\nAdjust height and slope, then click Create Beach.",
            "beachalt"   => "Draw two curves: inner (crest) and outer (water boundary).\nEdit anchor handles freely, then Create Beach.",
            "grid"    => "Reference grid overlay. Use the Show/Hide toggle\nto control visibility and adjust spacing here.",
            _         => string.Empty
        };

        private static string DisplayName(string id) => id switch
        {
            "camera"  => "Camera",
            "raise"   => "Raise",
            "erase"   => "Erase",
            "flatten" => "Flatten",
            "smooth"  => "Smooth",
            "blend"   => "Blend",
            "fill"    => "Set Height",
            "dredge"  => "Dredge",
            "clear"   => "Clear",
            "lasso"   => "Lasso",
            "measure"    => "Measure",
            "beach"      => "Beach",
            "beachalt"   => "Beach (Curves)",
            "grid"    => "Grid",
            _         => id
        };
    }
}
