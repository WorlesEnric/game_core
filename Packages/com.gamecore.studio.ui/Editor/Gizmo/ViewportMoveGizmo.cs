// GameCore.Studio.UI - the viewport's move gizmo (SR-1.6, W-EDIT-05). In Select mode with one selected scene object, three
// axis handles are drawn over the viewport image at the object's projected position; dragging a handle moves the object
// live along that world axis through P1.6's GizmoMoveController (no Undo, no journal while dragging) and mouse-up
// applies exactly one `move` change set (identical to typing the final position). Escape cancels the drag.
#nullable enable
using System;
using GameCore.Studio.Edit;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>Axis-handle overlay driving a <see cref="GizmoMoveController"/>.</summary>
    public sealed class ViewportMoveGizmo : VisualElement
    {
        public const float HandleLength = 70f;
        public const float HandleHitWidth = 8f;

        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] AxisColors = { new Color(0.95f, 0.3f, 0.3f), new Color(0.4f, 0.9f, 0.35f), new Color(0.3f, 0.55f, 1f) };

        private readonly Func<StudioRuntime> _runtime;
        private GizmoMoveController? _controller;
        private Func<Vector3, Vector2?>? _project;
        private GameObject? _target;
        private int _activeAxis = -1;
        private Vector2 _dragStartPointer;
        private Vector3 _dragStartPosition;
        private int _pointerId = -1;
        private Vector2 _origin;
        private readonly Vector2[] _ends = new Vector2[3];
        private bool _visible;

        public ViewportMoveGizmo(Func<StudioRuntime> runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            name = "move-gizmo";
            AddToClassList("gcs-gizmo");
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>The applied report of the last drag (null when the drag was cancelled or moved nothing).</summary>
        public ApplyReport? LastReport { get; private set; }

        public bool Dragging => _controller != null && _controller.Dragging;

        public GameObject? Target => _target;

        /// <summary>
        /// Updates the target and its projection (call every frame). <paramref name="project"/> maps a world point to the
        /// viewport's pixel space (null behind the camera).
        /// </summary>
        public void Refresh(GameObject? target, Func<Vector3, Vector2?> project)
        {
            _project = project;
            if (target != _target && Dragging)
            {
                Cancel();
            }

            _target = target;
            _visible = false;
            if (_target != null)
            {
                Vector2? origin = project(_target.transform.position);
                if (origin.HasValue)
                {
                    _origin = origin.Value;
                    _visible = true;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vector2 direction = ScreenAxis(axis);
                        _ends[axis] = _origin + (direction.sqrMagnitude > 0.0001f ? direction.normalized * HandleLength : Vector2.zero);
                    }
                }
            }

            MarkDirtyRepaint();
        }

        /// <summary>The handle under a viewport point (0 x, 1 y, 2 z), or -1.</summary>
        public int HitTest(Vector2 point)
        {
            if (!_visible)
            {
                return -1;
            }

            int best = -1;
            float bestDistance = HandleHitWidth;
            for (int axis = 0; axis < 3; axis++)
            {
                float distance = DistanceToSegment(point, _origin, _ends[axis]);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = axis;
                }
            }

            return best;
        }

        /// <summary>Starts a drag on <paramref name="axis"/>; true when a drag started.</summary>
        public bool BeginDrag(int axis, Vector2 pointer, int pointerId)
        {
            if (_target == null || axis < 0 || axis > 2)
            {
                return false;
            }

            _controller ??= new GizmoMoveController(_runtime());
            _controller.Begin(_target);
            _activeAxis = axis;
            _dragStartPointer = pointer;
            _dragStartPosition = _target.transform.position;
            _pointerId = pointerId;
            LastReport = null;
            return true;
        }

        /// <summary>Moves the object along the active axis by the pointer's projected travel.</summary>
        public void DragTo(Vector2 pointer)
        {
            if (!Dragging || _activeAxis < 0 || _controller == null)
            {
                return;
            }

            Vector2 screenAxis = ScreenAxisAt(_dragStartPosition, _activeAxis);
            float lengthSquared = screenAxis.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return;
            }

            float worldDelta = Vector2.Dot(pointer - _dragStartPointer, screenAxis) / lengthSquared;
            _controller.DragTo(_dragStartPosition + (Axes[_activeAxis] * worldDelta));
        }

        /// <summary>Ends the drag: one `move` change set.</summary>
        public ApplyReport? EndDrag()
        {
            if (_controller == null || !_controller.Dragging)
            {
                return null;
            }

            _activeAxis = -1;
            _pointerId = -1;
            LastReport = _controller.End();
            return LastReport;
        }

        public void Cancel()
        {
            _controller?.Cancel();
            _activeAxis = -1;
            _pointerId = -1;
        }

        public int PointerId => _pointerId;

        private Vector2 ScreenAxis(int axis) => _target == null ? Vector2.zero : ScreenAxisAt(_target.transform.position, axis);

        /// <summary>Pixels per world unit along an axis at a world position (projected).</summary>
        private Vector2 ScreenAxisAt(Vector3 position, int axis)
        {
            if (_project == null)
            {
                return Vector2.zero;
            }

            Vector2? from = _project(position);
            Vector2? to = _project(position + Axes[axis]);
            return from.HasValue && to.HasValue ? to.Value - from.Value : Vector2.zero;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!_visible)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.lineWidth = 3f;
            painter.lineCap = LineCap.Round;
            for (int axis = 0; axis < 3; axis++)
            {
                painter.strokeColor = axis == _activeAxis ? Color.yellow : AxisColors[axis];
                painter.BeginPath();
                painter.MoveTo(_origin);
                painter.LineTo(_ends[axis]);
                painter.Stroke();
                painter.fillColor = painter.strokeColor;
                painter.BeginPath();
                painter.Arc(_ends[axis], 5f, 0f, 360f);
                painter.Fill();
            }

            painter.fillColor = new Color(1f, 1f, 1f, 0.8f);
            painter.BeginPath();
            painter.Arc(_origin, 3.5f, 0f, 360f);
            painter.Fill();
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < 0.0001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
            return Vector2.Distance(point, a + (ab * t));
        }
    }
}
