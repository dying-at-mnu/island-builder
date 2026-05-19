using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    /// <summary>
    /// Freehand lasso selection. Hold left-mouse and drag on the terrain to draw
    /// a closed polygon. The selection persists when switching to other tools so
    /// Clear and FillToHeight can operate inside it. Drawing a new lasso replaces
    /// the previous one.
    /// </summary>
    public class LassoTool : ITool
    {
        public string ToolId      => "lasso";
        public float  BrushRadius => 0f;
        public bool   AlwaysUpdate => true; // receives full click events via SculptController

        private readonly List<Vector3> _polygon  = new();
        private bool                   _drawing;
        private Vector3                _lastAdded;
        private const float            MinDist = 3f; // metres between lasso points

        public bool                    HasSelection     => _polygon.Count >= 3;
        public IReadOnlyList<Vector3>  Polygon          => _polygon;
        /// <summary>When true, tools affect cells OUTSIDE the lasso instead of inside.</summary>
        public bool                    InvertSelection  { get; set; } = false;

        /// <summary>Fired when the selection polygon changes.</summary>
        public event Action SelectionChanged;

        public void OnActivate()   { }
        public void OnDeactivate() { }

        public void OnMouseDown(RaycastHit hit)
        {
            _polygon.Clear();
            _drawing  = true;
            _lastAdded = hit.point;
            _polygon.Add(hit.point);
            SelectionChanged?.Invoke();
        }

        public void OnMouseHeld(RaycastHit hit)
        {
            if (!_drawing) return;
            if (Vector3.Distance(hit.point, _lastAdded) >= MinDist)
            {
                _polygon.Add(hit.point);
                _lastAdded = hit.point;
                SelectionChanged?.Invoke();
            }
        }

        public void OnMouseUp()
        {
            _drawing = false;
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Moves a single polygon vertex without firing SelectionChanged (handle drag).
        /// Renderers sync positions in Update(); SelectionChanged is only for count changes.
        /// </summary>
        public void MovePoint(int index, Vector3 newPos)
        {
            if (index >= 0 && index < _polygon.Count)
                _polygon[index] = newPos;
        }

        public void ClearSelection()
        {
            _polygon.Clear();
            _drawing          = false;
            InvertSelection   = false;
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Point-in-polygon test using ray casting in the XZ plane.
        /// Returns true when (wx, wz) lies inside the lasso polygon.
        /// </summary>
        public bool IsInsideLasso(float wx, float wz)
        {
            if (_polygon.Count < 3) return false;
            bool inside = false;
            int  j      = _polygon.Count - 1;
            for (int i = 0; i < _polygon.Count; i++)
            {
                float xi = _polygon[i].x, zi = _polygon[i].z;
                float xj = _polygon[j].x, zj = _polygon[j].z;
                if (((zi > wz) != (zj > wz)) &&
                    (wx < (xj - xi) * (wz - zi) / (zj - zi) + xi))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        /// <summary>World-space bounding box of the lasso in XZ, for fast cell filtering.</summary>
        public bool GetBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            if (_polygon.Count == 0) return false;
            minX = maxX = _polygon[0].x;
            minZ = maxZ = _polygon[0].z;
            foreach (var p in _polygon)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            return true;
        }

        public VisualElement GetParameterPanel() => null;
    }
}
