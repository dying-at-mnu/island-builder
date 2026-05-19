using System;
using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Raises cells below the dredge height to that height.
    /// Default dredge height = sea level. Ctrl+click or slider to set a custom height.
    /// </summary>
    public class FillTool : BrushToolBase
    {
        public override string ToolId      => "dredge";
        public override bool   HasStrength => false;

        private readonly Action<string> _display;
        private float _targetNorm   = 0f;
        private float _targetMetres = 0f;
        private bool  _customTarget = false;

        public bool  HasCustomTarget  => _customTarget;
        public float TargetMetres     => _targetMetres;
        public float MaxHeightMetres  => _reader.WorldSize.y;

        public FillTool(ITerrainReader reader, ITerrainWriter writer,
                        Action<string> displayCallback = null) : base(reader, writer)
        {
            _display = displayCallback;
        }

        public void SetTarget(float metres)
        {
            float displayY  = _reader.WorldSize.y;
            _targetMetres   = Mathf.Clamp(metres, 0f, displayY);
            _targetNorm     = displayY > 0f ? _targetMetres / displayY : 0f;
            _customTarget   = true;
            _display?.Invoke($"Dredge: {_targetMetres:F1} m  (Ctrl+click or slider)");
        }

        public void ResetToSeaLevel()
        {
            _customTarget = false;
            UpdateSeaLevelTarget();
        }

        private void UpdateSeaLevelTarget()
        {
            float displayY  = _reader.WorldSize.y;
            _targetMetres   = _reader.SeaLevelOffset;
            _targetNorm     = displayY > 0f ? _targetMetres / displayY : 0f;
        }

        public override void OnActivate()
        {
            if (!_customTarget) UpdateSeaLevelTarget();
            _display?.Invoke(_customTarget
                ? $"Dredge: {_targetMetres:F1} m  (Ctrl+click or slider)"
                : $"Dredge: sea level ({_targetMetres:F1} m)");
        }

        public override void OnDeactivate() => _display?.Invoke(string.Empty);

        public override void OnMouseDown(RaycastHit hit)
        {
            float cw = _reader.CellWidth, cl = _reader.CellLength;
            int hx = Mathf.Clamp(Mathf.RoundToInt(hit.point.x / cw), 0, _reader.Resolution - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt(hit.point.z / cl), 0, _reader.Resolution - 1);
            SetTarget(_reader.GetHeights(new RectInt(hx, hz, 1, 1))[0, 0] * _reader.WorldSize.y);
        }

        public override void OnMouseHeld(RaycastHit hit)
        {
            if (!_customTarget) UpdateSeaLevelTarget();
            if (_targetNorm <= 0f) return;

            float aboveNorm = EditAboveNorm;

            // â”€â”€ Fill Entire Lasso mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (FillEntireLassoActive && GetLassoRegion(out RectInt lassoReg))
            {
                float[,] h = _reader.GetHeights(lassoReg);
                bool changed = false;
                for (int z = 0; z < lassoReg.height; z++)
                for (int x = 0; x < lassoReg.width; x++)
                {
                    if (!IsInsideLasso(lassoReg, x, z)) continue;
                    float cur = h[z, x];
                    if (aboveNorm > 0f && cur < aboveNorm) continue;
                    if (cur < _targetNorm) { h[z, x] = _targetNorm; changed = true; }
                }
                if (changed) _writer.SetHeights(h, new Vector2Int(lassoReg.x, lassoReg.y));
                return;
            }

            // â”€â”€ Brush mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var region  = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] bh = _reader.GetHeights(region);
            int rows = bh.GetLength(0), cols = bh.GetLength(1);
            bool anyChanged = false;

            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f || !WithinLasso(region.x + x, region.y + z)) continue;
                float cur = bh[z, x];
                if (aboveNorm > 0f && cur < aboveNorm) continue;
                if (cur < _targetNorm) { bh[z, x] = _targetNorm; anyChanged = true; }
            }

            if (anyChanged) _writer.SetHeights(bh, new Vector2Int(region.x, region.y));
        }
    }
}
