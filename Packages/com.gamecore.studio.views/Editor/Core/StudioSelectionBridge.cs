// GameCore.Studio.Views - the selection seam between the views and the Studio viewport (docs/studio/03 s2).
//
// P2.1 owns the Studio selection (its StudioSelection singleton and the viewport window). The views do not depend on
// it: they talk to IStudioSelectionBridge, and the integrator binds P2.1's selection with
// StudioViewsSession.BindSelection(bridge). Until then EditorSelectionBridge maps UnityEditor.Selection to
// AuthoringRefs through the project's ref resolver and frames the scene view on Focus.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;

namespace GameCore.Studio.Views
{
    /// <summary>What the views need from the Studio selection.</summary>
    public interface IStudioSelectionBridge
    {
        /// <summary>The current selection as authoring refs (logical owners; no stamps).</summary>
        IReadOnlyList<AuthoringRef> Current { get; }

        /// <summary>Raised after the selection changed (from any source).</summary>
        event Action? SelectionChanged;

        /// <summary>Selects the given authored things (a click on a card, a row, a diagnostic's <c>where</c>).</summary>
        void Select(IReadOnlyList<AuthoringRef> targets);

        /// <summary>Selects one thing and frames it in the viewport (a double-click).</summary>
        void Focus(AuthoringRef target);
    }

    /// <summary>The default bridge: UnityEditor.Selection plus the scene view.</summary>
    public sealed class EditorSelectionBridge : IStudioSelectionBridge, IDisposable
    {
        private readonly StudioRuntime _runtime;

        public EditorSelectionBridge(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Selection.selectionChanged += OnSelectionChanged;
        }

        public event Action? SelectionChanged;

        public IReadOnlyList<AuthoringRef> Current
        {
            get
            {
                List<AuthoringRef> refs = new List<AuthoringRef>();
                foreach (UnityEngine.Object selected in Selection.objects)
                {
                    if (selected == null)
                    {
                        continue;
                    }

                    AuthoringRef? reference = _runtime.Resolver.BuildRef(selected, null, false);
                    if (reference != null)
                    {
                        refs.Add(reference);
                    }
                }

                return refs;
            }
        }

        public void Select(IReadOnlyList<AuthoringRef> targets)
        {
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            foreach (AuthoringRef target in targets ?? Array.Empty<AuthoringRef>())
            {
                UnityEngine.Object? found = target.Kind == AuthoringKind.Location ? null : _runtime.Resolver.Find(target);
                if (found != null)
                {
                    objects.Add(found is UnityEngine.Component component ? component.gameObject : found);
                }
            }

            Selection.objects = objects.ToArray();
        }

        public void Focus(AuthoringRef target)
        {
            Select(new[] { target });
            if (Selection.activeGameObject != null && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
            else if (Selection.activeObject != null)
            {
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
        }

        public void Dispose()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            SelectionChanged?.Invoke();
        }
    }
}
