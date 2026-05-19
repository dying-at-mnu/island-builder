using UnityEngine;

namespace IslandBuilder.Presentation
{
    /// <summary>
    /// Generates 24×24 pixel-art Texture2D icons for each sculpt tool at runtime.
    /// No external assets required — all shapes are drawn programmatically.
    /// </summary>
    public static class ToolIconFactory
    {
        private const int S = 24; // canvas size

        public static Texture2D Raise()
        {
            // Upward-pointing filled triangle + thick base bar
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                float height = S * 0.70f;
                float top    = S * 0.08f;
                float slope  = (y - top) / height;
                float halfW  = slope * (S * 0.5f - 1f);
                bool triangle = y >= top && y <= top + height && Mathf.Abs(x - cx) <= halfW;
                bool bar      = y >= S * 0.80f && y <= S * 0.92f && x >= 1 && x < S - 1;
                return triangle || bar ? 1f : 0f;
            });
        }

        public static Texture2D Erase()
        {
            // Downward-pointing triangle (hollow centre to distinguish from Raise)
            return Build((x, y) =>
            {
                float cx     = S * 0.5f;
                float top    = S * 0.08f;
                float height = S * 0.70f;
                float bot    = top + height;
                float slope  = (bot - y) / height;
                float halfW  = slope * (S * 0.5f - 1f);
                if (y < top || y > bot) return 0f;
                float dist = Mathf.Abs(x - cx);
                // Outer edge (filled rim ~3px)
                float rim = 3f;
                float innerSlope = Mathf.Max(0f, (bot - y - rim) / height);
                float innerHalfW = innerSlope * (S * 0.5f - 1f - rim);
                bool inside  = dist <= halfW;
                bool inHole  = dist < innerHalfW && y < bot - rim;
                return (inside && !inHole) ? 1f : 0f;
            });
        }

        public static Texture2D Flatten()
        {
            // Three horizontal bars of decreasing width (= level layers)
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                bool bar1 = y >= 4  && y <= 7  && Mathf.Abs(x - cx) < S * 0.46f;
                bool bar2 = y >= 11 && y <= 14 && Mathf.Abs(x - cx) < S * 0.38f;
                bool bar3 = y >= 17 && y <= 20 && Mathf.Abs(x - cx) < S * 0.30f;
                return (bar1 || bar2 || bar3) ? 1f : 0f;
            });
        }

        public static Texture2D Smooth()
        {
            // Sine-wave path (thick stroke)
            return Build((x, y) =>
            {
                float cy   = S * 0.5f;
                float amp  = S * 0.30f;
                float freq = Mathf.PI * 2f / S;
                float wave = cy + Mathf.Sin(x * freq - Mathf.PI * 0.5f) * amp;
                float dist = Mathf.Abs(y - wave);
                return dist <= 2f ? Mathf.Clamp01(1f - (dist - 1f)) : 0f;
            });
        }

        public static Texture2D Cut()
        {
            // X shape (two diagonal bars crossing)
            return Build((x, y) =>
            {
                float d1 = Mathf.Abs((y - x) + (S * 0.04f));          // top-left → bottom-right
                float d2 = Mathf.Abs((y + x) - (S - 1) - (S * 0.04f)); // top-right → bottom-left
                bool onDiag = (d1 <= 2f || d2 <= 2f);
                // clip corners so it doesn't bleed off the rounded area
                bool inBounds = x >= 2 && x < S - 2 && y >= 2 && y < S - 2;
                return (onDiag && inBounds) ? 1f : 0f;
            });
        }

        public static Texture2D Fill()
        {
            // Filled land mass (top 60 %) above a wavy sea-level line
            return Build((x, y) =>
            {
                float seaY    = S * 0.60f;
                float waveAmp = 1.5f;
                float wave    = seaY + Mathf.Sin(x * Mathf.PI * 2f / S) * waveAmp;
                // Land above sea level
                if (y < wave - 1f && y > 2 && x > 1 && x < S - 2)
                    return 1f;
                // Wave stroke
                if (Mathf.Abs(y - wave) <= 1.5f)
                    return Mathf.Clamp01(1f - Mathf.Abs(y - wave));
                // Water dots below
                if (y > wave + 2f && y < S - 3)
                {
                    bool dotX = ((int)x % 4) == 1;
                    bool dotY = ((int)(y - (wave + 2f)) % 4) == 0;
                    return (dotX && dotY) ? 0.65f : 0f;
                }
                return 0f;
            });
        }

        // ── Top-bar icons ─────────────────────────────────────────────────────

        public static Texture2D Import()
        {
            // Box outline at bottom; downward arrow into it.
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                float boxY0 = S * 0.58f, boxY1 = S * 0.90f;
                float boxX0 = 2f,        boxX1 = S - 3f;
                bool boxBorder = y >= boxY0 && y <= boxY1 && x >= boxX0 && x <= boxX1
                              && (y <= boxY0 + 2f || y >= boxY1 - 2f || x <= boxX0 + 2f || x >= boxX1 - 2f);
                bool stem = x >= cx - 1.5f && x <= cx + 1.5f && y >= S * 0.08f && y <= S * 0.58f;
                // Downward arrowhead
                float ahTop = S * 0.50f, ahH = S * 0.16f;
                float frac  = (y - ahTop) / ahH;
                bool arrow  = y >= ahTop && y <= ahTop + ahH && Mathf.Abs(x - cx) <= frac * S * 0.24f + 1.5f;
                return (boxBorder || stem || arrow) ? 1f : 0f;
            });
        }

        public static Texture2D Export()
        {
            // Box outline at bottom; upward arrow from it.
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                float boxY0 = S * 0.58f, boxY1 = S * 0.90f;
                float boxX0 = 2f,        boxX1 = S - 3f;
                bool boxBorder = y >= boxY0 && y <= boxY1 && x >= boxX0 && x <= boxX1
                              && (y <= boxY0 + 2f || y >= boxY1 - 2f || x <= boxX0 + 2f || x >= boxX1 - 2f);
                bool stem = x >= cx - 1.5f && x <= cx + 1.5f && y >= S * 0.20f && y <= S * 0.65f;
                // Upward arrowhead
                float ahBot = S * 0.28f, ahH = S * 0.16f;
                float frac  = (ahBot - y) / ahH;
                bool arrow  = y <= ahBot && y >= ahBot - ahH && Mathf.Abs(x - cx) <= frac * S * 0.24f + 1.5f;
                return (boxBorder || stem || arrow) ? 1f : 0f;
            });
        }

        public static Texture2D Lock()
        {
            // Padlock: arc shackle at top, filled rectangular body below.
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                // Shackle arc (hollow U)
                float arcCy = S * 0.44f, arcRo = S * 0.22f, arcRi = S * 0.13f;
                float dx = x - cx, dy = y - arcCy;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                bool shackle = d >= arcRi && d <= arcRo + 1f && y <= arcCy && y >= S * 0.10f;
                // Body
                float bX0 = S * 0.15f, bX1 = S * 0.85f, bY0 = S * 0.48f, bY1 = S * 0.88f;
                bool body = x >= bX0 && x <= bX1 && y >= bY0 && y <= bY1;
                // Keyhole circle
                float kd = Mathf.Sqrt((x - cx) * (x - cx) + (y - S * 0.63f) * (y - S * 0.63f));
                bool keyhole = kd <= S * 0.09f;
                return (shackle || (body && !keyhole)) ? 1f : 0f;
            });
        }

        public static Texture2D Highlight()
        {
            // Sunburst: filled centre diamond + 8 radiating spokes.
            return Build((x, y) =>
            {
                float cx = S * 0.5f, cy = S * 0.5f;
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // Centre core
                bool core = Mathf.Abs(dx) + Mathf.Abs(dy) <= S * 0.18f;
                // 8 spokes: cardinal + diagonal, from radius 0.22 to 0.44
                float ang = Mathf.Atan2(dy, dx) * 4f / Mathf.PI; // 0..4 quadrant index
                float nearAxis = Mathf.Abs(ang - Mathf.Round(ang));
                bool spoke = dist >= S * 0.22f && dist <= S * 0.44f && nearAxis <= 0.09f;
                return (core || spoke) ? 1f : 0f;
            });
        }

        public static Texture2D Grid()
        {
            // Hash (#) symbol: two vertical + two horizontal lines forming a 3×3 grid.
            return Build((x, y) =>
            {
                float t = S / 3f, tt = 2f * S / 3f, thick = 1.8f;
                bool vl = (Mathf.Abs(x - t) <= thick || Mathf.Abs(x - tt) <= thick)
                       && y >= 2 && y <= S - 3;
                bool hl = (Mathf.Abs(y - t) <= thick || Mathf.Abs(y - tt) <= thick)
                       && x >= 2 && x <= S - 3;
                return (vl || hl) ? 1f : 0f;
            });
        }

        public static Texture2D Undo()
        {
            // Counterclockwise arc; arrowhead at the upper-right pointing left (CCW).
            return Build((x, y) =>
            {
                float cx = S * 0.5f, cy = S * 0.5f;
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float r    = S * 0.36f;
                float ang  = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                // Arc covers ~280°; gap at upper-right (0°→80°).
                bool arc = Mathf.Abs(dist - r) <= 2.2f && !(ang >= 0f && ang <= 80f);

                // Arrowhead at gap-end (80°), pointing in the CCW tangent direction.
                float gRad = 80f * Mathf.Deg2Rad;
                float tgx  = -Mathf.Sin(gRad), tgy = Mathf.Cos(gRad); // CCW tangent
                float rdx  =  Mathf.Cos(gRad), rdy = Mathf.Sin(gRad);
                float lx = x - (cx + r * rdx), ly = y - (cy + r * rdy);
                float tc = lx * tgx + ly * tgy, rc = lx * rdx + ly * rdy;
                float sz = S * 0.16f;
                bool head = tc >= -sz && tc <= 0f && Mathf.Abs(rc) <= sz * (1f + tc / sz);

                return (arc || head) ? 1f : 0f;
            });
        }

        public static Texture2D Redo()
        {
            // Clockwise arc; arrowhead at the upper-left pointing right (CW).
            return Build((x, y) =>
            {
                float cx = S * 0.5f, cy = S * 0.5f;
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float r    = S * 0.36f;
                float ang  = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

                // Arc covers ~280°; gap at upper-left (100°→180°).
                bool arc = Mathf.Abs(dist - r) <= 2.2f && !(ang >= 100f && ang <= 180f);

                // Arrowhead at gap-start (100°), pointing in the CW tangent direction.
                float gRad = 100f * Mathf.Deg2Rad;
                float tgx  =  Mathf.Sin(gRad), tgy = -Mathf.Cos(gRad); // CW tangent
                float rdx  =  Mathf.Cos(gRad), rdy =  Mathf.Sin(gRad);
                float lx = x - (cx + r * rdx), ly = y - (cy + r * rdy);
                float tc = lx * tgx + ly * tgy, rc = lx * rdx + ly * rdy;
                float sz = S * 0.16f;
                bool head = tc >= -sz && tc <= 0f && Mathf.Abs(rc) <= sz * (1f + tc / sz);

                return (arc || head) ? 1f : 0f;
            });
        }

        public static Texture2D SaveLayer()
        {
            // Three stacked horizontal bars (layers) + small downward arrow on right.
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                // Bars
                bool b1 = y >= S * 0.12f && y <= S * 0.26f && x >= 2 && x < S - 2;
                bool b2 = y >= S * 0.38f && y <= S * 0.52f && x >= 2 && x < S - 2;
                bool b3 = y >= S * 0.63f && y <= S * 0.77f && x >= 2 && x < S - 2;
                // Downward arrow below bars
                float ahTop = S * 0.80f, ahH = S * 0.14f;
                bool stem = x >= cx - 1.5f && x <= cx + 1.5f && y >= ahTop && y <= ahTop + ahH * 0.5f;
                float frac = (y - (ahTop + ahH * 0.4f)) / (ahH * 0.6f);
                bool head  = y >= ahTop + ahH * 0.4f && y <= ahTop + ahH
                          && Mathf.Abs(x - cx) <= frac * S * 0.18f + 1.5f;
                return (b1 || b2 || b3 || stem || head) ? 1f : 0f;
            });
        }

        public static Texture2D LoadLayer()
        {
            // Three stacked horizontal bars (layers) + small upward arrow on right.
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                // Bars (shifted up slightly to make room for arrow below)
                bool b1 = y >= S * 0.22f && y <= S * 0.35f && x >= 2 && x < S - 2;
                bool b2 = y >= S * 0.46f && y <= S * 0.59f && x >= 2 && x < S - 2;
                bool b3 = y >= S * 0.70f && y <= S * 0.83f && x >= 2 && x < S - 2;
                // Upward arrow above bars
                float ahBot = S * 0.18f, ahH = S * 0.14f;
                bool stem = x >= cx - 1.5f && x <= cx + 1.5f && y >= ahBot - ahH * 0.5f && y <= ahBot;
                float frac = (ahBot - ahH * 0.4f - y) / (ahH * 0.6f);
                bool head  = y <= ahBot - ahH * 0.4f && y >= ahBot - ahH
                          && Mathf.Abs(x - cx) <= frac * S * 0.18f + 1.5f;
                return (b1 || b2 || b3 || stem || head) ? 1f : 0f;
            });
        }

        public static Texture2D Reset()
        {
            // Circular arrow: arc ~280° with a filled arrowhead at the open end.
            return Build((x, y) =>
            {
                float cx = S * 0.5f, cy = S * 0.5f;
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float r    = S * 0.36f;

                // Angle in degrees; in Build() y grows downward so atan2 matches screen CW.
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // –180..180

                // Arc: full ring minus a ~80° gap at the top-right (ang 10..90)
                bool arc = Mathf.Abs(dist - r) <= 2.2f && !(ang >= 10f && ang <= 90f);

                // Arrowhead at gap-start (ang ≈ 10°), pointing clockwise (downward-right).
                float gRad = 10f * Mathf.Deg2Rad;
                float tipX = cx + r * Mathf.Cos(gRad);
                float tipY = cy + r * Mathf.Sin(gRad);
                // Clockwise tangent at this angle: (sin, –cos) → screen-space CW direction.
                float tgx =  Mathf.Sin(gRad), tgy = -Mathf.Cos(gRad);
                // Radial outward: (cos, sin)
                float rdx =  Mathf.Cos(gRad), rdy =  Mathf.Sin(gRad);
                float lx = x - tipX, ly = y - tipY;
                float tComp = lx * tgx + ly * tgy; // progress along tangent
                float rComp = lx * rdx + ly * rdy; // offset from circle edge
                float sz    = S * 0.16f;
                // Filled triangle: widens from tip backward.
                bool head = tComp >= -sz && tComp <= 0f
                         && Mathf.Abs(rComp) <= sz * (1f + tComp / sz);

                return (arc || head) ? 1f : 0f;
            });
        }

        public static Texture2D Calculate()
        {
            // Sigma (Σ) shape: top bar, two diagonals meeting at mid-right, bottom bar.
            return Build((x, y) =>
            {
                float x0 = S * 0.18f, x1 = S * 0.82f;
                float midY = S * 0.50f;
                // Top bar
                bool topBar = y >= S * 0.10f && y <= S * 0.22f && x >= x0 && x <= x1;
                // Bottom bar
                bool botBar = y >= S * 0.78f && y <= S * 0.90f && x >= x0 && x <= x1;
                // Upper diagonal: top-right → mid-left
                float uSlope = (midY - S * 0.16f) / (x0 - x1);
                float uExpX  = x1 + (y - S * 0.16f) / uSlope;
                bool upperDiag = y >= S * 0.16f && y <= midY && Mathf.Abs(x - uExpX) <= 1.8f;
                // Lower diagonal: mid-left → bottom-right
                float lSlope = (S * 0.84f - midY) / (x1 - x0);
                float lExpX  = x0 + (y - midY) / lSlope;
                bool lowerDiag = y >= midY && y <= S * 0.84f && Mathf.Abs(x - lExpX) <= 1.8f;
                return (topBar || botBar || upperDiag || lowerDiag) ? 1f : 0f;
            });
        }

        public static Texture2D Camera()
        {
            // Camera body (rectangle) + lens circle + viewfinder nub on top
            return Build((x, y) =>
            {
                float cx = S * 0.5f;
                float bodyX0 = 2f, bodyX1 = S - 3f;
                float bodyY0 = S * 0.35f, bodyY1 = S * 0.82f;
                // Viewfinder bump centred on top edge of body
                float vfX0 = cx - 3f, vfX1 = cx + 3f;
                float vfY0 = S * 0.20f, vfY1 = bodyY0;
                bool body = x >= bodyX0 && x <= bodyX1 && y >= bodyY0 && y <= bodyY1;
                bool vf   = x >= vfX0  && x <= vfX1  && y >= vfY0   && y <= vfY1;
                // Lens: circle outline inside body
                float lensR = S * 0.18f;
                float lensY = (bodyY0 + bodyY1) * 0.5f + 1f;
                float d     = Mathf.Sqrt((x - cx) * (x - cx) + (y - lensY) * (y - lensY));
                bool lens   = d <= lensR + 1.5f && d >= lensR - 1.5f;
                bool lensFill = d <= lensR - 1.5f;
                if (body || vf) return 1f;
                if (lens)       return 0.85f;
                if (lensFill)   return 0.30f;
                return 0f;
            });
        }

        public static Texture2D Blend()
        {
            // Two overlapping circles (Venn diagram) — left solid, overlap lighter.
            return Build((x, y) =>
            {
                float cy = S * 0.5f;
                float lCx = S * 0.34f, rCx = S * 0.66f, r = S * 0.30f;
                float lD = Mathf.Sqrt((x - lCx) * (x - lCx) + (y - cy) * (y - cy));
                float rD = Mathf.Sqrt((x - rCx) * (x - rCx) + (y - cy) * (y - cy));
                bool inL = lD <= r, inR = rD <= r;
                if (inL && inR) return 0.55f;   // overlap: mid-brightness
                if (inL || inR) return 1f;       // single circle: full
                return 0f;
            });
        }

        public static Texture2D Lasso()
        {
            // Oval loop with a small tail (lasso shape).
            return Build((x, y) =>
            {
                float cx = S * 0.50f, cy = S * 0.42f;
                float rx = S * 0.32f, ry = S * 0.24f;
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt((dx / rx) * (dx / rx) + (dy / ry) * (dy / ry));
                bool ring = Mathf.Abs(dist - 1f) <= 0.13f;
                // Tail going down-right from bottom of oval
                float tailY0 = cy + ry, tailY1 = S * 0.90f;
                float tailX  = cx + (y - tailY0) * 0.35f;
                bool tail = y >= tailY0 && y <= tailY1 && Mathf.Abs(x - tailX) <= 1.5f;
                return (ring || tail) ? 1f : 0f;
            });
        }

        public static Texture2D FillHeight()
        {
            // Terrain silhouette being filled up to a horizontal line.
            return Build((x, y) =>
            {
                float fillY = S * 0.45f; // target fill level
                // Horizontal fill-line
                bool line = Mathf.Abs(y - fillY) <= 1.5f && x >= 2 && x <= S - 3;
                // Upward arrows below the line
                float cx = S * 0.5f;
                bool stem = x >= cx - 1.5f && x <= cx + 1.5f && y > fillY && y <= S * 0.82f;
                float ahTop = S * 0.55f, ahH = S * 0.14f;
                float frac  = (ahTop - y) / ahH;
                bool head   = y >= ahTop - ahH && y <= ahTop
                           && Mathf.Abs(x - cx) <= frac * S * 0.20f + 1.5f;
                return (line || stem || head) ? 1f : 0f;
            });
        }

        public static Texture2D Clear()
        {
            // Eraser block: filled rounded rectangle with a diagonal stripe cut through it.
            return Build((x, y) =>
            {
                float x0 = 2f, x1 = S - 3f, y0 = 5f, y1 = S - 5f;
                bool block = x >= x0 && x <= x1 && y >= y0 && y <= y1;
                // Diagonal stripe (top-left to bottom-right) cuts through the block
                float stripe = (x - x0) - (y - y0) * ((x1 - x0) / (y1 - y0));
                bool cut = Mathf.Abs(stripe) <= 2.5f;
                return (block && !cut) ? 1f : 0f;
            });
        }

        public static Texture2D Measure()
        {
            // Crosshair with four tick marks
            return Build((x, y) =>
            {
                float cx = S * 0.5f, cy = S * 0.5f;
                float r  = S * 0.38f;
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                // Circle ring
                bool ring = Mathf.Abs(dist - r) <= 1.5f;
                // Crosshair lines (short, from circle inward)
                bool hLine = Mathf.Abs(y - cy) <= 1f && Mathf.Abs(x - cx) <= r;
                bool vLine = Mathf.Abs(x - cx) <= 1f && Mathf.Abs(y - cy) <= r;
                // Centre dot
                bool dot = dist <= 2f;
                return (ring || hLine || vLine || dot) ? 1f : 0f;
            });
        }

        // ── Core builder ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds a Texture2D by evaluating coverage(x, y) for each pixel.
        /// coverage returns [0..1]; 0 = transparent, 1 = opaque white.
        /// </summary>
        private static Texture2D Build(System.Func<float, float, float> coverage)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            for (int py = 0; py < S; py++)
                for (int px = 0; px < S; px++)
                {
                    // Super-sample 2×2 for cheap anti-aliasing
                    float sum = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            sum += coverage(px + sx * 0.5f, py + sy * 0.5f);
                    float alpha = sum * 0.25f;
                    // Texture2D y=0 is bottom; flip to match screen y=0 at top.
                    tex.SetPixel(px, S - 1 - py, new Color(1f, 1f, 1f, alpha));
                }

            tex.Apply();
            return tex;
        }

        public static Texture2D Beach()
        {
            // Horizontal sea-level line in lower half; arc/dome of sand above it.
            return Build((x, y) =>
            {
                float cy   = S * 0.42f; // sea-level line
                float onSea = Mathf.Abs(y - cy) < 1.5f ? 1f : 0f;
                // Sandy dome above sea level
                float cx   = S * 0.5f;
                float rx   = S * 0.42f, ry = S * 0.36f;
                float ex   = (x - cx) / rx, ey = (y - cy) / ry;
                float dome = ex * ex + ey * ey;
                bool  inDome = dome <= 1f && y >= cy;
                float rim    = dome >= 0.7f && dome <= 1f && y >= cy ? 1f : 0f;
                if (onSea > 0f) return 0.85f;
                if (rim  > 0f) return 1f;
                if (inDome)    return 0.45f;
                return 0f;
            });
        }
    }
}
