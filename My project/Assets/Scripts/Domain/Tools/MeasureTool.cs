using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace IslandBuilder.Domain.Tools
{
    public enum MeasureMode { Distance, Area }

    public readonly struct MeasureLine
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;
        /// <summary>Horizontal (XZ) distance in display metres.</summary>
        public readonly float Metres;

        public MeasureLine(Vector3 s, Vector3 e)
        {
            Start  = s;
            End    = e;
            float dx = e.x - s.x, dz = e.z - s.z;
            Metres = Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Hover to read elevation. Click and drag to draw a distance line (Distance mode)
    /// or trace a closed polygon and read its planimetric area (Area mode).
    /// </summary>
    public class MeasureTool : ITool
    {
        // ── Colour palette ────────────────────────────────────────────────────
        public static readonly Color[] Palette =
        {
            new Color(1.00f, 0.30f, 0.30f),
            new Color(0.30f, 1.00f, 0.40f),
            new Color(0.35f, 0.65f, 1.00f),
            new Color(1.00f, 0.88f, 0.20f),
            new Color(1.00f, 0.55f, 0.10f),
            new Color(0.75f, 0.30f, 1.00f),
            new Color(0.20f, 0.95f, 1.00f),
            new Color(1.00f, 0.30f, 0.80f),
        };

        private readonly ITerrainReader    _reader;
        private readonly Action<string>    _display;
        private readonly List<MeasureLine> _lines      = new();
        private readonly List<Vector3>     _areaPoly   = new();

        private bool    _dragging;
        private Vector3 _dragStart;
        private Vector3 _dragEnd;
        private Vector3 _lastAreaPoint;

        // ── ITool ─────────────────────────────────────────────────────────────
        public string ToolId       => "measure";
        public float  BrushRadius  => 0f;
        public bool   AlwaysUpdate => true;

        public MeasureMode Mode { get; set; } = MeasureMode.Distance;

        // ── Live readouts for ToolParameterGUI ────────────────────────────────
        public bool  HasHover        { get; private set; }
        public float HoverHeight     { get; private set; }
        public float HoverDeltaSea   { get; private set; }
        public float ActiveLineMetres => _dragging && Mode == MeasureMode.Distance
            ? new MeasureLine(_dragStart, _dragEnd).Metres : 0f;

        // ── Area mode ─────────────────────────────────────────────────────────
        public bool  HasArea           { get; private set; }
        public float AreaSqMetres      { get; private set; }
        public IReadOnlyList<Vector3> AreaPolygon => _areaPoly;

        // ── Distance mode ─────────────────────────────────────────────────────
        public IReadOnlyList<MeasureLine> Lines       => _lines;
        public bool    IsDragging  => _dragging && Mode == MeasureMode.Distance;
        public Vector3 ActiveStart => _dragStart;
        public Vector3 ActiveEnd   => _dragEnd;
        public Color   ActiveColor => Palette[_lines.Count % Palette.Length];

        public event Action LinesChanged;

        public MeasureTool(ITerrainReader reader, Action<string> displayCallback)
        {
            _reader  = reader;
            _display = displayCallback;
        }

        public void OnActivate()   { }

        public void OnDeactivate()
        {
            _dragging   = false;
            HasHover    = false;
            _display?.Invoke(string.Empty);
        }

        public void OnMouseDown(RaycastHit hit)
        {
            _dragging      = true;
            _dragStart     = hit.point;
            _dragEnd       = hit.point;
            _lastAreaPoint = hit.point;

            if (Mode == MeasureMode.Area)
            {
                _areaPoly.Clear();
                _areaPoly.Add(hit.point);
                HasArea = false;
            }
        }

        public void OnMouseHeld(RaycastHit hit)
        {
            _dragEnd  = hit.point;
            HasHover  = true;
            HoverHeight   = hit.point.y;
            HoverDeltaSea = hit.point.y - _reader.SeaLevelOffset;

            if (_dragging)
            {
                if (Mode == MeasureMode.Distance)
                {
                    float dx = hit.point.x - _dragStart.x, dz = hit.point.z - _dragStart.z;
                    _display?.Invoke($"Measuring: {Mathf.Sqrt(dx*dx+dz*dz):F1} m");
                }
                else
                {
                    // Accumulate area polygon points when mouse moves enough.
                    float ddx = hit.point.x - _lastAreaPoint.x;
                    float ddz = hit.point.z - _lastAreaPoint.z;
                    if (ddx*ddx + ddz*ddz > 1f)
                    {
                        _areaPoly.Add(hit.point);
                        _lastAreaPoint = hit.point;
                    }
                    float a = PolygonArea(_areaPoly);
                    _display?.Invoke($"Area: {a:F0} m²");
                }
            }
            else
            {
                _display?.Invoke(
                    $"Height: {hit.point.y:F1} m  |  Δ sea: {HoverDeltaSea:+0.#;-0.#;0} m");
            }
        }

        public void OnMouseUp()
        {
            if (!_dragging) return;
            _dragging = false;

            if (Mode == MeasureMode.Distance)
            {
                var line = new MeasureLine(_dragStart, _dragEnd);
                if (line.Metres > 0.5f)
                {
                    _lines.Add(line);
                    LinesChanged?.Invoke();
                }
            }
            else
            {
                if (_areaPoly.Count >= 3)
                {
                    AreaSqMetres = PolygonArea(_areaPoly);
                    HasArea      = true;
                }
            }

            _display?.Invoke(string.Empty);
        }

        public void ClearLines()
        {
            _lines.Clear();
            _dragging = false;
            LinesChanged?.Invoke();
        }

        /// <summary>Moves a single area-polygon vertex without event (handle drag).</summary>
        public void MoveAreaPoint(int index, Vector3 newPos)
        {
            if (index < 0 || index >= _areaPoly.Count) return;
            _areaPoly[index] = newPos;
            if (_areaPoly.Count >= 3) { AreaSqMetres = PolygonArea(_areaPoly); HasArea = true; }
        }

        public void ClearArea()
        {
            _areaPoly.Clear();
            HasArea = false;
        }

        public void SetLines(IEnumerable<MeasureLine> lines)
        {
            _lines.Clear();
            foreach (var l in lines)
                if (l.Metres > 0.5f) _lines.Add(l);
            LinesChanged?.Invoke();
        }

        public VisualElement GetParameterPanel() => null;

        // Shoelace formula — planimetric (XZ) area in m².
        private static float PolygonArea(List<Vector3> pts)
        {
            int n = pts.Count;
            if (n < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].x * pts[j].z - pts[j].x * pts[i].z;
            }
            return Mathf.Abs(area) * 0.5f;
        }
    }
}
