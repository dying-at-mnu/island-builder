namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Implemented by DxfImporter and ObjImporter. ImportManager selects the correct
    /// implementation by file extension and calls Import().
    ///
    /// Implementations may run synchronously or offload heavy work to a background
    /// thread; either way they must return a fully populated ImportResult.
    /// </summary>
    public interface IImporter
    {
        /// <summary>
        /// File extension this importer handles, without the leading dot and
        /// lowercased (e.g. "dxf", "obj"). Used by ImportManager for dispatch.
        /// </summary>
        string SupportedExtension { get; }

        /// <summary>
        /// Reads the file at <paramref name="path"/> and converts it into a
        /// normalised heightmap grid.
        /// </summary>
        /// <param name="path">Absolute path to the source file.</param>
        /// <returns>
        /// ImportResult with Success=true and populated Heights/WorldSize/Resolution,
        /// or Success=false and a non-null Error string.
        /// </returns>
        ImportResult Import(string path);
    }
}
