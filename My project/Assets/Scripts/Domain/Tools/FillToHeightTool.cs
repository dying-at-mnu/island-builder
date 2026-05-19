using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// With Fill Entire Lasso ON: fills the whole lasso (or terrain if no lasso) in one click.
    /// With Fill Entire Lasso OFF: brush-based fill that raises/lowers cells within the brush radius.
    /// Ctrl+click samples the target height; slider also sets it.
    /// Direction: Fill = raise only, Cut = lower only, Both = snap exactly.
    /// </summary>
    public class FillToHeightTool : BrushToolBase
    {
        public override string ToolId      => "fill";
        public override bool   HasStrength => false;

        private readonly Action<string> _display;
        private float _targetNorm    = 0f;
        private float _targetMetres  = 0f;
        private bool  _targetSampled = false;
        private bool  _filledThisStroke;

        public bool       TargetSampled   => _targetSampled;
        public float      TargetMetres    => _targetMetres;
        public float      MaxHeightMetres => _reader.WorldSize.y;
        public FlattenDir Direction       { get; set; } = FlattenDir.Fill;

        public FillToHeightTool(ITerrainReader reader, ITerrainWriter writer,
                                Action<string> displayCallback = null)
            : base(reader, writer)
        {
            _display = displayCallback;
        }

        public void SetTarget(float metres)
        {
            float displayY = _reader.WorldSize.y;
            _targetMetres  = Mathf.Clamp(metres, 0f, displayY);
            _targetNorm    = displayY > 0f ? _targetMetres / displayY : 0f;
            _targetSampled = true;
            _display?.Invoke($"Fill: {_targetMetres:F1} m  (Ctrl+click or slider)");
        }

        public override void OnActivate()
        {
            if (!_targetSampled)
            {
                float displayY = _reader.WorldSize.y;
                _targetMetres  = _reader.SeaLevelOffset;
                _targetNorm    = displayY > 0f ? _targetMetres / displayY : 0f;
                _targetSampled = true;
            }
            _display?.Invoke($"Fill: {_targetMetres:F1} m  (Ctrl+click or slider)");
        }

        public override void OnDeactivate()
        {
            _filledThisStroke = false;
            _display?.Invoke(string.Empty);
        }

        public override void OnMouseUp() => _filledThisStroke = false;

        public override void OnMouseDown(RaycastHit hit)
        {
            float cw = _reader.CellWidth, cl = _reader.CellLength;
            int hx = Mathf.Clamp(Mathf.RoundToInt(hit.point.x / cw), 0, _reader.Resolution - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt(hit.point.z / cl), 0, _reader.Resolution - 1);
            SetTarget(_reader.GetHeights(new RectInt(hx, hz, 1, 1))[0, 0] * _reader.WorldSize.y);
        }

        public override void OnMouseHeld(RaycastHit hit)
        {
            if (!_targetSampled) return;

            bool doRaise  = Direction != FlattenDir.Cut;
            bool doLower  = Direction != FlattenDir.Fill;
            float aboveNorm = EditAboveNorm;

            // â”€â”€ Fill Entire Lasso mode â€” one-shot â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (GlobalSettings?.FillEntireLasso == true)
            {
                if (_filledThisStroke) return;
                _filledThisStroke = true;
                if (Lasso?.HasSelection == true)
                    FillRegion(GetLassoRectAndFilter(), doRaise, doLower, aboveNorm, lassoOnly: true);
                else
                    FillAll(doRaise, doLower, aboveNorm);
                return;
            }

            // â”€â”€ Brush mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var region = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] h = _reader.GetHeights(region);
            int rows = h.GetLength(0), cols = h.GetLength(1);
            bool changed = false;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f || !WithinLasso(region.x + x, region.y + z)) continue;
                float cur = h[z, x];
                if (aboveNorm > 0f && cur < aboveNorm) continue;
                if      (cur < _targetNorm && doRaise) { h[z, x] = _targetNorm; changed = true; }
                else if (cur > _targetNorm && doLower) { h[z, x] = _targetNorm; changed = true; }
            }
            if (changed) _writer.SetHeights(h, new Vector2Int(region.x, region.y));
        }

        private (RectInt region, float[,] h) GetLassoRectAndFilter()
        {
            if (!GetLassoRegion(out RectInt region)) return (default, null);
            return (region, _reader.GetHeights(region));
        }

        private void FillRegion((RectInt region, float[,] h) data,
                                bool doRaise, bool doLower, float aboveNorm, bool lassoOnly)
        {
            var (region, h) = data;
            if (h == null) return;
            bool changed = false;
            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++)
            {
                if (lassoOnly && !IsInsideLasso(region, x, z)) continue;
                float cur = h[z, x];
                if (aboveNorm > 0f && cur < aboveNorm) continue;
                if      (cur < _targetNorm && doRaise) { h[z, x] = _targetNorm; changed = true; }
                else if (cur > _targetNorm && doLower) { h[z, x] = _targetNorm; changed = true; }
            }
            if (changed) _writer.SetHeights(h, new Vector2Int(region.x, region.y));
        }

        private void FillAll(bool doRaise, bool doLower, float aboveNorm)
        {
            int res = _reader.Resolution;
            var region = new RectInt(0, 0, res, res);
            float[,] h = _reader.GetHeights(region);
            bool changed = false;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float cur = h[z, x];
                if (aboveNorm > 0f && cur < aboveNorm) continue;
                if      (cur < _targetNorm && doRaise) { h[z, x] = _targetNorm; changed = true; }
                else if (cur > _targetNorm && doLower) { h[z, x] = _targetNorm; changed = true; }
            }
            if (changed) _writer.SetHeights(h, new Vector2Int(0, 0));
        }

        public override VisualElement GetParameterPanel() => null;
    }
}
