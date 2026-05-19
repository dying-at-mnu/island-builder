using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;

namespace IslandBuilder.Rendering
{
    /// <summary>
    /// Manages one LineRenderer per completed MeasureTool line, plus an active-
    /// preview renderer while the user is dragging. Completed lines persist even
    /// when other tools are active.
    /// </summary>
    [AddComponentMenu("Island Builder/Measurement Renderer")]
    public class MeasurementRenderer : MonoBehaviour
    {
        private MeasureTool           _tool;
        private ITerrainReader        _reader;
        private Material              _mat;

        private readonly List<LineRenderer> _completed    = new();
        private readonly List<LineRenderer> _areaHandles  = new();
        private LineRenderer                _active;
        private LineRenderer                _areaPoly;
        private bool                        _showAreaHandles;

        private const float AreaHandleRadius = 3f;
        private const int   AreaHandleSegs   = 10;

        public void SetShowAreaHandles(bool show) => _showAreaHandles = show;

        public void Bind(MeasureTool tool, ITerrainReader reader)
        {
            _tool   = tool;
            _reader = reader;
            _mat    = BuildMaterial();

            _active = CreateLR(Color.white);
            _active.enabled = false;

            // Area polygon preview — reuses the active colour, closed loop.
            _areaPoly = CreateLR(new Color(0.20f, 0.95f, 1.00f));
            _areaPoly.loop           = true;
            _areaPoly.positionCount  = 0;
            _areaPoly.enabled        = false;

            tool.LinesChanged += RebuildCompleted;
        }

        private void Update()
        {
            if (_tool == null || _active == null) return;

            float yOff = YOffset();

            // Distance mode — two-point active line.
            bool distDragging = _tool.IsDragging;
            _active.enabled   = distDragging;
            if (distDragging)
            {
                _active.SetPosition(0, _tool.ActiveStart + Vector3.up * yOff);
                _active.SetPosition(1, _tool.ActiveEnd   + Vector3.up * yOff);
                Recolour(_active, _tool.ActiveColor);
            }

            // Area mode — polygon outline.
            var poly = _tool.AreaPolygon;
            bool showPoly = _tool.Mode == MeasureMode.Area && poly.Count >= 2;
            _areaPoly.enabled = showPoly;
            if (showPoly)
            {
                _areaPoly.positionCount = poly.Count;
                for (int i = 0; i < poly.Count; i++)
                    _areaPoly.SetPosition(i, poly[i] + Vector3.up * yOff);
            }

            // Area handles — yellow circles at each vertex, visible in measure+area mode.
            bool showHandles = _showAreaHandles && poly.Count >= 3;
            while (_areaHandles.Count > poly.Count) RemoveLastAreaHandle();
            while (_areaHandles.Count < poly.Count) _areaHandles.Add(CreateHandleLR());
            for (int i = 0; i < _areaHandles.Count; i++)
            {
                _areaHandles[i].enabled = showHandles;
                if (showHandles) PlaceCircle(_areaHandles[i], poly[i] + Vector3.up * yOff);
            }
        }

        private void PlaceCircle(LineRenderer lr, Vector3 centre)
        {
            lr.positionCount = AreaHandleSegs;
            for (int i = 0; i < AreaHandleSegs; i++)
            {
                float a = (float)i / AreaHandleSegs * Mathf.PI * 2f;
                lr.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * AreaHandleRadius, 0f,
                                                        Mathf.Sin(a) * AreaHandleRadius));
            }
        }

        private LineRenderer CreateHandleLR()
        {
            var go = new GameObject("AH");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = true;
            lr.widthMultiplier   = 2.5f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = _mat;
            lr.startColor        = lr.endColor = new Color(1f, 1f, 0.3f, 0.95f);
            lr.positionCount     = AreaHandleSegs;
            lr.enabled           = false;
            return lr;
        }

        private void RemoveLastAreaHandle()
        {
            int last = _areaHandles.Count - 1;
            if (_areaHandles[last] != null) Destroy(_areaHandles[last].gameObject);
            _areaHandles.RemoveAt(last);
        }

        private void RebuildCompleted()
        {
            var lines = _tool.Lines;

            // Remove extra renderers if lines were cleared.
            while (_completed.Count > lines.Count)
            {
                int last = _completed.Count - 1;
                Destroy(_completed[last].gameObject);
                _completed.RemoveAt(last);
            }

            // Add new renderers for new lines.
            while (_completed.Count < lines.Count)
            {
                int   idx   = _completed.Count;
                Color color = MeasureTool.Palette[idx % MeasureTool.Palette.Length];
                _completed.Add(CreateLR(color));
            }

            // Sync positions and colours.
            float yOff = YOffset();
            for (int i = 0; i < lines.Count; i++)
            {
                var lr   = _completed[i];
                var line = lines[i];
                lr.SetPosition(0, line.Start + Vector3.up * yOff);
                lr.SetPosition(1, line.End   + Vector3.up * yOff);
                Recolour(lr, MeasureTool.Palette[i % MeasureTool.Palette.Length]);
                lr.enabled = true;
            }
        }

        private float YOffset() =>
            _reader != null ? Mathf.Max(1f, _reader.WorldSize.y * 0.002f) : 1f;

        private LineRenderer CreateLR(Color color)
        {
            var go = new GameObject("MeasureLine");
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.positionCount     = 2;
            lr.widthMultiplier   = 3f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.material          = _mat;
            Recolour(lr, color);
            lr.enabled = false;
            return lr;
        }

        private static void Recolour(LineRenderer lr, Color c)
        {
            lr.startColor = c;
            lr.endColor   = c;
        }

        // Reuse GridRenderer's always-on-top material builder so both line types
        // share the same ZTest=Always / Overlay setup.
        private static Material BuildMaterial() => GridRenderer.BuildAlwaysOnTopMat();

        private void OnDestroy()
        {
            while (_areaHandles.Count > 0) RemoveLastAreaHandle();
            if (_mat != null) Destroy(_mat);
        }
    }
}
