using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using IslandBuilder.Domain;
using IslandBuilder.Domain.Tools;
using IslandBuilder.Rendering;

namespace IslandBuilder.Interaction
{
    /// <summary>
    /// Raycasts from the camera to the terrain each frame and routes mouse events to
    /// the active ITool. Also handles Ctrl+Z / Ctrl+Y for undo/redo.
    ///
    /// Must live on the same GameObject as the Camera component.
    /// </summary>
    [AddComponentMenu("Island Builder/Sculpt Controller")]
    public class SculptController : MonoBehaviour
    {
        [SerializeField] private TerrainManager _terrainManager;
        [SerializeField] private ToolRegistry   _toolRegistry;
        [SerializeField] private UndoManager    _undoManager;
        [SerializeField] private BrushPreview   _brushPreview;

        private Camera _camera;
        private bool   _strokeStarted;
        private IslandBuilder.Domain.Tools.GlobalToolSettings _globalSettings;

        // ── Handle-drag state ─────────────────────────────────────────────────
        private LassoTool           _lassoForHandles;
        private MeasureTool         _measureForHandles;
        private LassoRenderer         _lassoRenderer;
        private MeasurementRenderer   _measureRenderer;
        private BeachToolAlt          _beachAlt;
        private BeachAltRenderer      _beachAltRenderer;
        private bool  _draggingHandle;
        private int   _dragHandleIdx;
        private int   _dragHandleOwner; // 0=lasso,1=area,2=beachInner,3=beachOuter
        private const float HandlePickPixels = 22f;

        public void BindGlobalSettings(IslandBuilder.Domain.Tools.GlobalToolSettings gs)
            => _globalSettings = gs;

        public void BindHandleEditing(LassoTool lt, LassoRenderer lr,
                                      MeasureTool mt, MeasurementRenderer mr)
        {
            _lassoForHandles   = lt;
            _lassoRenderer     = lr;
            _measureForHandles = mt;
            _measureRenderer   = mr;
        }

        public void BindBeachAltHandles(BeachToolAlt bat, BeachAltRenderer bar)
        {
            _beachAlt         = bat;
            _beachAltRenderer = bar;
        }

        private void Start() => _camera = GetComponent<Camera>();

