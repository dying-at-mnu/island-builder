using UnityEngine;

namespace IslandBuilder.Domain
{
    /// <summary>
    /// Named colour tables for depth-based water shading and elevation-based terrain shading.
    /// Shared between WaterRenderer and TerrainManager so both can be switched at runtime.
    /// </summary>
    public static class ColorTables
    {
        // ── Water depth tables ────────────────────────────────────────────────
        // Stops: (depth in display units, RGB colour, base alpha)
        // Alpha is further multiplied by the overall water-opacity slider.

        public static readonly string[] WaterNames =
        {
            "Default", "Classic", "GMT", "Warm", "Viridis",
            "Magma", "Solar", "Bathy Warm", "Warm/Cool"
        };

        public static readonly (float d, Color col, float a)[][] Water =
        {
            // 0 – Default (turquoise → navy → black)
            new[] {
                (   0f, Hex("66D2C4"), 1.00f),
                (   5f, Hex("00C7A6"), 1.00f),
                (  15f, Hex("0087CC"), 1.00f),
                (  50f, Hex("003169"), 1.00f),
                ( 200f, Hex("020A1A"), 1.00f),
                (1000f, Hex("000308"), 1.00f),
            },
            // 1 – Classic Blue (light sky → deep abyss)
            new[] {
                (   0f, Hex("B0E0FF"), 1.00f),
                (   5f, Hex("50A0D0"), 1.00f),
                (  15f, Hex("1060A0"), 1.00f),
                (  50f, Hex("083060"), 1.00f),
                ( 200f, Hex("020D20"), 1.00f),
                (1000f, Hex("000508"), 1.00f),
            },
            // 2 – GMT Ocean (pale cyan → dark navy, GEBCO-inspired)
            new[] {
                (   0f, Hex("99F0E0"), 1.00f),
                (   5f, Hex("0098C0"), 1.00f),
                (  15f, Hex("0060A0"), 1.00f),
                (  50f, Hex("003070"), 1.00f),
                ( 200f, Hex("001030"), 1.00f),
                (1000f, Hex("00040E"), 1.00f),
            },
            // 3 – Warm → Cool (sand → seafoam → deep blue)
            new[] {
                (   0f, Hex("FFD080"), 1.00f),
                (   5f, Hex("80C890"), 1.00f),
                (  15f, Hex("2080A0"), 1.00f),
                (  50f, Hex("103060"), 1.00f),
                ( 200f, Hex("081020"), 1.00f),
                (1000f, Hex("020408"), 1.00f),
            },
            // 4 – Viridis-inspired (yellow → green → teal → purple)
            new[] {
                (   0f, Hex("F8E040"), 1.00f),
                (   5f, Hex("50B870"), 1.00f),
                (  15f, Hex("208080"), 1.00f),
                (  50f, Hex("204080"), 1.00f),
                ( 200f, Hex("300848"), 1.00f),
                (1000f, Hex("180020"), 1.00f),
            },

            // 5 – Magma (matplotlib magma reversed: pale-yellow surface → black abyss)
            new[] {
                (   0f, Hex("FCFDBF"), 1.00f),
                (   5f, Hex("FE9F6D"), 1.00f),
                (  15f, Hex("DE4968"), 1.00f),
                (  50f, Hex("8C2981"), 1.00f),
                ( 200f, Hex("3B0F70"), 1.00f),
                (1000f, Hex("000010"), 1.00f),
            },

            // 6 – Solar (hot-sun palette: pale gold surface → deep near-black)
            new[] {
                (   0f, Hex("FFFFD0"), 1.00f),
                (   5f, Hex("FFD000"), 1.00f),
                (  15f, Hex("FF7000"), 1.00f),
                (  50f, Hex("C00000"), 1.00f),
                ( 200f, Hex("500000"), 1.00f),
                (1000f, Hex("100000"), 1.00f),
            },

            // 7 – Bathymetry Warm (warm-tinted bathymetric scale)
            new[] {
                (   0f, Hex("88DDCC"), 1.00f),
                (   5f, Hex("DDAA44"), 1.00f),
                (  15f, Hex("AA5522"), 1.00f),
                (  50f, Hex("661133"), 1.00f),
                ( 200f, Hex("220811"), 1.00f),
                (1000f, Hex("060205"), 1.00f),
            },

            // 8 – Warm/Cool (diverging: warm-orange surface → cool-blue abyss)
            new[] {
                (   0f, Hex("FF5A00"), 1.00f),
                (   5f, Hex("FF9966"), 1.00f),
                (  15f, Hex("9999AA"), 1.00f),
                (  50f, Hex("3355BB"), 1.00f),
                ( 200f, Hex("0A1A55"), 1.00f),
                (1000f, Hex("020509"), 1.00f),
            },
        };

