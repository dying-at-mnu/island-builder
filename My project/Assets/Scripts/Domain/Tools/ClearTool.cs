using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    public class ClearTool : BrushToolBase
    {
        public override string ToolId      => "clear";
        public override bool   HasStrength => false;

        public ClearTool(ITerrainReader reader, ITerrainWriter writer)
            : base(reader, writer) { }

        public override void OnMouseHeld(RaycastHit hit)
        {
            float aboveNorm = EditAboveNorm;

            // â”€â”€ Fill Entire Lasso mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (FillEntireLassoActive && GetLassoRegion(out RectInt lassoReg))
            {
                float[,] h     = _reader.GetHeights(lassoReg);
                float[,] base_ = _writer.GetBaseline(lassoReg);
                for (int z = 0; z < lassoReg.height; z++)
                for (int x = 0; x < lassoReg.width; x++)
                {
                    if (!IsInsideLasso(lassoReg, x, z)) continue;
                    if (aboveNorm > 0f && h[z, x] < aboveNorm) continue;
                    h[z, x] = base_[z, x];
                }
                _writer.SetHeights(h, new Vector2Int(lassoReg.x, lassoReg.y));
                return;
            }

            // â”€â”€ Brush mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var region     = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] bh    = _reader.GetHeights(region);
            float[,] bbase = _writer.GetBaseline(region);
            int rows = bh.GetLength(0), cols = bh.GetLength(1);

            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f || !WithinLasso(region.x + x, region.y + z)) continue;
                if (aboveNorm > 0f && bh[z, x] < aboveNorm) continue;
                bh[z, x] = bbase[z, x];
            }

            _writer.SetHeights(bh, new Vector2Int(region.x, region.y));
        }
    }
}
