using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Parses OBJ files and converts the vertex cloud to a normalised heightmap
    /// via ContourRasteriser. No face data is needed — vertices alone define the
    /// elevation surface.
    ///
    /// OBJ coordinate convention assumed: X = right, Y = up (elevation), Z = depth.
    /// Maps to ContourPoint as: X→X, Z→Z, Y→Elevation.
    /// </summary>
    public class ObjImporter : IImporter
    {
        public string SupportedExtension => "obj";

        /// <summary>Target heightmap resolution. Must be power-of-two + 1 (e.g. 513).</summary>
        public int Resolution = 513;

        public ImportResult Import(string path)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                return ImportResult.Failure($"Could not read OBJ file: {ex.Message}");
            }

            var points = new List<ContourPoint>(4096);

            foreach (var raw in lines)
            {
                // Only vertex lines are needed; skip everything else.
                if (raw.Length < 2 || raw[0] != 'v' || raw[1] != ' ') continue;

                var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vy) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vz))
                    continue;

                points.Add(new ContourPoint { X = vx, Z = vz, Elevation = vy });
            }

            if (points.Count == 0)
                return ImportResult.Failure("No vertex data ('v x y z' lines) found in the OBJ file.");

            var (heights, worldSize, seaLevel, boundary) = ContourRasteriser.Rasterise(points, Resolution);
            return new ImportResult
            {
                Success         = true,
                Heights         = heights,
                WorldSize       = worldSize,
                SeaLevel        = seaLevel,
                Resolution      = Resolution,
                TerrainBoundary = boundary
            };
        }
    }
}
