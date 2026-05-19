using System;
using UnityEngine;
using IslandBuilder.Infrastructure;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Central declaration point for all cross-layer C# events.
    ///
    /// Events are instance events on the firing component (per the SDD), but their
    /// delegate signatures are defined here so all layers can reference a single
    /// source of truth without creating circular dependencies.
    ///
    /// Firing components:
    ///   TerrainManager   → TerrainHeightsChanged
    ///   VolumeCalculator → VolumeChanged
    ///   CostEstimator    → CostChanged
    ///   ToolRegistry     → ActiveToolChanged
    ///   ImportManager    → ImportCompleted
    /// </summary>
    public static class TerrainEvents
    {
        /// <summary>
        /// Fired by TerrainManager after every SetHeights call.
        /// <paramref name="patchRect"/> is the modified region in heightmap index space.
        /// Consumed by VolumeCalculator, DepthShader, OverlayRenderer.
        /// </summary>
        public delegate void TerrainHeightsChangedHandler(RectInt patchRect);

        /// <summary>
        /// Fired by VolumeCalculator when the above-sea-level volume is recomputed.
        /// <paramref name="cubicMetres"/> is the total volume in m³.
        /// Consumed by CostEstimator and UIManager (HUD).
        /// </summary>
        public delegate void VolumeChangedHandler(float cubicMetres);

        /// <summary>
        /// Fired by CostEstimator after each VolumeChanged event.
        /// Consumed by UIManager (HUD).
        /// </summary>
        public delegate void CostChangedHandler(float massKg, float costUsd);

        /// <summary>
        /// Fired by ToolRegistry when SetActiveTool() is called.
        /// Consumed by UIManager (ParameterPanel swap) and OverlayRenderer.
        /// </summary>
        public delegate void ActiveToolChangedHandler(ITool newTool);

        /// <summary>
        /// Fired by ImportManager (via DxfImporter or ObjImporter) once the terrain
        /// has been loaded into TerrainManager.
        /// Consumed by CameraController (frame bounding box) and WaterRenderer.
        /// <paramref name="displaySeaLevel"/> is the sea-level Y position in display
        /// space (already scaled for vertical exaggeration).
        /// </summary>
        public delegate void ImportCompletedHandler(TerrainData terrainData, float displaySeaLevel);
    }
}
