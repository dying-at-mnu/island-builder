using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandBuilder.Interaction
{
    /// <summary>
    /// Renders the active brush boundary (ring) and a semi-transparent filled area.
    /// Supports circle (default), square, and star shapes for the RaiseTool shape mode.
    /// shapeType: 0 = circle, 1 = square, 2 = star.
    /// </summary>
    [AddComponentMenu("Island Builder/Brush Preview")]
    public class BrushPreview : MonoBehaviour
    {
        private const float StarInnerRatio = 0.45f;

        private LineRenderer _ring;
        private MeshFilter   _fillFilter;
        private MeshRenderer _fillRenderer;
        private Mesh         _fillMesh;

        private static readonly List<Vector3> _verts = new();
        private static readonly List<int>     _tris  = new();

        private void Awake()
        {
            _ring = gameObject.AddComponent<LineRenderer>();
            _ring.useWorldSpace     = true;
            _ring.loop              = true;
            _ring.positionCount     = 64;
            _ring.widthMultiplier   = 3f;
            _ring.shadowCastingMode = ShadowCastingMode.Off;
            _ring.receiveShadows    = false;

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white,                 0f),
                        new GradientColorKey(new Color(1f, 0.90f, 0.25f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            _ring.colorGradient  = grad;
            _ring.sharedMaterial = BuildRingMaterial();
            _ring.enabled        = false;

            var fillGo = new GameObject("BrushFill");
            fillGo.transform.SetParent(transform, false);
            _fillFilter   = fillGo.AddComponent<MeshFilter>();
            _fillRenderer = fillGo.AddComponent<MeshRenderer>();
            _fillMesh     = new Mesh { name = "BrushFill" };
            _fillFilter.sharedMesh          = _fillMesh;
            _fillRenderer.sharedMaterial    = BuildFillMaterial();
            _fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _fillRenderer.receiveShadows    = false;
            _fillRenderer.enabled           = false;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Circle / smooth-mode preview.</summary>
        public void Show(Vector3 worldPos, float radiusMetres)
            => Show(worldPos, radiusMetres, 0, 5, 0f);

        /// <summary>
        /// Shape-mode preview. shapeType: 0=circle, 1=square, 2=star, 3=triangle.
        /// yawDeg rotates the shape to match the camera orientation.
        /// </summary>
        public void Show(Vector3 worldPos, float radiusMetres, int shapeType, int starPoints,
                         float yawDeg = 0f)
        {
            if (radiusMetres <= 0f) { Hide(); return; }

            const float RingOff = 0.5f;
            const float FillOff = 0.3f;

            _ring.enabled         = true;
            _fillRenderer.enabled = true;

            switch (shapeType)
            {
                case 1:  BuildSquare  (worldPos, radiusMetres, yawDeg, RingOff, FillOff); break;
                case 2:  BuildStar    (worldPos, radiusMetres, starPoints, yawDeg, RingOff, FillOff); break;
                case 3:  BuildTriangle(worldPos, radiusMetres, yawDeg, RingOff, FillOff); break;
                default: BuildCircle  (worldPos, radiusMetres, RingOff, FillOff); break;
            }
        }

        public void Hide()
        {
            _ring.enabled         = false;
            _fillRenderer.enabled = false;
        }

        // ── Shape builders ────────────────────────────────────────────────────

        // ── Terrain height sampling ───────────────────────────────────────────

        private static float TerrainY(float x, float z, float fallback)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(new Vector3(x, 0f, z)) : fallback;
        }

        // ── Shape builders ────────────────────────────────────────────────────

        private void BuildCircle(Vector3 c, float r, float ringOff, float fillOff)
        {
            const int N = 64;
            _ring.positionCount = N;
            for (int i = 0; i < N; i++)
            {
                float a = (float)i / N * Mathf.PI * 2f;
                float vx = c.x + Mathf.Cos(a) * r;
                float vz = c.z + Mathf.Sin(a) * r;
                _ring.SetPosition(i, new Vector3(vx, TerrainY(vx, vz, c.y) + ringOff, vz));
            }
            BuildFanMesh(c, fillOff, N,
                i => new Vector2(Mathf.Cos((float)i / N * Mathf.PI * 2f) * r,
                                 Mathf.Sin((float)i / N * Mathf.PI * 2f) * r));
        }

        // Rotate a local (dx, dz) offset by yawDeg around Y.
        private static (float rx, float rz) Rot(float dx, float dz, float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return (dx * c - dz * s, dx * s + dz * c);
        }

        private void BuildSquare(Vector3 c, float r, float yawDeg, float ringOff, float fillOff)
        {
            float[] lx = { -1, 1, 1, -1 };
            float[] lz = { -1, -1, 1, 1 };
            _ring.positionCount = 4;
            for (int i = 0; i < 4; i++)
            {
                var (rx, rz) = Rot(lx[i] * r, lz[i] * r, yawDeg);
                float vx = c.x + rx, vz = c.z + rz;
                _ring.SetPosition(i, new Vector3(vx, TerrainY(vx, vz, c.y) + ringOff, vz));
            }
            BuildFanMesh(c, fillOff, 4, i => { var (rx, rz) = Rot(lx[i]*r, lz[i]*r, yawDeg); return new Vector2(rx, rz); });
        }

        private void BuildStar(Vector3 c, float r, int points, float yawDeg, float ringOff, float fillOff)
        {
            points = Mathf.Max(3, points);
            int N  = points * 2;
            _ring.positionCount = N;
            for (int i = 0; i < N; i++)
            {
                float a   = (float)i / N * Mathf.PI * 2f;
                float eff = (i % 2 == 0) ? r : r * StarInnerRatio;
                var (rx, rz) = Rot(Mathf.Cos(a) * eff, Mathf.Sin(a) * eff, yawDeg);
                float vx = c.x + rx, vz = c.z + rz;
                _ring.SetPosition(i, new Vector3(vx, TerrainY(vx, vz, c.y) + ringOff, vz));
            }
            BuildFanMesh(c, fillOff, N,
                i => { float a = (float)i / N * Mathf.PI * 2f;
                       float e = (i % 2 == 0) ? r : r * StarInnerRatio;
                       var (rx, rz) = Rot(Mathf.Cos(a) * e, Mathf.Sin(a) * e, yawDeg);
                       return new Vector2(rx, rz); });
        }

        private void BuildTriangle(Vector3 c, float r, float yawDeg, float ringOff, float fillOff)
        {
            // Vertices at 90°, 210°, 330° on the unit circle (pointing "up").
            float a0 = 90f * Mathf.Deg2Rad, a1 = 210f * Mathf.Deg2Rad, a2 = 330f * Mathf.Deg2Rad;
            float[] ax = { Mathf.Cos(a0)*r, Mathf.Cos(a1)*r, Mathf.Cos(a2)*r };
            float[] az = { Mathf.Sin(a0)*r, Mathf.Sin(a1)*r, Mathf.Sin(a2)*r };
            _ring.positionCount = 3;
            for (int i = 0; i < 3; i++)
            {
                var (rx, rz) = Rot(ax[i], az[i], yawDeg);
                float vx = c.x + rx, vz = c.z + rz;
                _ring.SetPosition(i, new Vector3(vx, TerrainY(vx, vz, c.y) + ringOff, vz));
            }
            BuildFanMesh(c, fillOff, 3, i => { var (rx, rz) = Rot(ax[i], az[i], yawDeg); return new Vector2(rx, rz); });
        }

        /// <summary>
        /// Builds the fill mesh as a triangle fan from centre using N edge vertices
        /// whose XZ offsets are supplied by <paramref name="edgeOffset"/>.
        /// </summary>
        private void BuildFanMesh(Vector3 c, float yOffset,
                                  int N, System.Func<int, Vector2> edgeOffset)
        {
            _verts.Clear();
            _tris.Clear();

            _verts.Add(new Vector3(c.x, TerrainY(c.x, c.z, c.y) + yOffset, c.z)); // centre
            for (int i = 0; i < N; i++)
            {
                var o  = edgeOffset(i);
                float vx = c.x + o.x, vz = c.z + o.y;
                _verts.Add(new Vector3(vx, TerrainY(vx, vz, c.y) + yOffset, vz));
            }

            for (int i = 0; i < N; i++)
            {
                int cur  = i + 1;
                int next = (i + 1) % N + 1;
                _tris.Add(0);
                _tris.Add(next); // CW from above → upward-facing normal
                _tris.Add(cur);
            }

            _fillMesh.Clear();
            _fillMesh.SetVertices(_verts);
            _fillMesh.SetTriangles(_tris, 0);
            _fillMesh.RecalculateBounds();
        }

        // ── Materials ─────────────────────────────────────────────────────────

        private static Material BuildRingMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private static Material BuildFillMaterial()
        {
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                var mat = new Material(urp);
                mat.SetFloat("_Surface",      1f);
                mat.SetFloat("_Blend",        0f);
                mat.SetFloat("_AlphaClip",    0f);
                mat.SetFloat("_ZWrite",       0f);
                mat.SetFloat("_Cull",         0f);
                mat.SetInt("_SrcBlend",      (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend",      (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                mat.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.color       = new Color(1f, 0.90f, 0.25f, 0.22f);
                return mat;
            }
            var fb = new Material(Shader.Find("Sprites/Default"));
            fb.color = new Color(1f, 0.90f, 0.25f, 0.22f);
            return fb;
        }
    }
}
