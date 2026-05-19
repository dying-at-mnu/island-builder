using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Lowers terrain toward the import baseline, never below it (F-11).
    /// </summary>
    public class EraseTool : BrushToolBase
    {
        public override string ToolId => "erase";

        public BrushShape Shape      { get; set; } = BrushShape.Circle;
        public int        StarPoints { get; set; } = 5;
        public float      CameraYawDegrees { get; set; }

        public EraseTool(ITerrainReader reader, ITerrainWriter writer) : base(reader, writer) { }

        public override void OnMouseHeld(RaycastHit hit)
        {
            var region     = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] h     = _reader.GetHeights(region);
            float[,] base_ = _writer.GetBaseline(region);
            float delta     = Strength * Time.deltaTime / _reader.VerticalExaggeration;
            int rows = h.GetLength(0), cols = h.GetLength(1);
            float aboveNorm = EditAboveNorm;

            float yawRad = CameraYawDegrees * Mathf.Deg2Rad;
            float cosY   = Mathf.Cos(yawRad), sinY = Mathf.Sin(yawRad);
            float cw     = _reader.CellWidth;
            float cl     = _reader.CellLength;

            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                if (!WithinLasso(region.x + x, region.y + z)) continue;
                if (aboveNorm > 0f && h[z, x] < aboveNorm) continue;

                float rawNx = ((region.x + x) * cw - hit.point.x) / BrushRadius;
                float rawNz = ((region.y + z) * cl - hit.point.z) / BrushRadius;
                float nx    =  rawNx * cosY + rawNz * sinY;
                float nz    = -rawNx * sinY + rawNz * cosY;

                float sd = RaiseTool.SignedDepth(Shape, nx, nz, StarPoints);
                if (sd <= 0f) continue;

                float falloff = Sharpness >= 1f ? 1f
                    : Mathf.Clamp01(sd / (1f - Sharpness));
                h[z, x] = Mathf.Max(base_[z, x], h[z, x] - delta * falloff);
            }

            _writer.SetHeights(h, new Vector2Int(region.x, region.y));
        }
    }
}
