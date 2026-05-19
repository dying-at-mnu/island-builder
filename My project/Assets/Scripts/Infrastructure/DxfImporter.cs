// ── netDxf dependency ──────────────────────────────────────────────────────
// This file requires the netDxf library. To set it up:
//   1. Download the latest release from https://github.com/haplokuon/netDxf/releases
//   2. Build the netDxf project targeting .NET Standard 2.0
//   3. Place netDxf.dll (and netDxf.xml if desired) in:
//        Assets/Plugins/netDxf/netDxf.dll
//   Unity will pick it up automatically from that folder.
//
// API note: confirmed against installed DLL via reflection.
//   Polyline2D  = 2D polyline (doc.Entities.Polylines2D); elevation is a per-entity scalar.
//   Polyline3D  = 3D polyline (doc.Entities.Polylines3D); per-vertex Z is the elevation.
//   Line        = single line segment with 3D start/end points.
// ──────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;
using netDxf;
using netDxf.Entities;

// Avoid conflict with UnityEngine.Vector3 / Vector2
using DxfVec2 = netDxf.Vector2;
using DxfVec3 = netDxf.Vector3;

namespace IslandBuilder.Infrastructure
{
    public class DxfImporter : IImporter
    {
        public string SupportedExtension => "dxf";

        /// <summary>Target heightmap resolution. Must be power-of-two + 1 (e.g. 513).</summary>
        public int Resolution = 513;

        /// <summary>
        /// Layer names to treat as contour geometry. Case-insensitive.
        /// Leave empty to import every layer.
        /// </summary>
        public string[] ContourLayers = Array.Empty<string>();

