using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    public class SmoothTool : BrushToolBase
    {
        public override string ToolId => "smooth";

        private bool _felApplied;

        public SmoothTool(ITerrainReader reader, ITerrainWriter writer) : base(reader, writer) { }

        public override void OnMouseUp() => _felApplied = false;

        public override void OnMouseHeld(RaycastHit hit)
        {
            float aboveNorm = EditAboveNorm;

            // â”€â”€ Fill Entire Lasso mode â€” one pass over the whole lasso â”€â”€â”€â”€â”€â”€â”€â”€
            if (FillEntireLassoActive && GetLassoRegion(out RectInt lassoReg))
            {
                if (_felApplied) return;
                _felApplied = true;

                float[,] orig   = _reader.GetHeights(lassoReg);
                float[,] result = (float[,])orig.Clone();
                int rows = orig.GetLength(0), cols = orig.GetLength(1);

                for (int z = 0; z < rows; z++)
                for (int x = 0; x < cols; x++)
                {
                    if (!IsInsideLasso(lassoReg, x, z)) continue;
                    if (aboveNorm > 0f && orig[z, x] < aboveNorm) continue;

                    float sum = 0f, weight = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nx >= cols || nz < 0 || nz >= rows) continue;
                        sum += orig[nz, nx]; weight += 1f;
                    }
                    result[z, x] = weight > 0f ? sum / weight : orig[z, x];
                }
                _writer.SetHeights(result, new Vector2Int(lassoReg.x, lassoReg.y));
                return;
            }

            // â”€â”€ Brush mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var region      = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] borig  = _reader.GetHeights(region);
            int brows = borig.GetLength(0), bcols = borig.GetLength(1);
            float[,] bres   = new float[brows, bcols];
            float blend = Strength * Time.deltaTime;

            for (int z = 0; z < brows; z++)
            for (int x = 0; x < bcols; x++)
            {
                float d = CellDistNorm(region.x + x, region.y + z, hit.point);
                if (d > 1f || !WithinLasso(region.x + x, region.y + z))
                    { bres[z, x] = borig[z, x]; continue; }
                if (aboveNorm > 0f && borig[z, x] < aboveNorm)
                    { bres[z, x] = borig[z, x]; continue; }

                float sum = 0f, weight = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nx >= bcols || nz < 0 || nz >= brows) continue;
                    sum += borig[nz, nx]; weight += 1f;
                }
                float avg = weight > 0f ? sum / weight : borig[z, x];
                bres[z, x] = Mathf.Lerp(borig[z, x], avg, blend * SharpFalloff(d));
            }

            _writer.SetHeights(bres, new Vector2Int(region.x, region.y));
        }
    }
}
