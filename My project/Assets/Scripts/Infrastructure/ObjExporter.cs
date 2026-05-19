using System.IO;
using System.Text;
using UnityEngine;
using IslandBuilder.Domain;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Exports the current terrain heightmap as a Wavefront OBJ mesh with a paired MTL file.
    /// One vertex per heightmap cell; two triangles per quad.
    /// </summary>
    public class ObjExporter : IExporter
    {
        private readonly ITerrainReader _reader;

        public ExportType SupportedType => ExportType.Obj;

        public ObjExporter(ITerrainReader reader) => _reader = reader;

        public bool Export(string path, ExportOptions opts)
        {
            int res = _reader.Resolution;
            if (res <= 1) return false;

            string mtlPath = Path.ChangeExtension(path, ".mtl");
            string mtlName = Path.GetFileName(mtlPath);

            try
            {
                WriteMtl(mtlPath);
                WriteObj(path, mtlName, res);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ObjExporter] Failed: {e.Message}");
                return false;
            }
        }

        private void WriteObj(string path, string mtlName, int res)
        {
            float cw     = _reader.CellWidth;
            float cl     = _reader.CellLength;
            float worldY = _reader.WorldSize.y;
            int   n      = res - 1;

            float[,] h = _reader.GetHeights(new RectInt(0, 0, res, res));

            using var w = new StreamWriter(path, false, Encoding.UTF8, bufferSize: 1 << 20);
            w.WriteLine("# Island Builder terrain export");
            w.WriteLine($"mtllib {mtlName}");
            w.WriteLine("o Terrain");

            // Vertices  (X right, Y up, Z forward — standard Unity/OBJ)
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    w.WriteLine($"v {x * cw:F3} {h[z, x] * worldY:F3} {z * cl:F3}");

            // UV coordinates (0..1 over the terrain footprint)
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    w.WriteLine($"vt {(float)x / n:F5} {(float)z / n:F5}");

            w.WriteLine("usemtl Sand");
            w.WriteLine("s off");

            // Faces — two CCW triangles per quad, 1-indexed
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int bl = z * res + x + 1;
                int br = bl + 1;
                int tl = bl + res;
                int tr = tl + 1;
                w.WriteLine($"f {bl}/{bl} {br}/{br} {tr}/{tr}");
                w.WriteLine($"f {bl}/{bl} {tr}/{tr} {tl}/{tl}");
            }
        }

        private static void WriteMtl(string path)
        {
            using var w = new StreamWriter(path, false, Encoding.UTF8);
            w.WriteLine("# Island Builder material");
            w.WriteLine("newmtl Sand");
            w.WriteLine("Kd 0.76 0.70 0.50");
            w.WriteLine("Ka 0.10 0.10 0.10");
            w.WriteLine("Ks 0.00 0.00 0.00");
            w.WriteLine("illum 1");
        }
    }
}
