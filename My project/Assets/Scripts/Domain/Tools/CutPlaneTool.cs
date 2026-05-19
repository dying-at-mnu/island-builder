using System;
using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    public class CutPlaneTool : BrushToolBase
    {
        public override string  ToolId      => "cut";
        public override bool HasStrength => false;

        private readonly Action<string> _display;
        private float _cutNorm    = 0f;
        private float _cutMetres  = 0f;
        private bool  _cutSampled = false;

        public bool  CutSampled => _cutSampled;
        public float CutMetres  => _cutMetres;

        public CutPlaneTool(ITerrainReader reader, ITerrainWriter writer,
                            Action<string> displayCallback = null) : base(reader, writer)
        {
            _display = displayCallback;
        }

        public override void OnActivate()
        {
            if (_cutSampled)
                _display?.Invoke($"Cut level: {_cutMetres:F1} m  (Ctrl+click to change)");
            else
                _display?.Invoke("Cut level: none  (Ctrl+click to set)");
        }

        public override void OnDeactivate() => _display?.Invoke(string.Empty);

        // Called only on Ctrl+LeftClick — samples the cut height directly from the heightmap.
        public override void OnMouseDown(RaycastHit hit)
        {
            float cw = _reader.CellWidth;
            float cl = _reader.CellLength;
            int hx = Mathf.Clamp(Mathf.RoundToInt(hit.point.x / cw), 0, _reader.Resolution - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt(hit.point.z / cl), 0, _reader.Resolution - 1);
            _cutNorm    = _reader.GetHeights(new RectInt(hx, hz, 1, 1))[0, 0];
            _cutMetres  = _cutNorm * _reader.WorldSize.y;
            _cutSampled = true;
            _display?.Invoke($"Cut level: {_cutMetres:F1} m  (Ctrl+click to change)");
        }

        public override void OnMouseHeld(RaycastHit hit)
        {
            if (!_cutSampled) return;

            var region  = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] h  = _reader.GetHeights(region);
            int rows = h.GetLength(0), cols = h.GetLength(1);

            for (int z = 0; z < rows; z++)
                for (int x = 0; x < cols; x++)
                {
                    float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                    if (d <= 1f && h[z, x] > _cutNorm &&
                        WithinLasso(region.x + x, region.y + z))
                        h[z, x] = _cutNorm;
                }

            _writer.SetHeights(h, new Vector2Int(region.x, region.y));
        }
    }
}
