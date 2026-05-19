using UnityEngine;

namespace IslandBuilder.Domain.Tools
{
    public enum RaiseMode  { Smooth, Shape }
    public enum BrushShape { Circle, Square, Star, Triangle }

    public class RaiseTool : BrushToolBase
    {
        public override string ToolId      => "raise";
        public override bool   HasStrength => false;

        // â”€â”€ Shared â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public RaiseMode Mode             { get; set; } = RaiseMode.Smooth;
        public float     VerticalStrength { get; set; } = 0.3f;

        // â”€â”€ Shape-mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public BrushShape Shape      { get; set; } = BrushShape.Circle;
        public int        StarPoints { get; set; } = 5;

        private const float StarInnerRatio = 0.45f;

        // Sharpness is inherited from BrushToolBase.
        // Smooth mode: 0 = soft centre-weighted taper, 1 = flat uniform disc.
        // Shape mode:  0 = gradient from boundary inward, 1 = hard edge (binary).
        // Default 0.5 gives a moderate flat zone for smooth and a medium-soft edge for shape.

        /// <summary>Camera yaw in degrees â€” shapes are oriented toward the camera.</summary>
        public float CameraYawDegrees { get; set; }

        public RaiseTool(ITerrainReader reader, ITerrainWriter writer) : base(reader, writer)
        {
            Sharpness = 0.5f;
        }

        public override void OnMouseHeld(RaycastHit hit)
        {
            var region = WorldToHeightmapRect(hit.point, BrushRadius);
            float[,] h = _reader.GetHeights(region);
            int rows = h.GetLength(0), cols = h.GetLength(1);

            float delta     = VerticalStrength * Time.deltaTime / _reader.VerticalExaggeration;
            float cw        = _reader.CellWidth;
            float cl        = _reader.CellLength;
            float aboveNorm = EditAboveNorm;

            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                if (aboveNorm > 0f && h[z, x] < aboveNorm) continue;
                float rawNx = ((region.x + x) * cw - hit.point.x) / BrushRadius;
                float rawNz = ((region.y + z) * cl - hit.point.z) / BrushRadius;
                // Rotate into camera-relative space so shapes face the viewer.
                float yawRad = CameraYawDegrees * Mathf.Deg2Rad;
                float cosY = Mathf.Cos(yawRad), sinY = Mathf.Sin(yawRad);
                float nx =  rawNx * cosY + rawNz * sinY;
                float nz = -rawNx * sinY + rawNz * cosY;

                if (!WithinLasso(region.x + x, region.y + z)) continue;

                if (Mode == RaiseMode.Smooth)
                {
                    float d = Mathf.Sqrt(nx * nx + nz * nz);
                    if (d > 1f) continue;
                    h[z, x] = Mathf.Max(0f, h[z, x] + delta * SharpFalloff(d));
                }
                else
                {
                    float sd = SignedDepth(nx, nz);
                    if (sd <= 0f) continue;
                    float edgeFactor = Sharpness >= 1f ? 1f
                        : Mathf.Clamp01(sd / (1f - Sharpness));
                    h[z, x] = Mathf.Max(0f, h[z, x] + delta * edgeFactor);
                }
            }

            _writer.SetHeights(h, new Vector2Int(region.x, region.y));
        }

        internal static float SignedDepth(BrushShape shape, float nx, float nz, int starPoints = 5)
        {
            return shape switch
            {
                BrushShape.Circle   => 1f - Mathf.Sqrt(nx * nx + nz * nz),
                BrushShape.Square   => 1f - Mathf.Max(Mathf.Abs(nx), Mathf.Abs(nz)),
                BrushShape.Star     => StarSignedDepth(nx, nz, starPoints),
                BrushShape.Triangle => TriSignedDepth(nx, nz),
                _                   => -1f
            };
        }

        private float SignedDepth(float nx, float nz) => SignedDepth(Shape, nx, nz, StarPoints);

        private static float StarSignedDepth(float nx, float nz, int starPoints)
        {
            float dist = Mathf.Sqrt(nx * nx + nz * nz);
            if (dist > 1f) return -1f;
            float angle       = Mathf.Atan2(nz, nx);
            float sectorAngle = Mathf.PI * 2f / starPoints;
            float nearestPt   = Mathf.Round(angle / sectorAngle) * sectorAngle;
            float angDist     = Mathf.Abs(angle - nearestPt);
            angDist = Mathf.Min(angDist, sectorAngle - angDist);
            float t          = angDist / (sectorAngle * 0.5f);
            float effectiveR = Mathf.Lerp(1f, StarInnerRatio, t);
            if (dist > effectiveR) return -1f;
            return 1f - dist / effectiveR;
        }

        // Regular equilateral triangle pointing "up" (away from camera).
        // Incircle radius = 0.5 â†’ normalise so sd = 1 at centroid, 0 at edges.
        private static float TriSignedDepth(float nx, float nz)
        {
            // Vertices on the unit circle at 90Â°, 210Â°, 330Â°
            const float v0x = 0f,      v0z = 1f;
            const float v1x = -0.866f, v1z = -0.5f;
            const float v2x =  0.866f, v2z = -0.5f;
            float d0 = TriEdge(nx, nz, v0x, v0z, v1x, v1z);
            float d1 = TriEdge(nx, nz, v1x, v1z, v2x, v2z);
            float d2 = TriEdge(nx, nz, v2x, v2z, v0x, v0z);
            return Mathf.Min(Mathf.Min(d0, d1), d2) * 2f; // Ã· inradius 0.5
        }

        // Signed distance from P to directed edge Aâ†’B; positive = inward (CW polygon).
        private static float TriEdge(float px, float pz,
                                     float ax, float az, float bx, float bz)
        {
            float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            return -((bz - az) * (px - ax) - (bx - ax) * (pz - az)) / Mathf.Max(len, 1e-6f);
        }
    }
}
