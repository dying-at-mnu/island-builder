using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Recomputes the total volume of sand added above the imported baseline after
    /// each terrain edit. Fires VolumeChanged at most twice per second to avoid
    /// hammering the UI during rapid brushing.
    /// </summary>
    [AddComponentMenu("Island Builder/Volume Calculator")]
    public class VolumeCalculator : MonoBehaviour
    {
        [SerializeField] private TerrainManager _terrainManager;

        public event TerrainEvents.VolumeChangedHandler VolumeChanged;

        private bool  _dirty        = true;
        private float _lastCalcTime = -999f;
        private const float CalcInterval = 0.5f;

        public void Bind(TerrainManager terrainManager)
        {
            if (_terrainManager != null)
                _terrainManager.TerrainHeightsChanged -= OnHeightsChanged;
            _terrainManager = terrainManager;
            if (_terrainManager != null)
                _terrainManager.TerrainHeightsChanged += OnHeightsChanged;
            _dirty = true;
        }

        private void OnDestroy()
        {
            if (_terrainManager != null)
                _terrainManager.TerrainHeightsChanged -= OnHeightsChanged;
        }

        public void ForceRecalculate()
        {
            Recalculate();
            _dirty        = false;
            _lastCalcTime = Time.time;
        }

        private void OnHeightsChanged(RectInt _) => _dirty = true;

        private void Update()
        {
            if (!_dirty || Time.time - _lastCalcTime < CalcInterval) return;
            Recalculate();
            _dirty        = false;
            _lastCalcTime = Time.time;
        }

        private void Recalculate()
        {
            if (_terrainManager == null) return;
            int res = _terrainManager.Resolution;
            if (res <= 1) { VolumeChanged?.Invoke(0f); return; }

            var region   = new RectInt(0, 0, res, res);
            float[,] h   = _terrainManager.GetHeights(region);
            float[,] bas = _terrainManager.GetBaseline(region);

            // RealHeightY is the survey elevation range in metres (not exaggerated).
            float realY    = _terrainManager.RealHeightY;
            float cellArea = _terrainManager.CellWidth * _terrainManager.CellLength;

            double total = 0.0;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    double added = h[z, x] - bas[z, x];
                    if (added > 0.0)
                        total += added * realY * cellArea;
                }

            VolumeChanged?.Invoke((float)total);
        }
    }
}
