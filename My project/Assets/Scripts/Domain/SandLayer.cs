namespace IslandBuilder.Domain
{
    /// <summary>
    /// A single independent layer of user-added sand. Stores a per-cell delta
    /// (normalised [0..1]) representing how much this layer adds above the terrain
    /// baseline. Composited height = baseline + Σ(visible layer deltas).
    /// </summary>
    public class SandLayer
    {
        public string   Name      { get; set; }
        public bool     IsVisible { get; set; } = true;
        public bool     IsLocked  { get; set; } = false;

        /// <summary>Per-cell delta, indexed [z, x].  Always ≥ 0.</summary>
        public float[,] Delta { get; private set; }

        public SandLayer(string name, int resolution)
        {
            Name  = name;
            Delta = new float[resolution, resolution];
        }

        /// <summary>Resize delta when terrain resolution changes (clears content).</summary>
        public void Resize(int resolution)
        {
            Delta = new float[resolution, resolution];
        }
    }
}
