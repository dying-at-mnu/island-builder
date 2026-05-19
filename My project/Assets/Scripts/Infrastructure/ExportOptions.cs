namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Parameters passed to IExporter.Export(). Each exporter reads only the
    /// fields relevant to its format and ignores the rest.
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// Identifies which export type is being requested.
        /// ExportManager uses this to select the correct IExporter.
        /// </summary>
        public ExportType Type;

        /// <summary>
        /// For ImageExporter: which orthographic camera preset to use when
        /// rendering the output image.
        /// </summary>
        public ImageViewPreset ViewPreset;
    }

    public enum ExportType
    {
        Obj,
        Image
    }

    /// <summary>Orthographic view directions available for image export.</summary>
    public enum ImageViewPreset
    {
        Top,
        Front,
        Back,
        Left,
        Right
    }
}
