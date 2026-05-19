using System;
using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    public enum FlattenDir { Both, Fill, Cut }  // Both = raise+lower, Fill = raise only, Cut = lower only

    public class FlattenTool : BrushToolBase
    {
        public override string ToolId => "flatten";

        private readonly Action<string> _display;
        private float _targetNorm    = 0f;
        private float _targetMetres  = 0f;
        private bool  _targetSampled = false;

        public bool       TargetSampled => _targetSampled;
        public float      TargetMetres  => _targetMetres;
        public FlattenDir Dir           { get; set; } = FlattenDir.Both;

        public FlattenTool(ITerrainReader reader, ITerrainWriter writer,
                           Action<string> displayCallback = null) : base(reader, writer)
        {
            _display = displayCallback;
            Strength = 0.8f;
        }

        public override void OnActivate()
        {
            _display?.Invoke(_targetSampled
                ? $"Flatten: {_targetMetres:F1} m  (Ctrl+click to change)"
                : "Flatten: none  (Ctrl+click to set)");
        }

        public override void OnDeactivate() => _display?.Invoke(string.Empty);

        public override void OnMouseDown(RaycastHit hit)
        {
            float cw = _reader.CellWidth, cl = _reader.CellLength;
            int hx = Mathf.Clamp(Mathf.RoundToInt(hit.point.x / cw), 0, _reader.Resolution - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt(hit.point.z / cl), 0, _reader.Resolution - 1);
            _targetNorm    = _reader.GetHeights(new RectInt(hx, hz, 1, 1))[0, 0];
            _targetMetres  = _targetNorm * _reader.WorldSize.y;
            _targetSampled = true;
            _display?.Invoke($"Flatten: {_targetMetres:F1} m  (Ctrl+click to change)");
        }

        public override void OnMouseHeld(RaycastHit hit)
        {
            if (!_targetSampled) return;

            float aboveNorm = EditAboveNorm;
            float blend     = Strength * Time.deltaTime;

            // â”€â”€ Fill Entire Lasso mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (FillEntireLassoActive && GetLassoRegion(out RectInt lassoReg))
            {
                float[,] h = _reader.GetHeights(lassoReg);
                for (int z = 0; z < lassoReg.height; z++)
                for (int x = 0; x < lassoReg.width; x++)
                {
                    if (!IsInsideLasso(lassoReg, x, z)) continue;
                    float cur = h[z, x];
                    if (aboveNorm > 0f && cur < aboveNorm) continue;
                    bool apply = Dir switch
                    {
                        FlattenDir.Fill => cur < _targetNorm,
                        FlattenDir.Cut  => cur > _targetNorm,
                        _               => true
                    };
                    if (apply) h[z, x] = Mathf.Lerp(cur, _targetNorm, blend);
                }
                _writer.SetHeights(h, new Vector2Int(lassoReg.x, lassoReg.y));
                return;
            }

            // â”€â”€ Brush mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var region = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] bh = _reader.GetHeights(region);
            int rows = bh.GetLength(0), cols = bh.GetLength(1);

            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f || !WithinLasso(region.x + x, region.y + z)) continue;
                float cur = bh[z, x];
                if (aboveNorm > 0f && cur < aboveNorm) continue;
                bool apply = Dir switch
                {
                    FlattenDir.Fill => cur < _targetNorm,
                    FlattenDir.Cut  => cur > _targetNorm,
                    _               => true
                };
                if (apply) bh[z, x] = Mathf.Lerp(cur, _targetNorm, blend * SharpFalloff(d));
            }

            _writer.SetHeights(bh, new Vector2Int(region.x, region.y));
        }
    }
}
