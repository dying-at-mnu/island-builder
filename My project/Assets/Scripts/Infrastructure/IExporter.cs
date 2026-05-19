namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Implemented by ObjExporter and ImageExporter. ExportManager holds an array
    /// of all registered IExporter instances and selects by ExportOptions.Type.
    /// </summary>
    public interface IExporter
    {
        /// <summary>
        /// The export type this exporter handles. Used by ExportManager for dispatch.
        /// </summary>
        ExportType SupportedType { get; }

        /// <summary>
        /// Writes the export artifact to <paramref name="path"/>.
        /// </summary>
        /// <param name="path">
        /// Absolute destination path. ObjExporter writes both .obj and .mtl
        /// (replacing the extension on the .mtl); ImageExporter writes a .png.
        /// </param>
        /// <param name="opts">Format-specific options.</param>
        /// <returns>True if the export succeeded; false otherwise.</returns>
        bool Export(string path, ExportOptions opts);
    }
}
