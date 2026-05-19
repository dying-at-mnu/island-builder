using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.IO;
using UnityEngine;

namespace IslandBuilder.Infrastructure
{
    /// <summary>
    /// Converts binary DWG files to DXF by shelling out to the free
    /// ODA File Converter (formerly Teigha File Converter).
    ///
    /// Download (free): https://www.opendesign.com/guestfiles/oda_file_converter
    ///
    /// ODA works on folders rather than single files.  The method copies the DWG
    /// to a temporary input folder, runs the converter, then returns the path of
    /// the generated DXF in a temporary output folder.  The caller is responsible
    /// for deleting the returned file when done.
    /// </summary>
    public static class DwgConverter
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Tries to convert <paramref name="dwgPath"/> to DXF.
        /// Returns the path to the temporary DXF file on success, null on failure.
        /// The caller must delete the returned file.
        /// </summary>
        public static string TryConvertToDxf(string dwgPath)
        {
            string converter = FindConverter();
            if (converter == null)
            {
                Debug.LogWarning(
                    "[DwgConverter] ODA File Converter not found.  " +
                    "Install it from https://www.opendesign.com/guestfiles/oda_file_converter " +
                    "to enable automatic DWG → DXF conversion.");
                return null;
            }

            string tag   = Guid.NewGuid().ToString("N").Substring(0, 8);
            string inDir = Path.Combine(Path.GetTempPath(), "SandScapeIn_"  + tag);
            string outDir= Path.Combine(Path.GetTempPath(), "SandScapeOut_" + tag);

            try
            {
                Directory.CreateDirectory(inDir);
                Directory.CreateDirectory(outDir);

                // Copy the DWG into the temp input folder.
                string inFile = Path.Combine(inDir, Path.GetFileName(dwgPath));
                File.Copy(dwgPath, inFile, overwrite: true);

                // ODA argument order:
                //   <input_dir> <output_dir> <version> <type> <recurse> <audit>
                var psi = new ProcessStartInfo
                {
                    FileName               = converter,
                    Arguments              = $"\"{inDir}\" \"{outDir}\" ACAD2018 DXF 0 1",
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };

                Debug.Log($"[DwgConverter] Running: {converter} {psi.Arguments}");

                using var proc = Process.Start(psi);
                if (proc == null) return null;
                proc.WaitForExit(30_000); // 30 s timeout

                // Grab the first DXF produced.
                var dxfs = Directory.GetFiles(outDir, "*.dxf",
                    SearchOption.AllDirectories);
                if (dxfs.Length == 0)
                {
                    Debug.LogWarning("[DwgConverter] ODA ran but produced no .dxf output.");
                    return null;
                }

                // Move it somewhere stable (outDir will be deleted in finally).
                string result = Path.Combine(Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(dwgPath) + "_oda_" + tag + ".dxf");
                File.Copy(dxfs[0], result, overwrite: true);
                Debug.Log($"[DwgConverter] Converted to: {result}");
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DwgConverter] Conversion failed: {e.Message}");
                return null;
            }
            finally
            {
                TryDeleteDir(inDir);
                TryDeleteDir(outDir);
            }
        }

        public static bool ConverterAvailable() => FindConverter() != null;

        /// <summary>
        /// Returns true when the file header starts with "AC" followed by a digit —
        /// the binary DWG magic.  DXF files (even those saved with a .dwg extension)
        /// are plain text beginning with a group-code line, not "AC".
        /// </summary>
        public static bool IsBinaryDwg(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var hdr = new byte[6];
                if (fs.Read(hdr, 0, 6) < 6) return false;
                return hdr[0] == (byte)'A' && hdr[1] == (byte)'C'
                    && char.IsDigit((char)hdr[2]);
            }
            catch { return false; }
        }

        // ── Discovery ─────────────────────────────────────────────────────────

        private static readonly string[] ExeNames =
        {
            "ODAFileConverter.exe",
            "TeighaFileConverter.exe",
            "ODAFileConverter",       // Linux / macOS (no .exe)
            "TeighaFileConverter",
        };

        private static string FindConverter()
        {
            // 1. Fixed well-known paths (Windows + macOS + Linux)
            var fixedPaths = new[]
            {
                @"C:\Program Files\ODA\ODAFileConverter\ODAFileConverter.exe",
                @"C:\Program Files (x86)\ODA\ODAFileConverter\ODAFileConverter.exe",
                @"C:\Program Files\ODA\Teigha File Converter\TeighaFileConverter.exe",
                @"C:\Program Files (x86)\ODA\Teigha File Converter\TeighaFileConverter.exe",
                "/usr/bin/ODAFileConverter",
                "/usr/local/bin/ODAFileConverter",
                "/opt/ODA/ODAFileConverter",
                "/opt/oda/ODAFileConverter",
                "/Applications/ODAFileConverter.app/Contents/MacOS/ODAFileConverter",
            };
            foreach (var p in fixedPaths)
                if (File.Exists(p)) return p;

            // 2. Scan all versioned sub-dirs under C:\Program Files\ODA
            foreach (var root in new[]
                { @"C:\Program Files\ODA", @"C:\Program Files (x86)\ODA" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var sub in Directory.GetDirectories(root))
                    foreach (var exe in ExeNames)
                    {
                        var full = Path.Combine(sub, exe);
                        if (File.Exists(full)) return full;
                    }
            }

            // 3. Search every directory in the system PATH.
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var exe in ExeNames)
                {
                    var full = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full)) return full;
                }
            }

            return null;
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best-effort */ }
        }
    }
}
