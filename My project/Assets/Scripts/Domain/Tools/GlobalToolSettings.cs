namespace IslandBuilder.Domain.Tools
{
    /// <summary>Shared settings that modify how all brush tools behave.</summary>
    public class GlobalToolSettings
    {
        /// <summary>When true, tools only affect cells whose current height > EditAboveHeight.</summary>
        public bool  EditAboveEnabled { get; set; } = false;

        /// <summary>Height threshold in display metres.</summary>
        public float EditAboveHeight  { get; set; } = 0f;

        /// <summary>
        /// When true, dredge / smooth / fill / clear instantly act on the entire
        /// lasso area in one click. Other tools still use the brush but respect the lasso.
        /// </summary>
        public bool  FillEntireLasso      { get; set; } = false;

        /// <summary>
        /// When true the next ctrl+click on terrain sets EditAboveHeight instead of
        /// invoking the active tool's OnMouseDown. SculptController clears this flag
        /// immediately after consuming the click.
        /// </summary>
        public bool  IsSamplingEditAbove  { get; set; } = false;
    }
}
