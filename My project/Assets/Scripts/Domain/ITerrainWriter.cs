using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Write access to the terrain heightmap. Consumed by SculptController,
    /// CutPlaneTool, and BoundaryFillTool. Separated from ITerrainReader so
    /// read-only consumers cannot accidentally modify terrain state.
    /// </summary>
    public interface ITerrainWriter
    {
        /// <summary>
        /// Writes a normalised height patch (0..1) back into the heightmap at the
        /// given origin. Fires TerrainHeightsChanged after the write completes.
        /// </summary>
        /// <param name="patch">
        /// Height values to write. Indexed [z, x] per Unity convention.
        /// </param>
        /// <param name="origin">
        /// Top-left heightmap index of the patch. Must place the patch fully within
        /// [0, Resolution).
        /// </param>
        void SetHeights(float[,] patch, Vector2Int origin);

        /// <summary>
        /// Returns a copy of the baseline heightmap for the given region. The baseline
        /// is the heightmap at import time and is never modified. EraseBrush clamps
        /// against this to prevent digging below the original surface.
        /// </summary>
        /// <param name="region">
        /// Rectangle in heightmap index space. Must be fully within [0, Resolution).
        /// </param>
        float[,] GetBaseline(RectInt region);
    }
}
