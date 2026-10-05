// GameCore.Studio.Edit - fallback inspectors for [Authorable] types and the Studio move tool.
// The editors are fallbacks (isFallback): any custom editor a gameplay package declares wins. For a single selected
// [Authorable] object they show the generated inspector (commits are change sets) above Unity's default inspector in a
// foldout, so non-authorable serialized data stays reachable; for anything else they are the default inspector.
#nullable enable
using GameCore.Studio.Authoring;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Edit
{
    /// <summary>Shared body of the fallback editors.</summary>
    internal static class AuthorableEditorBody
    {
        public static VisualElement Create(Editor editor)
        {
            VisualElement root = new VisualElement();
            if (editor.targets.Length == 1 && editor.target != null)
            {
                StudioRuntime runtime = StudioServices.Runtime;
                if (runtime.Identity.IsAuthorable(editor.target))
                {
                    AuthoringInspectorBuilder builder = new AuthoringInspectorBuilder(runtime.Identity, runtime.Resolver.Codec, new ManualEditCommitter(runtime));
                    VisualElement? generated = builder.Build(editor.target);
                    if (generated != null)
                    {
                        root.Add(generated);
                        Foldout raw = new Foldout { text = "Serialized data", value = false };
                        InspectorElement.FillDefaultInspector(raw, editor.serializedObject, editor);
                        root.Add(raw);
                        return root;
                    }
                }
            }

            InspectorElement.FillDefaultInspector(root, editor.serializedObject, editor);
            return root;
        }
    }

    [CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
    [CanEditMultipleObjects]
    internal sealed class AuthorableMonoBehaviourEditor : Editor
    {
        public override VisualElement CreateInspectorGUI() => AuthorableEditorBody.Create(this);
    }

    [CustomEditor(typeof(ScriptableObject), true, isFallback = true)]
    [CanEditMultipleObjects]
    internal sealed class AuthorableScriptableObjectEditor : Editor
    {
        public override VisualElement CreateInspectorGUI() => AuthorableEditorBody.Create(this);
    }

    /// <summary>
    /// A position gizmo whose drag becomes one journaled <c>move</c> change set on mouse-up (identical to typing the
    /// final position, W-EDIT-05).
    /// </summary>
    [EditorTool("GameCore Studio Move")]
    internal sealed class StudioMoveTool : EditorTool
    {
        private GizmoMoveController? _controller;

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

            Transform transform = selected.transform;
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
                ApplyReport? report = _controller.End();
                if (report != null && !report.Ok && report.Diagnostics.Count > 0)
                {
                    Debug.LogWarning("GameCore Studio move refused: " + report.Diagnostics[0]);
                }
            }
        }
    }
}