        public ImportResult Import(string path)
        {
            DxfDocument doc;
            try
            {
                doc = DxfDocument.Load(path);
            }
            catch (Exception ex)
            {
                bool isDwg = System.IO.Path.GetExtension(path)
                    .TrimStart('.').Equals("dwg", StringComparison.OrdinalIgnoreCase);

                if (isDwg && DwgConverter.IsBinaryDwg(path))
                {
                    bool hasConverter = DwgConverter.ConverterAvailable();
                    return ImportResult.Failure(hasConverter
                        ? "DWG conversion by ODA failed — check the converter log."
                        : "Binary DWG cannot be read directly.\n" +
                          "Install the free ODA File Converter from " +
                          "opendesign.com/guestfiles/oda_file_converter for automatic " +
                          "conversion, or export the file as .dxf manually.");
                }

                return ImportResult.Failure($"DXF load failed: {ex.Message}");
            }

            var points = new List<ContourPoint>(4096);
            bool filter = ContourLayers != null && ContourLayers.Length > 0;

            int nPoly2D = 0, nPoly3D = 0, nLines = 0;
            float minElev = float.MaxValue, maxElev = float.MinValue;

            // Capture each polyline as a raw list of (X,Z,elev) before any scaling.
            var rawPolylines = new List<(float elev, List<UnityEngine.Vector2> pts)>();

            // ── Polyline2D (2D, per-entity elevation) ────────────────────────
            foreach (var poly in doc.Entities.Polylines2D)
            {
                if (filter && !IsContourLayer(poly.Layer.Name)) continue;
                float elev = (float)poly.Elevation;
                var linePts = new List<UnityEngine.Vector2>(poly.Vertexes.Count);
                foreach (var v in poly.Vertexes)
                {
                    float x = (float)v.Position.X, z = (float)v.Position.Y;
                    points.Add(new ContourPoint { X = x, Z = z, Elevation = elev });
                    linePts.Add(new UnityEngine.Vector2(x, z));
                    if (elev < minElev) minElev = elev;
                    if (elev > maxElev) maxElev = elev;
                }
                if (linePts.Count >= 2) rawPolylines.Add((elev, linePts));
                nPoly2D++;
            }

            // ── Polyline3D (3D, per-vertex Z) ────────────────────────────────
            foreach (var poly in doc.Entities.Polylines3D)
            {
                if (filter && !IsContourLayer(poly.Layer.Name)) continue;
                float firstElev = 0f; bool hasFirst = false;
                var linePts = new List<UnityEngine.Vector2>(poly.Vertexes.Count);
                foreach (var v in poly.Vertexes)
                {
                    float elev = (float)v.Z;
                    float x = (float)v.X, z = (float)v.Y;
                    points.Add(new ContourPoint { X = x, Z = z, Elevation = elev });
                    linePts.Add(new UnityEngine.Vector2(x, z));
                    if (!hasFirst) { firstElev = elev; hasFirst = true; }
                    if (elev < minElev) minElev = elev;
                    if (elev > maxElev) maxElev = elev;
                }
                if (linePts.Count >= 2) rawPolylines.Add((firstElev, linePts));
                nPoly3D++;
            }

            // ── Lines (single segments with 3D endpoints) ─────────────────────
            foreach (Line line in doc.Entities.Lines)
            {
                if (filter && !IsContourLayer(line.Layer.Name)) continue;
                float e0 = (float)line.StartPoint.Z, e1 = (float)line.EndPoint.Z;
                float x0 = (float)line.StartPoint.X, z0 = (float)line.StartPoint.Y;
                float x1 = (float)line.EndPoint.X,   z1 = (float)line.EndPoint.Y;
                points.Add(new ContourPoint { X = x0, Z = z0, Elevation = e0 });
                points.Add(new ContourPoint { X = x1, Z = z1, Elevation = e1 });
                rawPolylines.Add((e0, new List<UnityEngine.Vector2> { new UnityEngine.Vector2(x0, z0), new UnityEngine.Vector2(x1, z1) }));
                if (e0 < minElev) minElev = e0; if (e0 > maxElev) maxElev = e0;
                if (e1 < minElev) minElev = e1; if (e1 > maxElev) maxElev = e1;
                nLines++;
            }

            // ── Unit detection ────────────────────────────────────────────────
            (float unitScale, string unitName, bool unitKnown) = ReadUnits(doc);

            // If the header unit is unknown, try to infer it from the contour interval.
            float rawContourInterval = 0f;
            if (points.Count >= 2)
            {
                var (detScale, detName, detKnown, detInterval) =
                    DetectUnitFromContourInterval(points, minElev, maxElev);
                rawContourInterval = detInterval;
                if (!unitKnown && detKnown)
                {
                    unitScale = detScale;
                    unitName  = detName;
                    unitKnown = true;
                    Debug.Log($"[DxfImporter] Unit inferred from contour interval: " +
                              $"{unitName} (scale {unitScale}), " +
                              $"raw Z interval = {detInterval:G4}, " +
                              $"= {detInterval * unitScale:G4} m");
                }
            }

            Debug.Log($"[DxfImporter] Entities read — Polyline2D: {nPoly2D}, Polyline3D: {nPoly3D}, Lines: {nLines}. " +
                      $"Total points: {points.Count}. " +
                      $"Elevation range: {(points.Count > 0 ? minElev : 0):F3} → {(points.Count > 0 ? maxElev : 0):F3}. " +
                      $"Unit: {unitName} (scale {unitScale})");

            if (points.Count == 0)
                return ImportResult.Failure(
                    "No contour geometry found in the DXF file. " +
                    "Check that the file contains Polyline2D, Polyline3D, or Line entities " +
                    (filter ? $"on layers: {string.Join(", ", ContourLayers)}." : "on any layer."));

            // Scale coordinates to metres when the unit is known.
            float sc = (unitKnown && !Mathf.Approximately(unitScale, 1f)) ? unitScale : 1f;
            if (!Mathf.Approximately(sc, 1f))
            {
                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    points[i] = new ContourPoint { X = p.X * sc, Z = p.Z * sc, Elevation = p.Elevation * sc };
                }
                for (int i = 0; i < rawPolylines.Count; i++)
                {
                    var (e, pts) = rawPolylines[i];
                    for (int j = 0; j < pts.Count; j++) pts[j] *= sc;
                    rawPolylines[i] = (e * sc, pts);
                }
            }

