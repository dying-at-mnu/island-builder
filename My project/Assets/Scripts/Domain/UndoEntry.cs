using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// A single entry on the undo/redo stack. Stores a snapshot of the heightmap
    /// patch that existed *before* a stroke was applied, so it can be restored on
    /// Ctrl+Z.
    ///
    /// Only the affected rectangular region is stored — not the full heightmap —
    /// so memory cost scales with brush size rather than terrain resolution.
    /// </summary>
    public readonly struct UndoEntry
    {
        /// <summary>
        /// Heightmap values before the stroke. Indexed [z, x] per Unity convention.
        /// Size is [PatchOrigin.height, PatchOrigin.width] (matching the RectInt dimensions).
        /// </summary>
        public readonly float[,] Patch;

        /// <summary>Top-left heightmap index of the stored patch.</summary>
        public readonly Vector2Int PatchOrigin;

        /// <summary>Width and height of the patch in heightmap cells.</summary>
        public readonly Vector2Int PatchSize;

        /// <summary>
        /// ToolId of the tool that produced this entry. Used for history panel labels.
        /// </summary>
        public readonly string ToolId;

        public UndoEntry(float[,] patch, Vector2Int origin, string toolId)
        {
            Patch       = patch;
            PatchOrigin = origin;
            PatchSize   = new Vector2Int(patch.GetLength(1), patch.GetLength(0));
            ToolId      = toolId;
        }

        /// <summary>
        /// Returns the heightmap-space rectangle that this entry covers, for use
        /// with ITerrainWriter.SetHeights.
        /// </summary>
        public RectInt ToRectInt() => new RectInt(PatchOrigin.x, PatchOrigin.y, PatchSize.x, PatchSize.y);
    }
}