        // ── Terrain elevation tables ──────────────────────────────────────────
        // Stops: (normalised height 0-1, RGB colour)
        // Height is normalised to the [minH, maxH] range of the imported terrain.

        public static readonly string[] TerrainNames =
            { "White", "Terrain", "Sand", "Thermal", "Gray" };

        public static readonly (float t, Color col)[][] Terrain =
        {
            // 0 – Default (flat white, matches original terrain layer)
            new[] { (0f, Color.white), (1f, Color.white) },

            // 1 – Hypsometric tints (classic green-to-brown-to-beige)
            new[] {
                (0.00f, Hex("4A7C59")),
                (0.25f, Hex("8FB86A")),
                (0.50f, Hex("C8AA4A")),
                (0.75f, Hex("A07040")),
                (1.00f, Hex("D8C8B8")),
            },

            // 2 – Sand / Coastal (light to pale)
            new[] {
                (0.00f, Hex("E8D88A")),
                (0.50f, Hex("C8A860")),
                (1.00f, Hex("F8F0E0")),
            },

            // 3 – Thermal / Altitude (blue→green→yellow→red)
            new[] {
                (0.00f, Hex("3358C0")),
                (0.25f, Hex("50B840")),
                (0.50f, Hex("F0D040")),
                (0.75f, Hex("D05820")),
                (1.00f, Hex("A01010")),
            },

            // 4 – Grayscale
            new[] { (0f, Hex("202020")), (1f, Hex("F8F8F8")) },
        };

        // ── Combined terrain palette (native terrain tables + all water tables) ──
        // Indices 0..TerrainNames.Length-1  → native terrain stops (height-based).
        // Indices TerrainNames.Length..end  → water colour tables (depth-mapped).

        public static readonly string[] CombinedTerrainNames = BuildCombinedNames();
        private static string[] BuildCombinedNames()
        {
            var arr = new string[TerrainNames.Length + WaterNames.Length];
            System.Array.Copy(TerrainNames, 0, arr, 0, TerrainNames.Length);
            System.Array.Copy(WaterNames,   0, arr, TerrainNames.Length, WaterNames.Length);
            return arr;
        }

        /// <summary>
        /// Sample the combined terrain palette. Indices below TerrainNames.Length use
        /// the native terrain stops; higher indices use water colour tables, mapping
        /// normalised height to depth (low elevation = deep, high = surface).
        /// </summary>
        /// <param name="depthDisplayUnits">
        /// Real-world depth below sea level in display units. Used only when
        /// combinedIndex selects a water palette; ignored for native terrain tables.
        /// Pass 0 (or omit) when the caller does not have depth information.
        /// </param>
        public static Color SampleTerrainCombined(int combinedIndex, float normalizedHeight,
                                                  float depthDisplayUnits = 0f)
        {
            if (combinedIndex < Terrain.Length)
                return SampleTerrain(combinedIndex, normalizedHeight);

            int waterIdx = combinedIndex - Terrain.Length;
            var c = SampleWater(waterIdx, Mathf.Max(0f, depthDisplayUnits));
            return new Color(c.r, c.g, c.b, 1f); // solid alpha for terrain
        }

        // ── Sampling helpers ──────────────────────────────────────────────────

        public static Color SampleWater(int table, float depthUnits)
        {
            var stops = Water[Mathf.Clamp(table, 0, Water.Length - 1)];
            if (depthUnits <= 0f || stops.Length == 0)
            { var s0 = stops[0]; return new Color(s0.col.r, s0.col.g, s0.col.b, s0.a); }
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (depthUnits <= stops[i+1].d)
                {
                    float t = (depthUnits - stops[i].d) / (stops[i+1].d - stops[i].d);
                    return new Color(
                        Mathf.Lerp(stops[i].col.r, stops[i+1].col.r, t),
                        Mathf.Lerp(stops[i].col.g, stops[i+1].col.g, t),
                        Mathf.Lerp(stops[i].col.b, stops[i+1].col.b, t),
                        Mathf.Lerp(stops[i].a,     stops[i+1].a,     t));
                }
            }
            var last = stops[stops.Length - 1];
            return new Color(last.col.r, last.col.g, last.col.b, last.a);
        }

        public static Color SampleTerrain(int table, float normalizedHeight)
        {
            var stops = Terrain[Mathf.Clamp(table, 0, Terrain.Length - 1)];
            if (stops.Length == 0) return Color.white;
            if (normalizedHeight <= stops[0].t)  return stops[0].col;
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (normalizedHeight <= stops[i+1].t)
                {
                    float t = (normalizedHeight - stops[i].t) / (stops[i+1].t - stops[i].t);
                    return Color.Lerp(stops[i].col, stops[i+1].col, t);
                }
            }
            return stops[stops.Length - 1].col;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out var c)) return c;
            return Color.white;
        }
    }
}
