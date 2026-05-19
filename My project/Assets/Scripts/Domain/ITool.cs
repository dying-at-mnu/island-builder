using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Implemented by every sculpt/analysis tool. ToolRegistry holds all registered
    /// ITool instances and routes input events to the currently active one.
    /// </summary>
    public interface ITool
    {
        /// <summary>Unique identifier used for undo history labels and tool lookup.</summary>
        string ToolId { get; }

        /// <summary>
        /// Brush radius in world metres. Used by SculptController to size the preview
        /// circle and snapshot the undo region. Non-brush tools return 0.
        /// </summary>
        float BrushRadius { get; }

        /// <summary>
        /// When true, SculptController calls OnMouseHeld every frame there is a terrain
        /// raycast hit, regardless of mouse button state. Used by MeasureTool.
        /// </summary>
        bool AlwaysUpdate { get; }

        /// <summary>Called by ToolRegistry when this tool becomes the active tool.</summary>
        void OnActivate();

        /// <summary>Called by ToolRegistry when another tool is selected.</summary>
        void OnDeactivate();

        /// <summary>Called by SculptController on the frame the primary mouse button is pressed.</summary>
        void OnMouseDown(RaycastHit hit);

        /// <summary>Called by SculptController every frame the primary mouse button is held.</summary>
        void OnMouseHeld(RaycastHit hit);

        /// <summary>Called by SculptController on the frame the primary mouse button is released.</summary>
        void OnMouseUp();

        /// <summary>
        /// Returns the UI Toolkit VisualElement to display in the parameter panel while
        /// this tool is active. May return null if the tool has no configurable parameters.
        /// </summary>
        UnityEngine.UIElements.VisualElement GetParameterPanel();
    }
}
