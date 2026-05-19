using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Converts added sand volume into mass and USD cost using configurable
    /// unit rates. Fires CostChanged whenever VolumeCalculator fires VolumeChanged.
    /// </summary>
    [AddComponentMenu("Island Builder/Cost Estimator")]
    public class CostEstimator : MonoBehaviour
    {
        [SerializeField, Range(1000f, 3000f)] private float _densityKgPerM3   = 1600f; // loose dredged sand
        [SerializeField, Range(1f,    500f)]  private float _costPerTonneUsd  = 25f;   // typical dredging cost

        public event TerrainEvents.CostChangedHandler CostChanged;

        public void Bind(VolumeCalculator volumeCalculator)
        {
            volumeCalculator.VolumeChanged += OnVolumeChanged;
        }

        private void OnVolumeChanged(float cubicMetres)
        {
            float massKg  = cubicMetres * _densityKgPerM3;
            float costUsd = massKg / 1000f * _costPerTonneUsd;
            CostChanged?.Invoke(massKg, costUsd);
        }
    }
}
