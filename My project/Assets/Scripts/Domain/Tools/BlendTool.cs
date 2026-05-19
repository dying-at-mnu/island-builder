using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Smear brush: the brush carries a weighted-average height from where the stroke
    /// began and deposits it as you drag into a new area, creating a smooth gradient
    /// between the source and destination regions. The carry height gradually adapts
    /// to wherever the brush currently sits, so a long drag produces a soft taper
    /// rather than a hard step.
    ///
    /// Behaviour in brief:
    ///   â€¢ Stroke starts  â†’ carry height = weighted average under brush.
    ///   â€¢ Drag into area B â†’ carry height deposited into B's cells (blend toward carry).
    ///   â€¢ Carry slowly picks up B's average â†’ blend softens the longer you drag.
    ///   â€¢ Strength controls deposit speed; pickup is always gentle (~30 %/s).
    /// </summary>
    public class BlendTool : BrushToolBase
    {
        public override string ToolId => "blend";

        private bool  _hasCarry;
        private float _carryHeight;

        public BlendTool(ITerrainReader reader, ITerrainWriter writer) : base(reader, writer)
        {
            Strength = 0.6f;
        }

        public override void OnActivate()   => _hasCarry = false;
        public override void OnDeactivate() => _hasCarry = false;
        public override void OnMouseUp()    => _hasCarry = false;

        public override void OnMouseHeld(RaycastHit hit)
        {
            var region = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] h = _reader.GetHeights(region);
            int rows = h.GetLength(0), cols = h.GetLength(1);

            // â”€â”€ Weighted average of heights currently under the brush â”€â”€â”€â”€â”€â”€â”€â”€â”€
            float totalW = 0f, totalH = 0f;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f) continue;
                float w = 1f - d;
                totalH += h[z, x] * w;
                totalW += w;
            }
            float currentAvg = totalW > 0f ? totalH / totalW : 0f;

            // First frame of stroke: initialise carry and take no action yet.
            if (!_hasCarry)
            {
                _carryHeight = currentAvg;
                _hasCarry    = true;
                return;
            }

            // â”€â”€ Deposit carry height into all brush cells â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Cells above carry drift down, cells below drift up â€” both sides
            // move toward the carry so each stroke averages the two areas together.
            float deposit = Strength * Time.deltaTime * 4f;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f) continue;
                if (!WithinLasso(region.x + x, region.y + z)) continue;
                float aboveNorm = EditAboveNorm;
                if (aboveNorm > 0f && h[z, x] < aboveNorm) continue;
                h[z, x] = Mathf.Max(0f, Mathf.Lerp(h[z, x], _carryHeight, deposit * SharpFalloff(d)));
            }

            _writer.SetHeights(h, new Vector2Int(region.x, region.y));

            // â”€â”€ Carry slowly adapts to current area â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // 10 %/s keeps the source-peak height alive long enough to meaningfully
            // fill a wide valley before the carry drifts down to valley level.
            float pickup = 1f - Mathf.Pow(1f - 0.10f, Time.deltaTime);
            _carryHeight = Mathf.Lerp(_carryHeight, currentAvg, pickup);
        }
    }
}
