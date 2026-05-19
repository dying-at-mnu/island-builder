using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Read-only view of the terrain heightmap. Consumed by VolumeCalculator,
    /// rendering components, and tools that need to sample heights without
    /// writing them.
    /// </summary>
    public interface ITerrainReader
    {
        /// <summary>Real-world dimensions of the terrain in metres. y = max elevation.</summary>
        Vector3 WorldSize { get; }

        /// <summary>
        /// Heightmap grid size. Unity requires a power-of-two + 1 value (e.g. 513, 1025).
        /// </summary>
        int Resolution { get; }

        /// <summary>
        /// Y position of the water plane in metres. Cells below this value are
        /// considered submerged.
        /// </summary>
        float SeaLevelOffset { get; }

        /// <summary>
        /// Current vertical exaggeration multiplier (1 = no exaggeration).
        /// Tools divide height deltas by this value so bumps stay proportional
        /// to their horizontal footprint regardless of the exaggeration setting.
        /// </summary>
        float VerticalExaggeration { get; }

        /// <summary>Cell width in metres: WorldSize.x / (Resolution - 1).</summary>
        float CellWidth { get; }

        /// <summary>Cell length in metres: WorldSize.z / (Resolution - 1).</summary>
        float CellLength { get; }

        /// <summary>
        /// Returns a copy of the normalised heightmap values (0..1) for the given
        /// heightmap-space rectangle. Indexed [z, x] per Unity convention.
        /// </summary>
        /// <param name="region">
        /// Rectangle in heightmap index space. Must be fully within [0, Resolution).
        /// </param>
        float[,] GetHeights(RectInt region);
    }
}
