// GameCore.Studio.UI - the Scene view half of the Studio move gizmo (SR-1.6, W-EDIT-05): an EditorTool the viewport
// activates while it is in Select mode. A position-handle drag moves the preview through P1.6's
// GizmoMoveController and mouse-up applies one `move` change set (never Undo.RecordObject directly). Escape cancels.
#nullable enable
using GameCore.Studio.Edit;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The Studio move tool for the Scene view.</summary>
    [EditorTool("GameCore Studio Select/Move")]
    public sealed class StudioSelectMoveTool : EditorTool
    {
        private GizmoMoveController? _controller;

        /// <summary>The report of the last completed drag.</summary>
        public ApplyReport? LastReport { get; private set; }

        public override GUIContent toolbarIcon => new GUIContent("GC Move", "GameCore Studio move: one journaled change set per drag");

        public override void OnWillBeDeactivated()
        {
            _controller?.Cancel();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            GameObject? selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected) || AuthoringRefResolver.IsStudioInternal(selected))
            {
                _controller?.Cancel();
                return;
            }

            _controller ??= new GizmoMoveController(StudioServices.Runtime);
            if (_controller.Dragging && _controller.Target != selected)
            {
                _controller.Cancel();
            }

            Event current = Event.current;
            if (_controller.Dragging && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                _controller.Cancel();
                current.Use();
                return;
            }

            Transform transform = _controller.PreviewTransform ?? selected.transform;
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(transform.position, Tools.pivotRotation == PivotRotation.Local ? transform.rotation : Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                if (!_controller.Dragging)
                {
                    _controller.Begin(selected);
                }

                _controller.DragTo(position);
            }

            if (_controller.Dragging && GUIUtility.hotControl == 0)
            {
                LastReport = _controller.End();
                if (LastReport != null && !LastReport.Ok && LastReport.Diagnostics.Count > 0)
                {
                    Debug.LogWarning(StudioStyles.Safe("GameCore Studio move refused: " + LastReport.Diagnostics[0]));
                }
            }
        }
    }
}
