using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Shared base for all circular-brush tools. Handles radius/strength properties,
    /// the WorldToHeightmapRect helper, and a standard parameter panel with sliders.
    /// </summary>
    public abstract class BrushToolBase : ITool
    {
        protected readonly ITerrainReader _reader;
        protected readonly ITerrainWriter _writer;

        public float     BrushRadius { get; set; } = 70f;
        public float     Strength    { get; set; } = 0.3f;

        /// <summary>
        /// When set, every brush operation is restricted to cells inside the lasso
        /// polygon. Null or HasSelection==false = no restriction.
        /// </summary>
        public LassoTool Lasso { get; set; }

        /// <summary>Global settings shared across all tools.</summary>
        public GlobalToolSettings GlobalSettings { get; set; }

        /// <summary>
        /// Normalised edit-above threshold. Cells at or below this value are skipped.
        /// Returns 0 (no restriction) when the setting is disabled or no terrain loaded.
        /// </summary>
        protected float EditAboveNorm =>
            GlobalSettings != null && GlobalSettings.EditAboveEnabled && _reader.WorldSize.y > 0f
                ? GlobalSettings.EditAboveHeight / _reader.WorldSize.y
                : 0f;

        /// <summary>True when FillEntireLasso is on and an active lasso selection exists.</summary>
        protected bool FillEntireLassoActive =>
            GlobalSettings != null && GlobalSettings.FillEntireLasso &&
            Lasso != null && Lasso.HasSelection;

        /// <summary>Returns the heightmap RectInt covering the full active lasso bounds.</summary>
        protected bool GetLassoRegion(out RectInt region)
        {
            region = default;
            if (Lasso == null || !Lasso.GetBounds(
                    out float minX, out float maxX, out float minZ, out float maxZ)) return false;
            float cw = _reader.CellWidth, cl = _reader.CellLength;
            int   res = _reader.Resolution;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX / cw), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt (maxX / cw), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(minZ / cl), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt (maxZ / cl), 0, res - 1);
            if (x1 < x0 || z1 < z0) return false;
            region = new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            return true;
        }

        protected bool IsInsideLasso(RectInt region, int lx, int lz)
            => Lasso != null &&
               Lasso.IsInsideLasso((region.x + lx) * _reader.CellWidth,
                                   (region.y + lz) * _reader.CellLength);

        /// <summary>
        /// Edge sharpness. 0 = linear falloff from centre to edge (soft).
        /// 1 = full effect across the entire radius with no taper (hard).
        /// </summary>
        public float Sharpness { get; set; } = 0f;

        public abstract string ToolId      { get; }
        public virtual  bool   AlwaysUpdate => false;
        public virtual  bool   HasStrength  => true;

        protected BrushToolBase(ITerrainReader reader, ITerrainWriter writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public virtual void OnActivate()   { }
        public virtual void OnDeactivate() { }
        public virtual void OnMouseDown(RaycastHit hit) { }
        public virtual void OnMouseUp()    { }
        public abstract void OnMouseHeld(RaycastHit hit);

        // ── Helpers ───────────────────────────────────────────────────────────

        protected RectInt WorldToHeightmapRect(Vector3 worldPos, float radiusMetres)
        {
            if (_reader.Resolution <= 1) return new RectInt(0, 0, 0, 0);
            float cw = _reader.CellWidth;
            float cl = _reader.CellLength;
            int   cx = Mathf.RoundToInt(worldPos.x / cw);
            int   cz = Mathf.RoundToInt(worldPos.z / cl);
            int   r  = Mathf.CeilToInt(radiusMetres / Mathf.Min(cw, cl));
            int   x0 = Mathf.Clamp(cx - r, 0, _reader.Resolution - 1);
            int   z0 = Mathf.Clamp(cz - r, 0, _reader.Resolution - 1);
            int   x1 = Mathf.Clamp(cx + r, 0, _reader.Resolution - 1);
            int   z1 = Mathf.Clamp(cz + r, 0, _reader.Resolution - 1);
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }

        /// <summary>
        /// Returns true when (hx, hz) is inside the active lasso, or when no lasso
        /// selection is active (unrestricted brushing).
        /// </summary>
        protected bool WithinLasso(int hx, int hz)
        {
            if (Lasso == null || !Lasso.HasSelection) return true;
            bool inside = Lasso.IsInsideLasso(hx * _reader.CellWidth, hz * _reader.CellLength);
            // XOR: InvertSelection=true flips the test so tools affect the outside.
            return inside != Lasso.InvertSelection;
        }

        /// <summary>
        /// Falloff in [0,1] for a cell at normalised distance d from the brush centre,
        /// shaped by the current Sharpness setting.
        /// Sharpness=0 → linear (1-d). Sharpness=1 → flat 1 everywhere inside radius.
        /// </summary>
        protected float SharpFalloff(float d) =>
            Sharpness >= 1f ? 1f
            : Mathf.Clamp01((1f - d) / Mathf.Max(0.001f, 1f - Sharpness));

        /// <summary>Distance [0..1] from heightmap cell (hx, hz) to worldPos / BrushRadius.</summary>
        protected float CellDistNorm(int hx, int hz, Vector3 worldPos)
        {
            float dx = hx * _reader.CellWidth  - worldPos.x;
            float dz = hz * _reader.CellLength - worldPos.z;
            return Mathf.Sqrt(dx * dx + dz * dz) / BrushRadius;
        }

        // ── Parameter panel ───────────────────────────────────────────────────

        // Parameters are rendered by ToolParameterGUI (OnGUI) so UI Toolkit is not needed.
        public virtual VisualElement GetParameterPanel() => null;

        protected static VisualElement MakeSlider(string label, float min, float max,
            float value, Action<float> onChange, string unit, float scale = 1f)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.width         = Length.Percent(100);
            row.style.marginBottom  = 10;

            string FormatVal(float v) => unit.Length > 0 ? $"{v * scale:F0}{unit}" : $"{v:F2}";

            // IMGUIContainer renders text unconditionally — no theme or font asset needed
            float current = value;
            string[] headerText = { $"{label}:  {FormatVal(value)}" };
            GUIStyle[] labelStyle = { null };
            var headerEl = new IMGUIContainer(() =>
            {
                labelStyle[0] ??= new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.white } };
                GUILayout.Label(headerText[0], labelStyle[0]);
            });
            headerEl.style.height = 18;
            headerEl.style.width  = Length.Percent(100);

            var track = new VisualElement();
            track.style.height          = 8;
            track.style.width           = Length.Percent(100);
            track.style.marginTop       = 4;
            track.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
            track.style.borderTopLeftRadius     = track.style.borderTopRightRadius    = 4;
            track.style.borderBottomLeftRadius  = track.style.borderBottomRightRadius = 4;

            var fill = new VisualElement();
            fill.style.position        = Position.Absolute;
            fill.style.top             = 0;
            fill.style.bottom          = 0;
            fill.style.left            = 0;
            fill.style.backgroundColor = new Color(0.25f, 0.55f, 0.85f, 1f);
            fill.style.borderTopLeftRadius     = fill.style.borderTopRightRadius    = 4;
            fill.style.borderBottomLeftRadius  = fill.style.borderBottomRightRadius = 4;

            var thumb = new VisualElement();
            thumb.style.position        = Position.Absolute;
            thumb.style.width           = 12;
            thumb.style.height          = 16;
            thumb.style.top             = -4;
            thumb.style.backgroundColor = new Color(0.80f, 0.92f, 1f, 1f);
            thumb.style.borderTopLeftRadius     = thumb.style.borderTopRightRadius    = 3;
            thumb.style.borderBottomLeftRadius  = thumb.style.borderBottomRightRadius = 3;

            void UpdateVisuals(float v, float trackWidth)
            {
                float t = Mathf.Clamp01((v - min) / (max - min));
                fill.style.width = Length.Percent(t * 100f);
                thumb.style.left = t * Mathf.Max(0f, trackWidth - 12f);
            }

            track.Add(fill);
            track.Add(thumb);

            track.RegisterCallback<GeometryChangedEvent>(evt =>
                UpdateVisuals(current, evt.newRect.width));

            bool dragging = false;
            track.RegisterCallback<PointerDownEvent>(evt =>
            {
                dragging = true;
                track.CapturePointer(evt.pointerId);
                float t = Mathf.Clamp01(evt.localPosition.x / track.resolvedStyle.width);
                current = min + t * (max - min);
                onChange(current);
                headerText[0] = $"{label}:  {FormatVal(current)}";
                UpdateVisuals(current, track.resolvedStyle.width);
                evt.StopPropagation();
            });

            track.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                float t = Mathf.Clamp01(evt.localPosition.x / track.resolvedStyle.width);
                current = min + t * (max - min);
                onChange(current);
                headerText[0] = $"{label}:  {FormatVal(current)}";
                UpdateVisuals(current, track.resolvedStyle.width);
                evt.StopPropagation();
            });

            track.RegisterCallback<PointerUpEvent>(evt =>
            {
                dragging = false;
                track.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            row.Add(headerEl);
            row.Add(track);
            return row;
        }
    }
}
