using System.Collections.Generic;
using UnityEngine;
using IslandBuilder.Domain.Tools;

namespace IslandBuilder.Domain
{
    [AddComponentMenu("Island Builder/Tool Registry")]
    public class ToolRegistry : MonoBehaviour
    {
        private readonly Dictionary<string, ITool> _tools = new();

        public ITool ActiveTool { get; private set; }
        public event TerrainEvents.ActiveToolChangedHandler ActiveToolChanged;

        public int  ToolCount            => _tools.Count;
        public void Register(ITool tool) => _tools[tool.ToolId] = tool;

        /// <summary>Sets BrushRadius on every registered brush tool.</summary>
        public void SetDefaultBrushRadius(float radiusMetres)
        {
            foreach (var tool in _tools.Values)
                if (tool is BrushToolBase bt)
                    bt.BrushRadius = radiusMetres;
        }

        public void SetActiveTool(string toolId)
        {
            if (!_tools.TryGetValue(toolId, out var tool)) return;
            ActiveTool?.OnDeactivate();
            ActiveTool = tool;
            ActiveTool.OnActivate();
            ActiveToolChanged?.Invoke(ActiveTool);
        }
    }
}