        private void Update()
        {
            var mouse    = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            // ── Undo / Redo ───────────────────────────────────────────────────
            bool ctrl  = keyboard.ctrlKey.isPressed;
            bool shift = keyboard.shiftKey.isPressed;
            if (ctrl && keyboard.zKey.wasPressedThisFrame)
            {
                if (shift) _undoManager?.Redo(_terrainManager);  // Ctrl+Shift+Z → redo
                else       _undoManager?.Undo(_terrainManager);  // Ctrl+Z       → undo
                return;
            }
            if (ctrl && keyboard.yKey.wasPressedThisFrame)
            {
                _undoManager?.Redo(_terrainManager);             // Ctrl+Y       → redo
                return;
            }

            // ── Block interaction when cursor is over the sidebar or top bar ─
            // UI Toolkit reference 1920×1080; sidebar=180 wide, top bar=44 tall.
            // Input System mouse Y=0 is at the screen bottom, so top bar = large Y.
            var mousePos = mouse.position.ReadValue();
            float wScale = Screen.width  / 1920f;
            float hScale = Screen.height / 1080f;
            if (mousePos.x < 180f * wScale ||
                mousePos.y > Screen.height - 44f * hScale ||
                mousePos.x > Screen.width  - 280f * hScale)
                return;

            // ── Raycast — pierces water and other non-terrain objects ─────────
            if (_camera == null) return;
            Ray ray = _camera.ScreenPointToRay(mousePos);
            bool hit = RaycastTerrain(ray, out RaycastHit hitInfo);

            // AlwaysUpdate tools (e.g. Lasso) may draw outside the terrain footprint;
            // let them also hit the terrain-extension mesh.
            if (!hit && _toolRegistry?.ActiveTool?.AlwaysUpdate == true)
                hit = RaycastExtension(ray, out hitInfo);

            // ── Global: sample Edit Above Height — checked before any early return ──
            if (mouse.leftButton.wasPressedThisFrame && _globalSettings?.IsSamplingEditAbove == true)
            {
                float sampleY;
                if (hit)
                {
                    sampleY = hitInfo.point.y;
                }
                else if (Physics.Raycast(ray, out RaycastHit anyHit, _camera.farClipPlane))
                {
                    sampleY = anyHit.point.y;
                }
                else
                {
                    return; // nothing hit — keep sampling mode active for next click
                }
                _globalSettings.EditAboveHeight     = sampleY;
                _globalSettings.EditAboveEnabled    = true;
                _globalSettings.IsSamplingEditAbove = false;
                return;
            }

            // ── Handle visibility ─────────────────────────────────────────────
            var activeTool = _toolRegistry?.ActiveTool;
            bool isLassoMode   = activeTool?.ToolId == "lasso";
            bool isAreaMode    = activeTool?.ToolId == "measure" &&
                                 _measureForHandles?.Mode == MeasureMode.Area;
            _lassoRenderer?.SetShowHandles(isLassoMode);
            _measureRenderer?.SetShowAreaHandles(isAreaMode);

            // ── Handle drag — intercepts before normal tool routing ───────────
            if (_draggingHandle)
            {
                if (mouse.leftButton.isPressed && hit)
                {
                    switch (_dragHandleOwner)
                    {
                        case 0: _lassoForHandles?.MovePoint(_dragHandleIdx, hitInfo.point);        break;
                        case 1: _measureForHandles?.MoveAreaPoint(_dragHandleIdx, hitInfo.point);  break;
                        case 2: _beachAlt?.InnerCurve.MoveAnchor(_dragHandleIdx, hitInfo.point);  break;
                        case 3: _beachAlt?.OuterCurve.MoveAnchor(_dragHandleIdx, hitInfo.point);  break;
                    }
                }
                else { _draggingHandle = false; }
                return;
            }

            if (!hit)
            {
                _brushPreview?.Hide();
                if (_strokeStarted)
                {
                    _toolRegistry?.ActiveTool?.OnMouseUp();
                    _strokeStarted = false;
                }
                return;
            }

            // ── Handle pick — on click, check if cursor is on a handle ────────
            bool isBeachAltMode = activeTool?.ToolId == "beachalt" &&
                (_beachAlt?.Phase == BeachAltPhase.InnerReady ||
                 _beachAlt?.Phase == BeachAltPhase.BothReady);

            if (mouse.leftButton.wasPressedThisFrame && !keyboard.ctrlKey.isPressed
                && (isLassoMode || isAreaMode || isBeachAltMode))
            {
                if (TryPickHandle(mousePos, isLassoMode, out int hIdx, out bool hIsLasso))
                {
                    // For lasso handles, translate visual handle index → polygon index.
                    int polyIdx = hIsLasso
                        ? (_lassoRenderer?.GetPolygonIndex(hIdx) ?? hIdx)
                        : hIdx;
                    if (polyIdx >= 0)
                    {
                        _draggingHandle  = true;
                        _dragHandleIdx   = polyIdx;
                        _dragHandleOwner = hIsLasso ? 0 : 1;
                        return;
                    }
                }
            }

            var tool = _toolRegistry?.ActiveTool;
            if (tool == null || tool.ToolId == "camera" || tool.ToolId == "grid")
                { _brushPreview?.Hide(); return; }

            // Beach (classic) with lasso: yield to camera.
            if (tool is IslandBuilder.Domain.Tools.BeachTool bt2 && bt2.HasLasso)
                { _brushPreview?.Hide(); return; }
            // Beach alt BothReady: both curves drawn, yield to camera for navigation.
            if (tool is IslandBuilder.Domain.Tools.BeachToolAlt ba2 &&
                ba2.Phase == IslandBuilder.Domain.Tools.BeachAltPhase.BothReady)
                { _brushPreview?.Hide(); return; }

            // Push current camera yaw to any shape-aware tool so shapes face the viewer.
            float yaw = _camera != null ? _camera.transform.eulerAngles.y : 0f;
            if (tool is RaiseTool rt2) rt2.CameraYawDegrees = yaw;
            if (tool is EraseTool et) et.CameraYawDegrees  = yaw;

            if (tool is RaiseTool rt && rt.Mode == RaiseMode.Shape)
                _brushPreview?.Show(hitInfo.point, tool.BrushRadius, (int)rt.Shape, rt.StarPoints, yaw);
            else if (tool is EraseTool ert && ert.Shape != BrushShape.Circle)
                _brushPreview?.Show(hitInfo.point, tool.BrushRadius, (int)ert.Shape, ert.StarPoints, yaw);
            else
                _brushPreview?.Show(hitInfo.point, tool.BrushRadius);

            // ── Always-update tools — receive full click events, not just held ──────
            if (tool.AlwaysUpdate)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    tool.OnMouseDown(hitInfo);
                else if (mouse.leftButton.wasReleasedThisFrame)
                    tool.OnMouseUp();
                else
                    tool.OnMouseHeld(hitInfo); // covers both hover and drag frames

                // Right-click inside the lasso clears the selection.
                if (mouse.rightButton.wasPressedThisFrame &&
                    tool is LassoTool lt && lt.HasSelection &&
                    lt.IsInsideLasso(hitInfo.point.x, hitInfo.point.z))
                    lt.ClearSelection();
                return;
            }

// ── Ctrl+LeftClick: sample target height for active tool ──────────
            if (ctrl && mouse.leftButton.wasPressedThisFrame)
            {
                tool.OnMouseDown(hitInfo);
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _strokeStarted = true;
                if (_undoManager != null && _terrainManager != null && tool.BrushRadius > 0f)
                {
                    // Snapshot the full terrain so any drag path is fully covered.
                    int res      = _terrainManager.Resolution;
                    float[,] pre = _terrainManager.GetHeights(new RectInt(0, 0, res, res));
                    _undoManager.Push(new UndoEntry(pre, Vector2Int.zero, tool.ToolId));
                }
                tool.OnMouseHeld(hitInfo);
            }
            else if (mouse.leftButton.isPressed && _strokeStarted)
            {
                tool.OnMouseHeld(hitInfo);
            }
            else if (mouse.leftButton.wasReleasedThisFrame && _strokeStarted)
            {
                tool.OnMouseUp();
                _strokeStarted = false;
            }
            else if (!mouse.leftButton.isPressed)
            {
                _strokeStarted = false;
            }
        }

