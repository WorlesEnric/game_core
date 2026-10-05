// GameCore.Studio.UI - the Studio Viewport (GameCore/Studio/Viewport; SR-1.1/1.2/1.5, SR-8.2, W-UI-01..04, B-SELECT,
// B-FRAME): a dockable EditorWindow whose UI Toolkit overlay shows the game camera rendered into a RenderTexture at window
// size (correct aspect), with three modes:
//   Play    - keyboard/mouse go to the game while the window is focused (PlayInputRouting), Esc/Tab leave it;
//   Select  - hover highlight, click select, shift-add, ctrl/cmd-toggle, marquee (partial/full containment), an overlap
//             list when two or more objects lie under the cursor (samples within 4 px, depth order, occlusion flags),
//             right-click point-at (ground/NavMesh) with a marker, the move gizmo (one change set per drag), F frames;
//   Inspect - hover shows the authoring card (type id, name, authoring id, definition@revision, stale badge).
// Tab cycles the modes, Esc clears the selection. Pause/Resume/Step drive the application root; the pump indicator
// shows sanctioned pumps per frame (red when it is not exactly one in Play) and the frame time. Selection badges show
// residency (unloaded region: disabled, with the reason) and staleness. The prompt bar is docked at the bottom and the
// candidate strip sits bottom-right. The viewport never pumps the world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace GameCore.Studio.UI
{
    /// <summary>The Studio Viewport window.</summary>
    public sealed class StudioViewportWindow : EditorWindow
    {
        /// <summary>Pointer travel (px) below which a press is a click, above which a drag (marquee, look).</summary>
        public const float DragThreshold = 4f;

        [SerializeField]
        private ViewportMode mode = ViewportMode.Select;

        [SerializeField]
        private bool fullContainment;

        [SerializeField]
        private bool forceFreeCamera;

        [SerializeField]
        private bool freePoseSaved;

        [SerializeField]
        private Vector3 freePosition;

        [SerializeField]
        private Quaternion freeRotation = Quaternion.identity;

        private readonly ViewportRenderer _renderer = new ViewportRenderer();
        private readonly PumpMonitor _pump = new PumpMonitor();
        private readonly PlayInputRouting _routing = new PlayInputRouting();
        private readonly SelectTimings _timings = new SelectTimings();
        private readonly HashSet<KeyCode> _keys = new HashSet<KeyCode>();

        private StudioUiContext? _context;
        private PickingService? _picking;
        private ViewportPicker? _picker;
        private VisualElement? _area;
        private Image? _image;
        private VisualElement? _hoverBox;
        private VisualElement? _hoverCard;
        private VisualElement? _marqueeBox;
        private VisualElement? _marker;
        private Label? _markerLabel;
        private VisualElement? _badges;
        private Label? _pumpLabel;
        private Label? _cameraLabel;
        private Label? _statusLabel;
        private Label? _appStateLabel;
        private Button? _playButton;
        private Button? _selectButton;
        private Button? _inspectButton;
        private Button? _pauseButton;
        private Button? _resumeButton;
        private Button? _stepButton;
        private ViewportMoveGizmo? _gizmo;
        private OverlapPopup? _overlap;
        private CandidateStripView? _strip;
        private PromptBar? _prompt;

        private Vector2 _pointer;
        private bool _hoverDirty;
        private int _pressButton = -1;
        private Vector2 _pressPosition;
        private bool _dragging;
        private bool _looking;
        private SelectionOp _pressOp;
        private PickCandidate? _hover;
        private Vector3? _markerWorld;
        private double _lastRender;
        private double _lastBadges;
        private double _lastTick;
        private bool _renderDirty = true;
        private int _stepTarget = -1;
        private bool _uiBuilt;
        private bool _toolActivated;
        private long _badgeVersion = -1;

        [MenuItem(StudioWindowIds.ViewportMenu, false, 101)]
        public static void OpenMenu() => Open();

        /// <summary>Opens (or focuses) the viewport.</summary>
        public static StudioViewportWindow Open()
        {
            StudioViewportWindow window = GetWindow<StudioViewportWindow>(StudioWindowIds.ViewportTitle);
            window.minSize = new Vector2(480f, 320f);
            window.Show();
            return window;
        }

        public ViewportMode Mode => mode;

        /// <summary>Marquee requires full containment (toolbar toggle).</summary>
        public bool FullContainment
        {
            get => fullContainment;
            set => fullContainment = value;
        }

        public StudioUiContext Context => _context ??= StudioUiSession.Context;

        public ViewportRenderer Renderer => _renderer;

        public PumpMonitor Pump => _pump;

        public PlayInputRouting Routing => _routing;

        public SelectTimings Timings => _timings;

        public PromptBar? Prompt => _prompt;

        public OverlapPopup? Overlap => _overlap;

        public ViewportMoveGizmo? Gizmo => _gizmo;

        public CandidateStripView? Strip => _strip;

        /// <summary>The viewport image rectangle in window points (picking coordinates).</summary>
        public Rect ImageRect => _area == null ? Rect.zero : new Rect(0f, 0f, _area.contentRect.width, _area.contentRect.height);

        /// <summary>The render target (null before the first layout or without graphics).</summary>
        public RenderTexture? Texture => _renderer.Target;

        /// <summary>The candidate under the pointer (Select/Inspect hover).</summary>
        public PickCandidate? Hovered => _hover;

        /// <summary>World position of the point-at marker, when set.</summary>
        public Vector3? MarkerWorld => _markerWorld;

        /// <summary>Uses another UI context (tests); rebuilds the UI.</summary>
        public void UseContext(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _picking = null;
            _picker = null;
            if (_uiBuilt)
            {
                rootVisualElement.Clear();
                _uiBuilt = false;
                CreateGUI();
            }
        }

        /// <summary>Switches the mode (Play only while a world runs in Play mode).</summary>
        public void SetMode(ViewportMode next)
        {
            if (next == ViewportMode.Play && !EditorApplication.isPlaying)
            {
                next = ViewportMode.Select;
            }

            mode = next;
            _hover = null;
            _overlap?.Hide();
            if (_gizmo != null && _gizmo.Dragging)
            {
                _gizmo.Cancel();
            }

            UpdateMoveTool();
            RefreshToolbar();
        }

        /// <summary>Play -> Select -> Inspect -> (Play when playing, else Select).</summary>
        public void CycleMode()
        {
            switch (mode)
            {
                case ViewportMode.Play:
                    SetMode(ViewportMode.Select);
                    break;
                case ViewportMode.Select:
                    SetMode(ViewportMode.Inspect);
                    break;
                default:
                    SetMode(EditorApplication.isPlaying ? ViewportMode.Play : ViewportMode.Select);
                    break;
            }
        }

        /// <summary>The picking service over the current camera and image rectangle.</summary>
        public PickingService Picking
        {
            get
            {
                Camera camera = _renderer.Current != null ? _renderer.Current : _renderer.ChooseCamera();
                StudioRuntime runtime = Context.Runtime;
                if (_picking == null)
                {
                    _picking = new PickingService(camera, ImageRect, runtime.Resolver, runtime.Identity, null, StudioUiSettings.instance.CreatePickOptions());
                }

                _picking.Camera = camera;
                _picking.Viewport = ImageRect;
                StudioUiSettings settings = StudioUiSettings.instance;
                _picking.Options.IncludeUi = settings.IncludeUi;
                _picking.Options.OverlapDepthFraction = settings.OverlapDepthFraction;
                _picking.Options.GroundHeight = settings.GroundHeight;
                return _picking;
            }
        }

        /// <summary>The gesture-to-selection picker over <see cref="Picking"/> (shared with tests).</summary>
        public ViewportPicker Picker => _picker ??= new ViewportPicker(Context.Selection, () => Picking, _timings, () => StudioUiSettings.instance.OverlapRadiusPixels);

        /// <summary>
        /// Candidates under a point and within the overlap radius (center plus four samples at the radius), merged by
        /// target and part, depth ordered (UI first), ground excluded.
        /// </summary>
        public IReadOnlyList<PickCandidate> CandidatesAt(Vector2 point, out PickResult center) => Picker.CandidatesAt(point, out center);

        /// <summary>
        /// A click at <paramref name="point"/> (viewport points): selects the nearest candidate with <paramref name="op"/>,
        /// opens the overlap list when two or more candidates lie under the cursor, clears on empty ground (Replace).
        /// </summary>
        public IReadOnlyList<PickCandidate> ClickAt(Vector2 point, SelectionOp op)
        {
            IReadOnlyList<PickCandidate> candidates = Picker.Click(point, op);
            if (candidates.Count >= 2 && _overlap != null)
            {
                _pressOp = op;
                _overlap.Show(candidates, point + new Vector2(12f, 12f), NameOf);
            }
            else
            {
                _overlap?.Hide();
            }

            return candidates;
        }

        /// <summary>A marquee over <paramref name="rect"/> (viewport points).</summary>
        public PickResult MarqueeSelect(Rect rect, SelectionOp op, bool? full = null) => Picker.Marquee(rect, op, full ?? fullContainment);

        /// <summary>Right-click point-at: samples the ground (NavMesh when present) and sets the selection's location.</summary>
        public LocationPick PointAtLocation(Vector2 point)
        {
            LocationPick location = Picker.PointAt(point);
            if (location.Hit)
            {
                _markerWorld = location.Position;
                if (_markerLabel != null)
                {
                    _markerLabel.text = location.Source + " " + location.Position.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                        + location.Position.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " + location.Position.z.ToString("0.0", CultureInfo.InvariantCulture)
                        + " (" + (location.Location?.Location?.Region ?? "?") + ")";
                }
            }

            return location;
        }

        /// <summary>Hover at a point (Select highlight / Inspect card); returns the hovered candidate.</summary>
        public PickCandidate? HoverAt(Vector2 point)
        {
            _hover = Picker.Hover(point);
            UpdateHoverVisuals();
            return _hover;
        }

        /// <summary>Renders the current camera now (tests and evidence); false without graphics or before layout.</summary>
        public bool RenderNow()
        {
            if (!EnsureTarget())
            {
                return false;
            }

            Camera camera = _renderer.ChooseCamera();
            bool rendered = _renderer.Render(camera);
            _image?.MarkDirtyRepaint();
            return rendered;
        }

        /// <summary>
        /// Captures the frame context of a prompt (03 s2): camera pose, viewport size and the rendered image stored in
        /// Studio/Artifacts (referenced by stamp, never inline). Without graphics the image is omitted.
        /// </summary>
        public FrameContext? CaptureFrame()
        {
            Camera camera = _renderer.Current != null ? _renderer.Current : _renderer.ChooseCamera();
            Rect rect = ImageRect;
            CameraPose pose = new CameraPose(
                new double[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                new double[] { camera.transform.rotation.x, camera.transform.rotation.y, camera.transform.rotation.z, camera.transform.rotation.w },
                camera.fieldOfView,
                rect.height > 0f ? rect.width / rect.height : camera.aspect);
            RenderTexture? target = _renderer.Target;
            int[] size = { target != null ? target.width : Mathf.RoundToInt(rect.width), target != null ? target.height : Mathf.RoundToInt(rect.height) };
            string? image = null;
            if (target != null && ViewportRenderer.CanRender)
            {
                byte[] png = EncodeTarget(target);
                string digest = ContentStamp.Sha256Hex(png);
                Context.Runtime.Artifacts.Put(png, new ArtifactRef(digest, "image/png", png.LongLength, "frame.png", null, "frame"));
                image = ContentStamp.Prefix + digest;
            }

            return new FrameContext(pose, size, image);
        }

        /// <summary>PNG bytes of the current viewport image (evidence; null without graphics).</summary>
        public byte[]? EncodeViewportPng()
        {
            RenderTexture? target = _renderer.Target;
            return target == null || !ViewportRenderer.CanRender ? null : EncodeTarget(target);
        }

        /// <summary>Projects a world point into viewport points (null behind the camera).</summary>
        public Vector2? Project(Vector3 world)
        {
            Camera? camera = _renderer.Current;
            if (camera == null)
            {
                return null;
            }

            Vector3 viewport = camera.WorldToViewportPoint(world);
            if (viewport.z <= 0f)
            {
                return null;
            }

            Rect rect = ImageRect;
            return new Vector2(rect.x + (viewport.x * rect.width), rect.y + ((1f - viewport.y) * rect.height));
        }

        /// <summary>The viewport-point rectangle covering a GameObject's renderers (null when off screen).</summary>
        public Rect? ScreenRectOf(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Vector2? point = Project(target.transform.position);
                return point.HasValue ? new Rect(point.Value - new Vector2(6f, 6f), new Vector2(12f, 12f)) : (Rect?)null;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector2? point = Project(corner);
                if (!point.HasValue)
                {
                    continue;
                }

                any = true;
                min = Vector2.Min(min, point.Value);
                max = Vector2.Max(max, point.Value);
            }

            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : (Rect?)null;
        }

        // ------------------------------------------------------------------------------------------- lifecycle

        private void OnEnable()
        {
            titleContent = new GUIContent(StudioWindowIds.ViewportTitle);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.hierarchyChanged += MarkRenderDirty;
            Undo.undoRedoPerformed += MarkRenderDirty;
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.hierarchyChanged -= MarkRenderDirty;
            Undo.undoRedoPerformed -= MarkRenderDirty;
            _gizmo?.Cancel();
            _routing.Dispose();
            if (_context != null)
            {
                _context.PromptPrefillRequested -= OnPromptPrefill;
            }

            RestoreMoveTool();
            SaveFreePose();
            _renderer.Dispose();
            _uiBuilt = false;
        }

        private void CreateGUI()
        {
            if (_uiBuilt)
            {
                return;
            }

            StudioUiContext context = Context;
            VisualElement root = rootVisualElement;
            root.Clear();
            StudioStyles.Apply(root);
            root.AddToClassList("gcs-viewport");

            VisualElement toolbar = new VisualElement { name = "viewport-toolbar" };
            toolbar.AddToClassList("gcs-row");
            toolbar.AddToClassList("gcs-toolbar");
            root.Add(toolbar);
            _playButton = ModeButton(toolbar, "Play", ViewportMode.Play, "Play: input goes to the game (Play Mode)");
            _selectButton = ModeButton(toolbar, "Select", ViewportMode.Select, "Select: click, shift-add, ctrl-toggle, drag a marquee, right-click to point at the ground");
            _inspectButton = ModeButton(toolbar, "Inspect", ViewportMode.Inspect, "Inspect: hover shows the authoring card");
            toolbar.Add(Separator());
            _pauseButton = new Button(() => Root(app => app.Pause())) { name = "app-pause", text = "Pause", tooltip = "Pause the GameCore world (application root)" };
            _resumeButton = new Button(() => Root(app => app.Resume())) { name = "app-resume", text = "Resume", tooltip = "Resume the GameCore world" };
            _stepButton = new Button(StepWorld) { name = "app-step", text = "Step", tooltip = "Run the paused world for exactly one pump" };
            toolbar.Add(_pauseButton);
            toolbar.Add(_resumeButton);
            toolbar.Add(_stepButton);
            _appStateLabel = new Label { name = "app-state" };
            toolbar.Add(_appStateLabel);
            toolbar.Add(Separator());
            Toggle full = new Toggle("Full") { name = "marquee-full", value = fullContainment, tooltip = "Marquee requires full containment (off: any overlap)" };
            full.RegisterValueChangedCallback(change => fullContainment = change.newValue);
            toolbar.Add(full);
            Toggle free = new Toggle("Free cam") { name = "free-camera", value = forceFreeCamera, tooltip = "Show the Studio free camera even while the game runs" };
            free.RegisterValueChangedCallback(change =>
            {
                forceFreeCamera = change.newValue;
                _renderer.ForceFreeCamera = forceFreeCamera;
                MarkRenderDirty();
            });
            toolbar.Add(free);
            _cameraLabel = new Label { name = "camera-label" };
            _cameraLabel.AddToClassList("gcs-muted");
            toolbar.Add(_cameraLabel);
            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("gcs-grow");
            toolbar.Add(spacer);
            _pumpLabel = new Label { name = "pump-indicator", tooltip = "Sanctioned GameCore pumps per frame (must be exactly 1 in Play) and frame time (B-FRAME)" };
            _pumpLabel.AddToClassList("gcs-pump");
            toolbar.Add(_pumpLabel);
            toolbar.Add(new Button(FirstRunWizardWindow.Open) { text = "?", tooltip = "How the Studio modes work" });

            _area = new VisualElement { name = "viewport-area", focusable = true };
            _area.AddToClassList("gcs-viewport__area");
            _area.tabIndex = 0;
            root.Add(_area);
            _image = new Image { name = "viewport-image", scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _image.AddToClassList("gcs-fill");
            _area.Add(_image);

            VisualElement overlay = new VisualElement { name = "viewport-overlay", pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("gcs-fill");
            _area.Add(overlay);
            _hoverBox = new VisualElement { name = "hover-box", pickingMode = PickingMode.Ignore };
            _hoverBox.AddToClassList("gcs-hover-box");
            overlay.Add(_hoverBox);
            _marqueeBox = new VisualElement { name = "marquee-box", pickingMode = PickingMode.Ignore };
            _marqueeBox.AddToClassList("gcs-marquee");
            overlay.Add(_marqueeBox);
            _marker = new VisualElement { name = "point-marker", pickingMode = PickingMode.Ignore };
            _marker.AddToClassList("gcs-marker");
            _markerLabel = new Label { pickingMode = PickingMode.Ignore };
            _markerLabel.AddToClassList("gcs-marker__label");
            _marker.Add(_markerLabel);
            overlay.Add(_marker);
            _gizmo = new ViewportMoveGizmo(() => Context.Runtime);
            overlay.Add(_gizmo);
            _hoverCard = new VisualElement { name = "hover-card", pickingMode = PickingMode.Ignore };
            _hoverCard.AddToClassList("gcs-card");
            overlay.Add(_hoverCard);
            _badges = new VisualElement { name = "selection-badges" };
            _badges.AddToClassList("gcs-badges");
            overlay.Add(_badges);
            _statusLabel = new Label { name = "viewport-status", pickingMode = PickingMode.Ignore };
            _statusLabel.AddToClassList("gcs-viewport__status");
            overlay.Add(_statusLabel);
            _overlap = new OverlapPopup((candidate, part) => Choose(candidate, part, _pressOp));
            _area.Add(_overlap);
            _strip = new CandidateStripView(context);
            _area.Add(_strip);

            _prompt = new PromptBar(context, CaptureFrame);
            root.Add(_prompt);

            _area.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _area.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _area.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _area.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hover = null;
                UpdateHoverVisuals();
            });
            _area.RegisterCallback<WheelEvent>(OnWheel);
            _area.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _area.RegisterCallback<KeyUpEvent>(evt => _keys.Remove(evt.keyCode));
            _area.RegisterCallback<GeometryChangedEvent>(_ => MarkRenderDirty());
            _area.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous)
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            context.PromptPrefillRequested += OnPromptPrefill;

            _renderer.ForceFreeCamera = forceFreeCamera;
            if (freePoseSaved)
            {
                _renderer.FreeCamera.transform.SetPositionAndRotation(freePosition, freeRotation);
            }
            else
            {
                _renderer.MatchSceneView();
            }

            _uiBuilt = true;
            if (mode == ViewportMode.Play && !EditorApplication.isPlaying)
            {
                mode = ViewportMode.Select;
            }

            UpdateMoveTool();
            RefreshToolbar();
            if (!StudioUiSettings.FirstRunDone && !Application.isBatchMode && _context == StudioUiSession.ContextIfCreated)
            {
                EditorApplication.delayCall += FirstRunWizardWindow.Open;
            }
        }

        /// <summary>Builds the UI now when the editor has not called CreateGUI yet (tests, evidence).</summary>
        public void EnsureGui()
        {
            if (!_uiBuilt)
            {
                CreateGUI();
            }
        }

        /// <summary>The viewport area element (null before the UI is built).</summary>
        public VisualElement? Area => _area;

        // ------------------------------------------------------------------------------------------- per-frame

        private void Tick()
        {
            if (!_uiBuilt || _area == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Min(0.1, Math.Max(0.0, now - _lastTick));
            _lastTick = now;
            _pump.Sample();
            if (_stepTarget >= 0)
            {
                GameApplicationRoot? root = GameApplication.Current;
                if (root == null || root.PumpCounter.SanctionedPumps >= _stepTarget)
                {
                    if (root != null && root.State == GameApplicationState.Running)
                    {
                        root.Pause();
                    }

                    _stepTarget = -1;
                }
            }

            bool shouldRoute = mode == ViewportMode.Play && EditorApplication.isPlaying && focusedWindow == this;
            _routing.Update(shouldRoute);
            if (_looking && _renderer.Source == ViewportCameraSource.FreeCamera)
            {
                FlyFreeCamera((float)delta);
            }

            bool playing = EditorApplication.isPlaying && !EditorApplication.isPaused;
            int hz = StudioUiSettings.instance.EditModeRenderHz;
            if (EnsureTarget() && (playing || _renderDirty || now - _lastRender >= 1.0 / hz))
            {
                Camera camera = _renderer.ChooseCamera();
                _renderer.Render(camera);
                _lastRender = now;
                _renderDirty = false;
                _image?.MarkDirtyRepaint();
            }
            else
            {
                _renderer.ChooseCamera();
            }

            if (_hoverDirty && (mode == ViewportMode.Select || mode == ViewportMode.Inspect) && _pressButton < 0)
            {
                _hoverDirty = false;
                HoverAt(_pointer);
            }
            else if (_hover != null)
            {
                UpdateHoverVisuals();
            }

            UpdateMarker();
            UpdateGizmo();
            if (now - _lastBadges > 0.5 || _badgeVersion != Context.Selection.Version)
            {
                _lastBadges = now;
                RefreshBadges();
                RefreshToolbar();
                _prompt?.Refresh();
            }

            if (_pumpLabel != null)
            {
                _pumpLabel.text = _pump.Text();
                _pumpLabel.EnableInClassList("gcs-pump--ok", _pump.Health == PumpHealth.Ok);
                _pumpLabel.EnableInClassList("gcs-pump--bad", _pump.Health == PumpHealth.Violation);
            }
        }

        private bool EnsureTarget()
        {
            if (_area == null || !ViewportRenderer.CanRender)
            {
                return false;
            }

            Rect rect = _area.contentRect;
            if (float.IsNaN(rect.width) || rect.width < 2f || rect.height < 2f)
            {
                return false;
            }

            float scale = EditorGUIUtility.pixelsPerPoint;
            RenderTexture target = _renderer.EnsureTarget(Mathf.RoundToInt(rect.width * scale), Mathf.RoundToInt(rect.height * scale));
            if (_image != null && _image.image != target)
            {
                _image.image = target;
            }

            return true;
        }

        // ------------------------------------------------------------------------------------------- input

        private void OnPointerDown(PointerDownEvent evt)
        {
            _area?.Focus();
            _pointer = evt.localPosition;
            if (mode == ViewportMode.Play)
            {
                return;
            }

            if (evt.button == 0 && mode == ViewportMode.Select && _gizmo != null)
            {
                int axis = _gizmo.HitTest(_pointer);
                if (axis >= 0 && _gizmo.BeginDrag(axis, _pointer, evt.pointerId))
                {
                    _area?.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                    return;
                }
            }

            _pressButton = evt.button;
            _pressPosition = _pointer;
            _dragging = false;
            _pressOp = OpOf(evt.shiftKey, evt.ctrlKey || evt.commandKey);
            _area?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 previous = _pointer;
            _pointer = evt.localPosition;
            if (mode == ViewportMode.Play)
            {
                return;
            }

            if (_gizmo != null && _gizmo.Dragging)
            {
                _gizmo.DragTo(_pointer);
                MarkRenderDirty();
                return;
            }

            if (_pressButton < 0)
            {
                _hoverDirty = true;
                return;
            }

            if (!_dragging && Vector2.Distance(_pointer, _pressPosition) > DragThreshold)
            {
                _dragging = true;
                _looking = _pressButton == 1;
            }

            if (_dragging && _pressButton == 0 && _marqueeBox != null)
            {
                Rect rect = Rect.MinMaxRect(Mathf.Min(_pressPosition.x, _pointer.x), Mathf.Min(_pressPosition.y, _pointer.y), Mathf.Max(_pressPosition.x, _pointer.x), Mathf.Max(_pressPosition.y, _pointer.y));
                Place(_marqueeBox, rect);
                _marqueeBox.style.display = DisplayStyle.Flex;
            }
            else if (_looking && _renderer.Source == ViewportCameraSource.FreeCamera)
            {
                Vector2 delta = _pointer - previous;
                Transform camera = _renderer.FreeCamera.transform;
                Vector3 euler = camera.eulerAngles;
                float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                pitch = Mathf.Clamp(pitch + (delta.y * 0.2f), -89f, 89f);
                camera.rotation = Quaternion.Euler(pitch, euler.y + (delta.x * 0.2f), 0f);
                MarkRenderDirty();
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            _pointer = evt.localPosition;
            if (_area != null && _area.HasPointerCapture(evt.pointerId))
            {
                _area.ReleasePointer(evt.pointerId);
            }

            if (_gizmo != null && _gizmo.Dragging)
            {
                ApplyReport? report = _gizmo.EndDrag();
                SetStatus(report == null ? "Move cancelled." : "Move " + report.State + " (" + report.Entry.Id + ") in " + report.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                MarkRenderDirty();
                return;
            }

            int button = _pressButton;
            _pressButton = -1;
            bool dragged = _dragging;
            _dragging = false;
            _looking = false;
            if (_marqueeBox != null)
            {
                _marqueeBox.style.display = DisplayStyle.None;
            }

            if (mode == ViewportMode.Play || button < 0)
            {
                return;
            }

            if (button == 0)
            {
                if (dragged)
                {
                    Rect rect = Rect.MinMaxRect(Mathf.Min(_pressPosition.x, _pointer.x), Mathf.Min(_pressPosition.y, _pointer.y), Mathf.Max(_pressPosition.x, _pointer.x), Mathf.Max(_pressPosition.y, _pointer.y));
                    PickResult result = MarqueeSelect(rect, _pressOp);
                    SetStatus("Marquee: " + result.Targets().Count + " object(s) in " + result.Timings.TotalMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms" + (fullContainment ? " (full containment)" : string.Empty));
                }
                else
                {
                    IReadOnlyList<PickCandidate> candidates = ClickAt(_pointer, _pressOp);
                    SetStatus(candidates.Count == 0 ? "Nothing here." : candidates.Count + " candidate(s); selected " + NameOf(candidates[0].Ref));
                }
            }
            else if (button == 1 && !dragged)
            {
                LocationPick location = PointAtLocation(_pointer);
                SetStatus(location.Hit ? "Point-at: " + _markerLabel?.text : "Point-at: no ground under the cursor.");
            }

            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (mode == ViewportMode.Play || _renderer.Source != ViewportCameraSource.FreeCamera)
            {
                return;
            }

            Transform camera = _renderer.FreeCamera.transform;
            camera.position -= camera.forward * (evt.delta.y * 0.5f);
            MarkRenderDirty();
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (HandleKey(evt.keyCode))
            {
                evt.StopPropagation();
            }
        }

        /// <summary>
        /// The viewport's keyboard commands (also driven directly by tests): Tab cycles the mode; Esc cancels a drag,
        /// closes the overlap list, leaves Play mode or clears the selection; 1/2/3 pick a mode; F frames the
        /// selection; WASD/QE fly the free camera while the right button is held. True when the key was consumed.
        /// </summary>
        public bool HandleKey(KeyCode key)
        {
            if (key == KeyCode.Tab)
            {
                CycleMode();
                return true;
            }

            if (key == KeyCode.Escape)
            {
                if (_gizmo != null && _gizmo.Dragging)
                {
                    _gizmo.Cancel();
                }
                else if (_overlap != null && _overlap.Visible)
                {
                    _overlap.Hide();
                }
                else if (mode == ViewportMode.Play)
                {
                    SetMode(ViewportMode.Select);
                }
                else
                {
                    Context.Selection.Clear();
                    _markerWorld = null;
                }

                return true;
            }

            if (mode == ViewportMode.Play)
            {
                return false;
            }

            if (key != KeyCode.None)
            {
                _keys.Add(key);
            }

            if (_looking)
            {
                return true;
            }

            switch (key)
            {
                case KeyCode.Alpha1:
                    SetMode(ViewportMode.Play);
                    return true;
                case KeyCode.Alpha2:
                    SetMode(ViewportMode.Select);
                    return true;
                case KeyCode.Alpha3:
                    SetMode(ViewportMode.Inspect);
                    return true;
                case KeyCode.F:
                    FrameSelection();
                    return true;
                default:
                    return false;
            }
        }

        private void FlyFreeCamera(float delta)
        {
            Transform camera = _renderer.FreeCamera.transform;
            Vector3 move = Vector3.zero;
            if (_keys.Contains(KeyCode.W))
            {
                move += camera.forward;
            }

            if (_keys.Contains(KeyCode.S))
            {
                move -= camera.forward;
            }

            if (_keys.Contains(KeyCode.D))
            {
                move += camera.right;
            }

            if (_keys.Contains(KeyCode.A))
            {
                move -= camera.right;
            }

            if (_keys.Contains(KeyCode.E))
            {
                move += Vector3.up;
            }

            if (_keys.Contains(KeyCode.Q))
            {
                move -= Vector3.up;
            }

            if (move.sqrMagnitude > 0f)
            {
                float speed = _keys.Contains(KeyCode.LeftShift) ? 30f : 8f;
                camera.position += move.normalized * (speed * delta);
                MarkRenderDirty();
            }
        }

        private void FrameSelection()
        {
            IReadOnlyList<UnityEngine.Object> objects = Context.Selection.ResolveObjects();
            bool any = false;
            Bounds bounds = default;
            foreach (UnityEngine.Object target in objects)
            {
                if (!(target is GameObject gameObject))
                {
                    continue;
                }

                foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>())
                {
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                if (!any)
                {
                    bounds = new Bounds(gameObject.transform.position, Vector3.one);
                    any = true;
                }
            }

            if (any)
            {
                _renderer.ForceFreeCamera = true;
                forceFreeCamera = true;
                _renderer.Frame(bounds);
                MarkRenderDirty();
            }
        }

        // ------------------------------------------------------------------------------------------- visuals

        private void Choose(PickCandidate candidate, bool part, SelectionOp op) => Picker.Choose(candidate, part, op);

        private void UpdateHoverVisuals()
        {
            if (_hoverBox == null || _hoverCard == null)
            {
                return;
            }

            GameObject? owner = _hover == null ? null : (_hover.HitObject != null ? _hover.HitObject : (_hover.Owner as Component)?.gameObject);
            if (_hover?.Owner is Component component)
            {
                owner = component.gameObject;
            }

            Rect? rect = owner != null && mode != ViewportMode.Play ? ScreenRectOf(owner) : null;
            if (rect.HasValue)
            {
                Place(_hoverBox, rect.Value);
                _hoverBox.style.display = DisplayStyle.Flex;
            }
            else
            {
                _hoverBox.style.display = DisplayStyle.None;
            }

            if (mode != ViewportMode.Inspect || _hover == null)
            {
                _hoverCard.style.display = DisplayStyle.None;
                return;
            }

            SelectionBadge badge = SelectionModel.DescribeRef(Context.Runtime, _hover.Ref);
            _hoverCard.Clear();
            Label name = new Label(badge.Label);
            name.AddToClassList("gcs-card__title");
            _hoverCard.Add(name);
            _hoverCard.Add(new Label("type " + badge.TypeId));
            _hoverCard.Add(new Label("id " + (_hover.Ref.AuthoringId ?? "-")));
            _hoverCard.Add(new Label("definition " + (_hover.Ref.Definition ?? "-")));
            if (_hover.Part != null)
            {
                _hoverCard.Add(new Label("part " + _hover.Part));
            }

            if (badge.Stale)
            {
                _hoverCard.Add(StudioStyles.Badge("stale", "error"));
            }

            if (!badge.Resident)
            {
                _hoverCard.Add(StudioStyles.Badge("unloaded", "warn"));
            }

            Rect area = ImageRect;
            float x = Mathf.Min(_pointer.x + 16f, Mathf.Max(0f, area.width - 260f));
            float y = Mathf.Min(_pointer.y + 16f, Mathf.Max(0f, area.height - 120f));
            _hoverCard.style.left = x;
            _hoverCard.style.top = y;
            _hoverCard.style.display = DisplayStyle.Flex;
        }

        private void UpdateMarker()
        {
            if (_marker == null)
            {
                return;
            }

            if (_markerWorld.HasValue && Context.Selection.Location != null)
            {
                Vector2? point = Project(_markerWorld.Value);
                if (point.HasValue)
                {
                    _marker.style.left = point.Value.x - 7f;
                    _marker.style.top = point.Value.y - 7f;
                    _marker.style.display = DisplayStyle.Flex;
                    return;
                }
            }

            _marker.style.display = DisplayStyle.None;
        }

        private void UpdateGizmo()
        {
            if (_gizmo == null)
            {
                return;
            }

            GameObject? target = null;
            if (mode == ViewportMode.Select && Context.Selection.Targets.Count == 1)
            {
                UnityEngine.Object? resolved = Context.Selection.ResolvePrimary();
                GameObject? candidate = resolved is Component component ? component.gameObject : resolved as GameObject;
                if (candidate != null && !EditorUtility.IsPersistent(candidate) && !AuthoringRefResolver.IsStudioInternal(candidate))
                {
                    target = candidate;
                }
            }

            _gizmo.Refresh(target, Project);
        }

        private void RefreshBadges()
        {
            if (_badges == null)
            {
                return;
            }

            _badgeVersion = Context.Selection.Version;
            _badges.Clear();
            foreach (SelectionBadge badge in Context.Selection.Describe())
            {
                Label label = new Label(badge.Label + (badge.Stale ? "  stale" : string.Empty)) { tooltip = badge.ResidencyReason ?? badge.StaleReason ?? badge.TypeId };
                label.AddToClassList("gcs-badge");
                label.EnableInClassList("gcs-badge--disabled", !badge.Resident);
                label.EnableInClassList("gcs-badge--error", badge.Stale);
                label.SetEnabled(badge.Resident);
                _badges.Add(label);
            }

            if (Context.Selection.Location?.Location != null)
            {
                _badges.Add(StudioStyles.Badge("location " + Context.Selection.Location.Location.Region, "location"));
            }
        }

        private void RefreshToolbar()
        {
            if (_playButton == null || _selectButton == null || _inspectButton == null)
            {
                return;
            }

            _playButton.EnableInClassList("gcs-mode--active", mode == ViewportMode.Play);
            _selectButton.EnableInClassList("gcs-mode--active", mode == ViewportMode.Select);
            _inspectButton.EnableInClassList("gcs-mode--active", mode == ViewportMode.Inspect);
            _playButton.SetEnabled(EditorApplication.isPlaying);
            GameApplicationRoot? root = EditorApplication.isPlaying ? GameApplication.Current : null;
            bool live = root != null && root.State != GameApplicationState.Stopped;
            _pauseButton?.SetEnabled(live && root!.State == GameApplicationState.Running);
            _resumeButton?.SetEnabled(live && (root!.State == GameApplicationState.Paused || root.State == GameApplicationState.Ready));
            _stepButton?.SetEnabled(live && root!.State != GameApplicationState.Running);
            if (_appStateLabel != null)
            {
                _appStateLabel.text = root == null ? "no world" : root.State.ToString();
            }

            if (_cameraLabel != null)
            {
                Camera? camera = _renderer.Current;
                _cameraLabel.text = (_renderer.Source == ViewportCameraSource.GameCamera ? "game camera" : "free camera") + (camera != null ? " (" + camera.name + ")" : string.Empty)
                    + (_routing.Active ? " · input → game" : string.Empty);
            }
        }

        private void UpdateMoveTool()
        {
            if (mode == ViewportMode.Select)
            {
                if (ToolManager.activeToolType != typeof(StudioSelectMoveTool))
                {
                    ToolManager.SetActiveTool<StudioSelectMoveTool>();
                }

                _toolActivated = true;
            }
            else
            {
                RestoreMoveTool();
            }
        }

        private void RestoreMoveTool()
        {
            if (_toolActivated && ToolManager.activeToolType == typeof(StudioSelectMoveTool))
            {
                ToolManager.RestorePreviousPersistentTool();
            }

            _toolActivated = false;
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }

        private void OnPromptPrefill(string text)
        {
            _prompt?.Prefill(text);
            Focus();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SetMode(ViewportMode.Play);
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                _routing.Update(false);
                if (mode == ViewportMode.Play)
                {
                    SetMode(ViewportMode.Select);
                }
            }

            _pump.Reset();
            _picking = null;
            MarkRenderDirty();
        }

        private void StepWorld()
        {
            GameApplicationRoot? root = GameApplication.Current;
            if (root == null || root.State == GameApplicationState.Running || root.State == GameApplicationState.Stopped)
            {
                return;
            }

            _stepTarget = root.PumpCounter.SanctionedPumps + 1;
            root.Resume();
        }

        private static void Root(Action<GameApplicationRoot> action)
        {
            GameApplicationRoot? root = GameApplication.Current;
            if (root != null && root.State != GameApplicationState.Stopped)
            {
                action(root);
            }
        }

        private Button ModeButton(VisualElement toolbar, string text, ViewportMode target, string tooltip)
        {
            Button button = new Button(() => SetMode(target)) { name = "mode-" + text.ToLowerInvariant(), text = text, tooltip = tooltip + " (Tab cycles)" };
            button.AddToClassList("gcs-mode");
            toolbar.Add(button);
            return button;
        }

        private static VisualElement Separator()
        {
            VisualElement separator = new VisualElement();
            separator.AddToClassList("gcs-separator");
            return separator;
        }

        private static void Place(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private static SelectionOp OpOf(bool shift, bool control)
        {
            return control ? SelectionOp.Toggle : shift ? SelectionOp.Add : SelectionOp.Replace;
        }

        private string NameOf(AuthoringRef reference)
        {
            return SelectionModel.DescribeRef(Context.Runtime, reference).Label;
        }

        private static byte[] EncodeTarget(RenderTexture target)
        {
            RenderTexture? previous = RenderTexture.active;
            Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                texture.Apply(false, false);
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyImmediate(texture);
            }
        }

        private void MarkRenderDirty() => _renderDirty = true;

        private void SaveFreePose()
        {
            Camera camera = _renderer.FreeCamera;
            freePosition = camera.transform.position;
            freeRotation = camera.transform.rotation;
            freePoseSaved = true;
        }
    }
}
