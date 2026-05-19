using UnityEngine;
using UnityEngine.InputSystem;
using IslandBuilder.Domain;
using IslandBuilder.Infrastructure;

namespace IslandBuilder.Interaction
{
    /// <summary>
    /// Orbit/pan/zoom camera for terrain inspection.
    ///
    /// Controls (new Input System):
    ///   Left drag   – orbit (yaw + pitch around pivot)
    ///   Middle drag – pan  (translate pivot in the horizontal plane)
    ///   Scroll      – zoom (change distance to pivot)
    ///
    /// Subscribes to ImportManager.ImportCompleted to auto-frame the terrain (F-06).
    /// </summary>
    [AddComponentMenu("Island Builder/Camera Controller")]
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private ImportManager _importManager;
        [SerializeField] private ToolRegistry  _toolRegistry;

        [Header("Sensitivity")]
        [SerializeField] private float _orbitSensitivity = 0.25f;
        [SerializeField] private float _panSensitivity   = 0.0015f;
        [SerializeField] private float _zoomSensitivity  = 0.10f;

        // Orbit state — default frames the 1000 m placeholder terrain.
        // yaw=180 puts the camera south of the terrain looking north (north-up convention).
        private Vector3 _pivot    = new Vector3(500f, 0f, 500f);
        private float   _distance = 1200f;
        private float   _yaw      = 180f;
        private float   _pitch    = 45f;

        private Camera _camera;

        public void BindToolRegistry(ToolRegistry toolRegistry) => _toolRegistry = toolRegistry;

        public void BindImportManager(ImportManager im)
        {
            if (_importManager != null) _importManager.ImportCompleted -= OnImportCompleted;
            _importManager = im;
            if (_importManager != null) _importManager.ImportCompleted += OnImportCompleted;
        }

        private void OnEnable()
        {
            if (_importManager != null)
                _importManager.ImportCompleted += OnImportCompleted;
        }

        private void OnDisable()
        {
            if (_importManager != null)
                _importManager.ImportCompleted -= OnImportCompleted;
        }

        private void Start()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null)
            {
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane  = 10000f;
            }
            UpdatePosition();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Block camera movement when the cursor is over a UI panel.
            // Panels (logical px at 1920×1080 reference):
            //   Left sidebar  : x < 180
            //   Top bar       : screen-top 36 px (y > Screen.height - 36*hScale)
            //   Right panels  : x > Screen.width - 280*hScale
            var  mp     = mouse.position.ReadValue();
            float hScale = Screen.height / 1080f;
            if (mp.x < 180f * (Screen.width / 1920f) ||
                mp.y > Screen.height - 36f * hScale ||
                mp.x > Screen.width  - 280f * hScale)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            bool moved    = false;

            var  activeId  = _toolRegistry?.ActiveTool?.ToolId;
            bool beachLasso = (activeId == "beach" &&
                               (_toolRegistry.ActiveTool as IslandBuilder.Domain.Tools.BeachTool)?.HasLasso == true)
                           || (activeId == "beachalt" &&
                               (_toolRegistry.ActiveTool as IslandBuilder.Domain.Tools.BeachToolAlt)?.Phase ==
                                   IslandBuilder.Domain.Tools.BeachAltPhase.BothReady);
            bool camMode   = activeId == "camera" || activeId == "grid" || beachLasso;
            bool orbitBtn  = camMode ? mouse.leftButton.isPressed  : mouse.rightButton.isPressed;
            bool panBtn    = camMode ? mouse.rightButton.isPressed : mouse.middleButton.isPressed;

            // ── Orbit ─────────────────────────────────────────────────────────
            if (orbitBtn && delta.sqrMagnitude > 0f)
            {
                _yaw   += delta.x * _orbitSensitivity;
                _pitch -= delta.y * _orbitSensitivity;
                _pitch  = Mathf.Clamp(_pitch, 5f, 89f);
                moved   = true;
            }

            // ── Pan ───────────────────────────────────────────────────────────
            if (panBtn && delta.sqrMagnitude > 0f)
            {
                // Pan in the camera's horizontal plane so terrain "sticks" to the cursor.
                Vector3 right          = transform.right;
                Vector3 forwardOnPlane = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (forwardOnPlane.sqrMagnitude < 0.001f) forwardOnPlane = Vector3.forward;
                forwardOnPlane.Normalize();

                float scale = _distance * _panSensitivity;
                _pivot -= right          * (delta.x * scale);
                _pivot += forwardOnPlane * (delta.y * scale);
                moved   = true;
            }

            // ── Zoom ──────────────────────────────────────────────────────────
            // scroll.y on Windows is typically ±120 per notch; Mathf.Sign normalises it.
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                _distance *= 1f - Mathf.Sign(scrollY) * _zoomSensitivity;
                float farLimit = _camera != null ? _camera.farClipPlane * 0.9f : 50000f;
                _distance  = Mathf.Clamp(_distance, 0.5f, farLimit);
                moved = true;
            }

            if (moved) UpdatePosition();
        }

        // ── ImportCompleted handler ───────────────────────────────────────────

        private void OnImportCompleted(TerrainData terrainData, float displaySeaLevel)
        {
            Vector3 size  = terrainData.size;
            float   span  = Mathf.Max(size.x, size.z);

            // Pivot at sea level so the camera orbits around the water surface,
            // not the ocean floor (which is at Y=0 and buried under the water).
            _pivot    = new Vector3(size.x * 0.5f, displaySeaLevel, size.z * 0.5f);
            _distance = span * 1.2f;
            _yaw      = 180f;   // camera south of terrain, looking north (north-up)
            _pitch    = 45f;

            // Scale clip planes to the terrain so nothing is clipped at any size.
            if (_camera != null)
            {
                _camera.nearClipPlane = Mathf.Max(0.1f, span * 0.00001f);
                _camera.farClipPlane  = span * 4f;
            }

            // Allow zooming out to 4× the span.
            _distance = Mathf.Clamp(_distance, 0.5f, span * 4f);

            UpdatePosition();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdatePosition()
        {
            float yRad = _yaw   * Mathf.Deg2Rad;
            float pRad = _pitch * Mathf.Deg2Rad;

            var offset = new Vector3(
                Mathf.Cos(pRad) * Mathf.Sin(yRad),
                Mathf.Sin(pRad),
                Mathf.Cos(pRad) * Mathf.Cos(yRad)
            ) * _distance;

            transform.position = _pivot + offset;
            transform.LookAt(_pivot, Vector3.up);
        }
    }
}
