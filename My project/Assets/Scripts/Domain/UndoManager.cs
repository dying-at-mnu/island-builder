using System.Collections.Generic;
using UnityEngine;

namespace IslandBuilder.Domain
{
    [AddComponentMenu("Island Builder/Undo Manager")]
    public class UndoManager : MonoBehaviour
    {
        private const int MaxDepth = 10;
        private readonly Stack<UndoEntry> _undoStack = new();
        private readonly Stack<UndoEntry> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Push(UndoEntry entry)
        {
            if (_undoStack.Count >= MaxDepth)
            {
                // Trim oldest entry (bottom of stack) — rebuild without it.
                var arr = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = arr.Length - 2; i >= 0; i--)
                    _undoStack.Push(arr[i]);
            }
            _undoStack.Push(entry);
            _redoStack.Clear();
        }

        public bool Undo(TerrainManager terrain)
        {
            if (_undoStack.Count == 0) return false;
            var entry  = _undoStack.Pop();
            var region = entry.ToRectInt();
            // Snapshot current state for redo before restoring.
            float[,] current = terrain.GetHeights(region);
            _redoStack.Push(new UndoEntry(current, entry.PatchOrigin, entry.ToolId));
            terrain.SetHeights(entry.Patch, entry.PatchOrigin);
            return true;
        }

        public bool Redo(TerrainManager terrain)
        {
            if (_redoStack.Count == 0) return false;
            var entry  = _redoStack.Pop();
            var region = entry.ToRectInt();
            float[,] current = terrain.GetHeights(region);
            _undoStack.Push(new UndoEntry(current, entry.PatchOrigin, entry.ToolId));
            terrain.SetHeights(entry.Patch, entry.PatchOrigin);
            return true;
        }
    }
}
