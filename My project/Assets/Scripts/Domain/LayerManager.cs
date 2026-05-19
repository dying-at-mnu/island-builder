using System;
using System.Collections.Generic;
using UnityEngine;
using IslandBuilder.Infrastructure;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Manages independent sand layers that compose additively on top of the
    /// imported terrain baseline.
    ///
    /// Each <see cref="SandLayer"/> stores a per-cell delta (≥ 0). The terrain
    /// always displays:  clamp(baseline + Σ visible_layer_deltas, 0, 1).
    ///
    /// Tools write to TerrainManager as before. This class observes
    /// TerrainHeightsChanged, attributes the difference to the active layer, then
    /// immediately recomposes so other layers' contributions are protected — you
    /// can never erase another layer's sand by sculpting on the active layer.
    /// </summary>
    [AddComponentMenu("Island Builder/Layer Manager")]
    public class LayerManager : MonoBehaviour
    {
        private TerrainManager           _terrain;
        private readonly List<SandLayer> _layers = new();
        private int  _activeIndex;
        private bool _compositing; // prevents re-entrant recomposition

        public IReadOnlyList<SandLayer> Layers     => _layers;
        public int                      ActiveIndex => _activeIndex;
        public SandLayer                ActiveLayer => _layers.Count > 0 ? _layers[_activeIndex] : null;

        public event Action LayersChanged;

        // ── Public API ────────────────────────────────────────────────────────

        public void Bind(TerrainManager terrain, ImportManager importManager)
        {
            _terrain = terrain;
            terrain.TerrainHeightsChanged += OnTerrainChanged;
            importManager.ImportCompleted  += (td, _) => OnNewTerrain(td.heightmapResolution);
            InitLayers(terrain.Resolution);
        }

        public void AddLayer(string name = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                // Find the lowest "Layer N" number not already taken by an existing layer.
                int n = 1;
                var used = new System.Collections.Generic.HashSet<string>();
                foreach (var l in _layers) used.Add(l.Name);
                while (used.Contains($"Layer {n}")) n++;
                name = $"Layer {n}";
            }
            _layers.Add(new SandLayer(name, Mathf.Max(1, _terrain.Resolution)));
            _activeIndex = _layers.Count - 1;
            LayersChanged?.Invoke();
        }

        public void SetActiveLayer(int index)
        {
            if (index < 0 || index >= _layers.Count) return;
            _activeIndex = index;
            LayersChanged?.Invoke();
        }

        public void RenameLayer(int index, string newName)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index].Name = string.IsNullOrWhiteSpace(newName) ? $"Layer {index + 1}" : newName;
            LayersChanged?.Invoke();
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index].IsVisible = visible;
            RecomposeAll();
            LayersChanged?.Invoke();
        }

        public void RemoveLayer(int index)
        {
            if (_layers.Count <= 1 || index < 0 || index >= _layers.Count) return;
            _layers.RemoveAt(index);
            _activeIndex = Mathf.Clamp(_activeIndex, 0, _layers.Count - 1);
            RecomposeAll();
            LayersChanged?.Invoke();
        }

        /// <summary>Moves a layer one position up (toward the top of the stack).</summary>
        public void MoveLayerUp(int index)
        {
            if (index >= _layers.Count - 1) return;
            Swap(index, index + 1);
            if      (_activeIndex == index)     _activeIndex = index + 1;
            else if (_activeIndex == index + 1) _activeIndex = index;
            RecomposeAll();
            LayersChanged?.Invoke();
        }

        /// <summary>Moves a layer one position down (toward the bottom of the stack).</summary>
        public void MoveLayerDown(int index)
        {
            if (index <= 0) return;
            Swap(index, index - 1);
            if      (_activeIndex == index)     _activeIndex = index - 1;
            else if (_activeIndex == index - 1) _activeIndex = index;
            RecomposeAll();
            LayersChanged?.Invoke();
        }

        /// <summary>Merges the layer at <paramref name="index"/> down into the layer below it.</summary>
        public void SetLayerLocked(int index, bool locked)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index].IsLocked = locked;
            LayersChanged?.Invoke();
        }

        public void MergeDown(int index)
        {
            if (index <= 0 || index >= _layers.Count) return;
            var upper = _layers[index];
            var lower = _layers[index - 1];
            int res = _terrain.Resolution;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                lower.Delta[z, x] = Mathf.Max(0f, lower.Delta[z, x] + upper.Delta[z, x]);
            _layers.RemoveAt(index);
            _activeIndex = Mathf.Clamp(_activeIndex, 0, _layers.Count - 1);
            RecomposeAll();
            LayersChanged?.Invoke();
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void InitLayers(int res)
        {
            _layers.Clear();
            _activeIndex = 0;
            AddLayer("Layer 1");
        }

        private void OnNewTerrain(int resolution)
        {
            foreach (var layer in _layers)
                layer.Resize(Mathf.Max(1, resolution));
            LayersChanged?.Invoke();
        }

        private void Swap(int a, int b)
        {
            (_layers[a], _layers[b]) = (_layers[b], _layers[a]);
        }

        /// <summary>
        /// Called after any tool write. Captures the change into the active layer's
        /// delta, then recomposes the region to enforce independence: if another
        /// layer has sand at a cell, erasing on the active layer won't remove it.
        /// </summary>
        private void OnTerrainChanged(RectInt region)
        {
            if (_compositing || ActiveLayer == null || _layers.Count == 0) return;

            // If the active layer is locked, reject the edit and restore the terrain.
            if (ActiveLayer.IsLocked)
            {
                RecomposeRegion(region);
                return;
            }

            int res = _terrain.Resolution;
            region = new RectInt(
                Mathf.Clamp(region.x, 0, res - 1),
                Mathf.Clamp(region.y, 0, res - 1),
                Mathf.Min(region.width,  res - region.x),
                Mathf.Min(region.height, res - region.y));
            if (region.width <= 0 || region.height <= 0) return;

            float[,] current  = _terrain.GetHeights(region);
            float[,] baseline = _terrain.GetBaseline(region);

            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width;  x++)
            {
                float others = 0f;
                for (int li = 0; li < _layers.Count; li++)
                {
                    if (li == _activeIndex || !_layers[li].IsVisible) continue;
                    others += _layers[li].Delta[region.y + z, region.x + x];
                }
                float newDelta = current[z, x] - baseline[z, x] - others;
                ActiveLayer.Delta[region.y + z, region.x + x] = Mathf.Max(0f, newDelta);
            }

            // Recompose the region: restores composited view, protecting other layers.
            RecomposeRegion(region);
        }

        private void RecomposeRegion(RectInt region)
        {
            _compositing = true;
            float[,] bas = _terrain.GetBaseline(region);
            float[,] h   = (float[,])bas.Clone();

            foreach (var layer in _layers)
            {
                if (!layer.IsVisible) continue;
                for (int z = 0; z < region.height; z++)
                for (int x = 0; x < region.width;  x++)
                    h[z, x] = h[z, x] + layer.Delta[region.y + z, region.x + x];
            }

            _terrain.DirectSetHeights(h, region);
            _compositing = false;
        }

        private void RecomposeAll()
        {
            if (_terrain == null) return;
            _compositing = true;
            int res = _terrain.Resolution;
            var region   = new RectInt(0, 0, res, res);
            float[,] bas = _terrain.GetBaseline(region);
            float[,] h   = (float[,])bas.Clone();
            foreach (var layer in _layers)
            {
                if (!layer.IsVisible) continue;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = h[z, x] + layer.Delta[z, x];
            }
            _terrain.DirectSetHeights(h, region);
            _compositing = false;
        }
    }
}