        /// <summary>
        /// Casts through all colliders and returns the closest TerrainCollider hit,
        /// allowing interaction through water planes or other overlaid meshes.
        /// </summary>
        private bool RaycastExtension(Ray ray, out RaycastHit result)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, _camera.farClipPlane);
            result = default;
            float best = float.MaxValue;
            bool  found = false;
            foreach (var h in hits)
            {
                if (h.distance >= best) continue;
                if (h.collider is MeshCollider mc &&
                    mc.GetComponent<TerrainExtensionCollider>() != null)
                {
                    best   = h.distance;
                    result = h;
                    found  = true;
                }
            }
            return found;
        }

        // Returns true + fills (idx, owner) where owner: 0=lasso,1=area,2=beachInner,3=beachOuter
        private bool TryPickHandle(Vector2 screenPos, bool lassoMode,
                                   out int idx, out bool isLasso)
        {
            idx     = -1;
            isLasso = false;
            if (_camera == null) return false;

            float best = HandlePickPixels * HandlePickPixels;
            int   bestOwner = 0;

            if (lassoMode && _lassoForHandles?.HasSelection == true && _lassoRenderer != null)
            {
                int hCount = _lassoRenderer.HandleCount;
                for (int hi = 0; hi < hCount; hi++)
                {
                    int pi = _lassoRenderer.GetPolygonIndex(hi);
                    if (pi < 0) continue;
                    Vector3 sp = _camera.WorldToScreenPoint(_lassoForHandles.Polygon[pi]);
                    if (sp.z <= 0f) continue;
                    float d2 = (sp.x-screenPos.x)*(sp.x-screenPos.x) +
                               (sp.y-screenPos.y)*(sp.y-screenPos.y);
                    if (d2 < best) { best = d2; idx = hi; bestOwner = 0; isLasso = true; }
                }
            }
            else if (!lassoMode && _measureForHandles?.AreaPolygon.Count >= 3)
            {
                var poly = _measureForHandles.AreaPolygon;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector3 sp = _camera.WorldToScreenPoint(poly[i]);
                    if (sp.z <= 0f) continue;
                    float d2 = (sp.x-screenPos.x)*(sp.x-screenPos.x) +
                               (sp.y-screenPos.y)*(sp.y-screenPos.y);
                    if (d2 < best) { best = d2; idx = i; bestOwner = 1; isLasso = false; }
                }
            }

            // Beach alt curve anchors (always check both inner + outer when beachalt active).
            if (_beachAlt != null &&
                (_beachAlt.Phase == BeachAltPhase.InnerReady ||
                 _beachAlt.Phase == BeachAltPhase.BothReady))
            {
                PickCurveAnchors(_beachAlt.InnerCurve.Anchors, 2, screenPos, ref best, ref idx, ref bestOwner);
                if (_beachAlt.Phase == BeachAltPhase.BothReady)
                    PickCurveAnchors(_beachAlt.OuterCurve.Anchors, 3, screenPos, ref best, ref idx, ref bestOwner);
            }

            if (idx >= 0) _dragHandleOwner = bestOwner;
            return idx >= 0;
        }

        private void PickCurveAnchors(IReadOnlyList<Vector3> anchors, int owner,
                                       Vector2 screenPos, ref float best,
                                       ref int idx, ref int bestOwner)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 sp = _camera.WorldToScreenPoint(anchors[i]);
                if (sp.z <= 0f) continue;
                float d2 = (sp.x-screenPos.x)*(sp.x-screenPos.x) +
                           (sp.y-screenPos.y)*(sp.y-screenPos.y);
                if (d2 < best) { best = d2; idx = i; bestOwner = owner; }
            }
        }

        private bool RaycastTerrain(Ray ray, out RaycastHit terrainHit)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, _camera.farClipPlane);
            terrainHit = default;
            float best = float.MaxValue;
            bool  found = false;
            foreach (var h in hits)
            {
                if (h.collider is TerrainCollider && h.distance < best)
                {
                    best       = h.distance;
                    terrainHit = h;
                    found      = true;
                }
            }
            return found;
        }

        private RectInt HeightmapRect(Vector3 worldPos, float radiusMetres)
        {
            if (_terrainManager == null || _terrainManager.Resolution <= 1)
                return new RectInt(0, 0, 0, 0);
            float cw = _terrainManager.CellWidth;
            float cl = _terrainManager.CellLength;
            int   cx = Mathf.RoundToInt(worldPos.x / cw);
            int   cz = Mathf.RoundToInt(worldPos.z / cl);
            int   r  = Mathf.CeilToInt(radiusMetres / Mathf.Min(cw, cl));
            int res  = _terrainManager.Resolution;
            int x0 = Mathf.Clamp(cx - r, 0, res - 1);
            int z0 = Mathf.Clamp(cz - r, 0, res - 1);
            int x1 = Mathf.Clamp(cx + r, 0, res - 1);
            int z1 = Mathf.Clamp(cz + r, 0, res - 1);
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }
    }
}