            // Compute survey bounding box to offset polylines to terrain-relative space.
            float minX = float.MaxValue, minZ = float.MaxValue;
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Z < minZ) minZ = p.Z;
            }

            var (heights, worldSize, seaLevel, boundary) = ContourRasteriser.Rasterise(points, Resolution);

            // Derive the horizontal scale that ContourRasteriser may have applied
            // (e.g. 111 000 for geographic-degree inputs) so contour XZ matches terrain.
            float extentX = 0f;
            foreach (var p in points) if (p.X - minX > extentX) extentX = p.X - minX;
            float hScale = (extentX > 1e-6f) ? worldSize.x / extentX : 1f;

            // Build ContourPolyline array in terrain world-space XZ.
            var contourLines = new ContourPolyline[rawPolylines.Count];
            for (int i = 0; i < rawPolylines.Count; i++)
            {
                var (e, pts) = rawPolylines[i];
                var worldPts = new UnityEngine.Vector2[pts.Count];
                for (int j = 0; j < pts.Count; j++)
                    worldPts[j] = new UnityEngine.Vector2((pts[j].x - minX) * hScale,
                                              (pts[j].y - minZ) * hScale);
                contourLines[i] = new ContourPolyline { Elevation = e, Points = worldPts };
            }

            return new ImportResult
            {
                Success                = true,
                Heights                = heights,
                WorldSize              = worldSize,
                SeaLevel               = seaLevel,
                Resolution             = Resolution,
                DetectedUnit           = unitName,
                AppliedUnitScale       = unitKnown ? unitScale : 1f,
                NeedsScaleConfirmation = !unitKnown,
                TerrainBoundary        = boundary,
                ContourLines           = contourLines,
                ContourInterval        = rawContourInterval
            };
        }


        /// <summary>
        /// Reads the $INSUNITS header variable and returns a scale factor (DXF units →
        /// metres), a display name, and whether the unit was positively identified.
        /// Falls back to (1, "Unknown", false) for Unitless or on any error.
        /// </summary>
        private static (float scale, string name, bool known) ReadUnits(DxfDocument doc)
        {
            try
            {
                int u = (int)doc.DrawingVariables.InsUnits;
                return u switch
                {
                    1  => (0.0254f,     "Inches",      true),
                    2  => (0.3048f,     "Feet",        true),
                    3  => (1609.344f,   "Miles",       true),
                    4  => (0.001f,      "Millimetres", true),
                    5  => (0.01f,       "Centimetres", true),
                    6  => (1.0f,        "Metres",      true),
                    7  => (1000.0f,     "Kilometres",  true),
                    21 => (0.3048006f,  "US Ft",       true),
                    _  => (1.0f,        "Unknown",     false)   // 0 = Unitless
                };
            }
            catch
            {
                return (1.0f, "Unknown", false);
            }
        }

        /// <summary>
        /// Infers the horizontal/vertical unit by finding the contour interval in the raw
        /// elevation data and checking which unit makes it a standard "nice" interval.
        /// Returns (scale, name, true) only when the match is clear (within ~5 %).
        /// </summary>
        private static (float scale, string name, bool known, float rawInterval)
            DetectUnitFromContourInterval(List<ContourPoint> points, float rawMin, float rawMax)
        {
            float rawRange = rawMax - rawMin;
            if (rawRange < 1e-6f) return (1f, "Unknown", false, 0f);

            // Collect unique elevations, quantised to 4 significant figures to absorb
            // floating-point noise while keeping distinct contour levels separate.
            float sigScale = Mathf.Pow(10f, 4f - Mathf.Ceil(Mathf.Log10(Mathf.Max(1e-9f, rawRange))));
            var elevSet = new System.Collections.Generic.HashSet<int>();
            foreach (var p in points)
                elevSet.Add(Mathf.RoundToInt(p.Elevation * sigScale));

            if (elevSet.Count < 2) return (1f, "Unknown", false, 0f);

            var elevs = new System.Collections.Generic.List<float>(elevSet.Count);
            foreach (int e in elevSet) elevs.Add(e / sigScale);
            elevs.Sort();

            // Find the minimum positive gap between adjacent unique elevations,
            // ignoring tiny gaps that are rounding artefacts (< 0.05 % of range).
            float minGap = float.MaxValue;
            float noiseFloor = rawRange * 0.0005f;
            for (int i = 1; i < elevs.Count; i++)
            {
                float d = elevs[i] - elevs[i - 1];
                if (d > noiseFloor && d < minGap) minGap = d;
            }
            if (minGap == float.MaxValue) return (1f, "Unknown", false, 0f);

            // Standard contour intervals in metres.
            float[] niceMetres = { 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 2.5f, 5f, 10f, 20f, 25f, 50f, 100f };

            // Candidate unit conversions (metres per raw unit).
            (float s, string n)[] candidates = {
                (1f,       "Metres"),
                (0.3048f,  "Feet"),
                (0.01f,    "Centimetres"),
                (0.001f,   "Millimetres"),
                (0.0254f,  "Inches"),
                (1000f,    "Kilometres"),
            };

            float bestScore = float.MaxValue;
            float bestScale = 1f;
            string bestName = "Unknown";

            foreach (var (unitScale, unitName) in candidates)
            {
                float gapM = minGap * unitScale; // gap converted to metres
                // Find the nearest standard contour interval on a log scale.
                float nearestScore = float.MaxValue;
                foreach (float ni in niceMetres)
                {
                    float logDist = Mathf.Abs(Mathf.Log10(gapM / ni));
                    if (logDist < nearestScore) nearestScore = logDist;
                }
                if (nearestScore < bestScore)
                {
                    bestScore = nearestScore;
                    bestScale = unitScale;
                    bestName  = unitName;
                }
            }

            // Accept only when the best match is within ~12 % of a standard interval.
            const float Threshold = 0.05f; // log10 units ≈ 12 %
            return bestScore < Threshold
                ? (bestScale, bestName, true,  minGap)
                : (1f,        "Unknown", false, minGap);
        }

        private bool IsContourLayer(string layerName)
        {
            foreach (var name in ContourLayers)
                if (string.Equals(name, layerName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
