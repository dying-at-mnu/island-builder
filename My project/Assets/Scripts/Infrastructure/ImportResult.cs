using UnityEngine;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Returned by every IImporter implementation. TerrainManager is the only consumer.
    /// On failure, success is false and error contains a human-readable message;
    /// all other fields are undefined.
    /// </summary>
    public class ImportResult
    {
        /// <summary>True if the import succeeded and all fields below are valid.</summary>
        public bool Success;

        /// <summary>Human-readable error message. Non-null only when Success is false.</summary>
        public string Error;

        /// <summary>
        /// Normalised height values in the range 0..1. Indexed [z, x] per Unity convention.
        /// Size is [Resolution, Resolution].
        /// </summary>
        public float[,] Heights;

        /// <summary>Real-world dimensions of the imported terrain in metres. y = max elevation.</summary>
        public Vector3 WorldSize;

        /// <summary>
        /// World-space Y coordinate of sea level (elevation = 0).
        /// Used by WaterRenderer to position the water plane at the correct height.
        /// </summary>
        public float SeaLevel;

        /// <summary>
        /// Grid size of the Heights array. Must be a power-of-two + 1 value
        /// (e.g. 513, 1025) to satisfy the Unity Terrain requirement.
        /// </summary>
        public int Resolution;

        /// <summary>
        /// Human-readable name of the unit detected in the file header ("Metres",
        /// "Feet", "Unknown", etc.). Always set on success.
        /// </summary>
        public string DetectedUnit;

        /// <summary>
        /// True when the file carried no unit information. The caller must ask the
        /// user for a scale factor and multiply WorldSize and SeaLevel by it.
        /// </summary>
        public bool NeedsScaleConfirmation;

        /// <summary>
        /// Convex hull of the source contour points in world-space XZ (metres).
        /// Represents the actual boundary of the imported survey data, which may
        /// be irregular rather than rectangular.
        /// </summary>
        public UnityEngine.Vector2[] TerrainBoundary;

        /// <summary>Scale factor (metres per DXF unit) applied during import.
        /// 1.0 for metres or unknown units; 0.3048 for feet, etc.</summary>
        public float AppliedUnitScale;

        /// <summary>
        /// Raw Z-value difference between adjacent contour lines in the file's native units
        /// (before any unit conversion). 0 when not detected or not a DXF import.
        /// </summary>
        public float ContourInterval;

        /// <summary>
        /// Original contour polylines from the DXF file in terrain world-space XZ
        /// (origin = terrain corner). Null for non-DXF imports.
        /// </summary>
        public ContourPolyline[] ContourLines;

        /// <summary>Convenience factory for a failed import.</summary>
        public static ImportResult Failure(string error) =>
            new ImportResult { Success = false, Error = error };
    }

    /// <summary>One polyline from the DXF contour file, ready to render.</summary>
    public class ContourPolyline
    {
        public float   Elevation; // elevation in metres after unit conversion
        public Vector2[] Points;  // XZ in terrain world space (metres from terrain origin)
    }
}
