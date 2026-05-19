using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Selecting this tool opens the grid settings in the sidebar.
    /// Grid visibility is controlled by the Show/Hide toggle inside the settings,
    /// not by selecting this tool.
    /// </summary>
    public class GridTool : ITool
    {
        public string ToolId       => "grid";
        public float  BrushRadius  => 0f;
        public bool   AlwaysUpdate => false;

        public void OnActivate()              { } // visibility controlled by sidebar toggle
        public void OnDeactivate()            { }
        public void OnMouseDown(RaycastHit h) { }
        public void OnMouseHeld(RaycastHit h) { }
        public void OnMouseUp()               { }
        public VisualElement GetParameterPanel() => null;
    }
}
