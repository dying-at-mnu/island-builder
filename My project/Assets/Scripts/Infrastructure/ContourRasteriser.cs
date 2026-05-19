using System.Collections.Generic;
using UnityEngine;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Converts a set of 3D contour points into a normalised heightmap grid using
    /// inverse-distance weighting (IDW). Shared by DxfImporter and ObjImporter.
    ///
    /// Algorithm:
    ///   For each grid cell, find all source points within a search radius and
    ///   compute a weighted average elevation: h = Σ(e/d²) / Σ(1/d²).
    ///   Cells with no neighbours within the radius fall back to a global
    ///   nearest-neighbour search so that no cell is ever silently zeroed out.
    ///
    /// A spatial hash grid reduces neighbour lookups to O(1) per cell instead of O(N).
    /// </summary>
    public static class ContourRasteriser
    {
        /// <param name="points">Source 3D points (X/Z = horizontal, Elevation = metres).</param>
        /// <param name="resolution">Target grid size. Must be power-of-two + 1 (e.g. 513).</param>
        /// <param name="nearestN">Retained for API compatibility; no longer used — all candidates within the search radius are used.</param>
        /// <param name="searchRadiusMultiplier">Search radius = this × max(cellWidth, cellLength). Default 3 per SDD.</param>
        /// <returns>
        /// Normalised heights [resolution, resolution] indexed [z, x] (Unity convention),
        /// the real-world extents computed from the point bounding box,
        /// and the world-space Y coordinate of sea level (elevation = 0).
        /// </returns>
        public static (float[,] heights, Vector3 worldSize, float seaLevel, Vector2[] boundary) Rasterise(
            List<ContourPoint> points,
            int   resolution            = 513,
            int   nearestN              = 8,
            float searchRadiusMultiplier = 3f)
        {
            if (points == null || points.Count == 0)
            {
                Debug.LogWarning("[ContourRasteriser] No input points.");
                return (new float[resolution, resolution], new Vector3(100f, 10f, 100f), 0f, new Vector2[0]);
            }

            // ── Bounding box ──────────────────────────────────────────────────
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float minE = float.MaxValue, maxE = float.MinValue;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
                if (p.Elevation < minE) minE = p.Elevation;
                if (p.Elevation > maxE) maxE = p.Elevation;
            }

            float extentX = Mathf.Max(maxX - minX, 0.001f);
            float extentZ = Mathf.Max(maxZ - minZ, 0.001f);
            float extentE = Mathf.Max(maxE - minE, 0.001f);

            float cellWidth  = extentX / (resolution - 1);
            float cellLength = extentZ / (resolution - 1);

            // Adaptive search radius: use the larger of a cell-based radius and an
            // average-spacing-based radius so sparse point clouds (e.g. widely-spaced
            // contour lines on an atoll) still produce filled terrain.
            float avgSpacing   = Mathf.Sqrt((extentX * extentZ) / points.Count);
            float searchRadius = Mathf.Max(
                searchRadiusMultiplier * Mathf.Max(cellWidth, cellLength),
                searchRadiusMultiplier * avgSpacing);

            // ── Spatial hash grid ─────────────────────────────────────────────
            // Each bucket is one searchRadius × searchRadius square.
            // Checking the 3×3 neighbourhood around a cell's bucket guarantees
            // all points within searchRadius are found.
            int bw = Mathf.Max(1, Mathf.CeilToInt(extentX / searchRadius) + 1);
            int bh = Mathf.Max(1, Mathf.CeilToInt(extentZ / searchRadius) + 1);
            var buckets = new List<int>[bh, bw];

            for (int i = 0; i < points.Count; i++)
            {
                int bx = Mathf.Clamp(Mathf.FloorToInt((points[i].X - minX) / searchRadius), 0, bw - 1);
                int bz = Mathf.Clamp(Mathf.FloorToInt((points[i].Z - minZ) / searchRadius), 0, bh - 1);
                if (buckets[bz, bx] == null) buckets[bz, bx] = new List<int>();
                buckets[bz, bx].Add(i);
            }

            // ── Rasterise — Pass 1: IDW within search radius ──────────────────
            // Sentinel value flags cells that had no candidates within the radius.
            // These are filled in Pass 2 so no cell is ever silently zeroed.
            const float Sentinel = float.MinValue;
            float[,] heights  = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                    heights[z, x] = Sentinel;

            // Reusable candidate list to avoid per-cell allocations in the hot path.
            var candidates = new List<(float dist2, float elev)>(64);

            for (int z = 0; z < resolution; z++)
            {
                float wz     = minZ + z * cellLength;
                int   bzBase = Mathf.Clamp(Mathf.FloorToInt((wz - minZ) / searchRadius), 0, bh - 1);

                for (int x = 0; x < resolution; x++)
                {
                    float wx     = minX + x * cellWidth;
                    int   bxBase = Mathf.Clamp(Mathf.FloorToInt((wx - minX) / searchRadius), 0, bw - 1);

                    candidates.Clear();
                    float r2 = searchRadius * searchRadius;

                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nbz = bzBase + dz;
                        if (nbz < 0 || nbz >= bh) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nbx = bxBase + dx;
                            if (nbx < 0 || nbx >= bw) continue;

                            var bucket = buckets[nbz, nbx];
                            if (bucket == null) continue;

                            foreach (int idx in bucket)
                            {
                                float ddx   = points[idx].X - wx;
                                float ddz   = points[idx].Z - wz;
                                float dist2 = ddx * ddx + ddz * ddz;
                                if (dist2 <= r2)
                                    candidates.Add((dist2, points[idx].Elevation));
                            }
                        }
                    }

                    if (candidates.Count == 0) continue; // handled in Pass 2

                    // Use ALL candidates within the radius (no nearest-N cap) so that
                    // no source data is discarded. Sort is no longer needed.
                    float sumW = 0f, sumWE = 0f;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        // IDW weight = 1/d². Guard against d=0 (point exactly on cell centre).
                        float w  = 1f / Mathf.Max(candidates[i].dist2, 1e-6f);
                        sumW  += w;
                        sumWE += w * candidates[i].elev;
                    }

                    float elevMetres = sumWE / sumW;
                    heights[z, x] = Mathf.Clamp01((elevMetres - minE) / extentE);
                }
            }

            // ── Rasterise — Pass 2: global nearest-neighbour fallback ──────────
            // Any cell still at Sentinel had no points within the search radius.
            // Rather than defaulting to 0 (which would create phantom deep zones),
            // we find the single nearest point from the entire dataset and assign
            // its elevation. This guarantees every cell reflects actual DXF data.
            int fallbackCount = 0;
            for (int z = 0; z < resolution; z++)
            {
                float wz = minZ + z * cellLength;
                for (int x = 0; x < resolution; x++)
                {
                    if (heights[z, x] != Sentinel) continue;

                    float wx = minX + x * cellWidth;
                    float bestDist2 = float.MaxValue;
                    float bestElev  = minE;

                    for (int i = 0; i < points.Count; i++)
                    {
                        float ddx   = points[i].X - wx;
                        float ddz   = points[i].Z - wz;
                        float dist2 = ddx * ddx + ddz * ddz;
                        if (dist2 < bestDist2) { bestDist2 = dist2; bestElev = points[i].Elevation; }
                    }

                    heights[z, x] = Mathf.Clamp01((bestElev - minE) / extentE);
                    fallbackCount++;
                }
            }

            if (fallbackCount > 0)
                Debug.Log($"[ContourRasteriser] {fallbackCount} of {resolution * resolution} cells " +
                          $"filled by global nearest-neighbour fallback (gaps between contour lines).");

            // ── Auto-detect unit conversion ───────────────────────────────────
            // Real terrain is never under 2 m wide; an extent < 2 means the
            // file uses geographic degrees for X/Z.
            // The ratio of raw elevation extent to raw horizontal extent reveals the
            // elevation unit:
            //   > 5000  → millimetres  (vScale = 0.001)
            //   > 500   → centimetres  (vScale = 0.01)
            //   > 50    → decimetres   (vScale = 0.1)
            //   else    → metres       (vScale = 1.0)
            float hScale = 1f, vScale = 1f;
            float maxHoriz = Mathf.Max(extentX, extentZ);

            if (maxHoriz > 0f && maxHoriz < 2f)
            {
                hScale = 111000f; // 1 degree ≈ 111 km at equator
                float vertRatio = extentE / maxHoriz;
                string elevUnit;
                // Thresholds tuned so typical bathymetric DXF files in centimetres
                // (ratio ~3000–10000) are not misidentified as millimetres.
                if (vertRatio > 50000f)     { vScale = 0.001f; elevUnit = "millimetres"; }
                else if (vertRatio > 5000f) { vScale = 0.01f;  elevUnit = "centimetres"; }
                else if (vertRatio > 500f)  { vScale = 0.1f;   elevUnit = "decimetres";  }
                else                        {                   elevUnit = "metres";      }

                Debug.Log($"[ContourRasteriser] Auto-detected geographic degrees + {elevUnit} elevation " +
                          $"(ratio={vertRatio:F0}). hScale=111 000, vScale={vScale} → " +
                          $"world {extentX * hScale:F0} × {extentZ * hScale:F0} m, " +
                          $"elevation range {minE * vScale:F2} → {maxE * vScale:F2} m.");
            }

            var worldSize = new Vector3(extentX * hScale, extentE * vScale, extentZ * hScale);

            // ── Sea level world position ──────────────────────────────────────
            // Sea level = where elevation equals zero in raw units.
            // Normalised position: (-minE) / extentE  (clamp to [0,1] for safety).
            // World Y = normalised sea level × terrain height.
            float seaLevelWorld = worldSize.y > 0f
                ? Mathf.Clamp01(-minE / extentE) * worldSize.y
                : 0f;

            // Convex hull of the source points expressed in world space (origin = terrain corner).
            var boundary = ConvexHull(points, minX, minZ);

            return (heights, worldSize, seaLevelWorld, boundary);
        }

        // ── Convex hull (Andrew's Monotone Chain) ─────────────────────────────

        private static Vector2[] ConvexHull(List<ContourPoint> points, float minX, float minZ)
        {
            var pts = new List<Vector2>(points.Count);
            foreach (var p in points)
                pts.Add(new Vector2(p.X - minX, p.Z - minZ));

            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            int n = pts.Count;
            if (n < 3) return pts.ToArray();

            var hull = new Vector2[2 * n];
            int k = 0;

            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(hull[k-2], hull[k-1], pts[i]) <= 0f) k--;
                hull[k++] = pts[i];
            }
            for (int i = n - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(hull[k-2], hull[k-1], pts[i]) <= 0f) k--;
                hull[k++] = pts[i];
            }

            var result = new Vector2[k - 1];
            System.Array.Copy(hull, result, k - 1);
            return result;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b)
            => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
