using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Passive tool: no terrain interaction. Selecting it signals "camera-only" mode
    /// so users can orbit / pan without accidentally sculpting.
    /// </summary>
    public class CameraTool : ITool
    {
        public string ToolId      => "camera";
        public float  BrushRadius => 0f;
        public bool   AlwaysUpdate => false;

        public void OnActivate()              { }
        public void OnDeactivate()            { }
        public void OnMouseDown(RaycastHit h) { }
        public void OnMouseHeld(RaycastHit h) { }
        public void OnMouseUp()               { }

        public VisualElement GetParameterPanel() => null;
    }
}
