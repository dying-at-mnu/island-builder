namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// A single 3D sample point fed into ContourRasteriser.
    /// Represents one vertex from a DXF contour polyline or an OBJ mesh.
    /// X/Z are horizontal world-space positions; Elevation is height in metres.
    /// </summary>
    public struct ContourPoint
    {
        public float X;          // World X  (DXF X,  OBJ X)
        public float Z;          // World Z  (DXF Y,  OBJ Z)
        public float Elevation;  // Metres   (DXF Z,  OBJ Y)
    }
}
