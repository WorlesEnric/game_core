// GameCore.Studio.Views - the views' editor-session bindings (a ScriptableSingleton: the only place this package keeps
// state that outlives one window). It holds the selection bridge and the gameplay bridge the integrator bound, the
// running world a boot owner registered, and creates the defaults on first use. Nothing here is persisted; after a
// domain reload the integrator's [InitializeOnLoad] binding runs again.
#nullable enable
using System;
using GameCore.Studio.Edit;
using UnityEditor;

namespace GameCore.Studio.Views
{
    /// <summary>Selection and gameplay bindings of the project's views.</summary>
    public sealed class StudioViewsSession : ScriptableSingleton<StudioViewsSession>
    {
        [NonSerialized]
        private IStudioSelectionBridge? _selection;

        [NonSerialized]
        private bool _selectionBound;

        [NonSerialized]
        private IGameplayCommandBridge? _gameplay;

        [NonSerialized]
        private object? _runningWorld;

        [NonSerialized]
        private StudioRuntime? _selectionRuntime;

        /// <summary>Raised when a binding changes (open views re-subscribe).</summary>
        public event Action? BindingsChanged;

        /// <summary>
        /// Binds the Studio selection (P2.1's StudioSelection adapter). Views opened afterwards, and open views on their
        /// next refresh, use it instead of UnityEditor.Selection.
        /// </summary>
        public static void BindSelection(IStudioSelectionBridge bridge)
        {
            StudioViewsSession session = instance;
            if (session._selection is IDisposable disposable && !session._selectionBound)
            {
                disposable.Dispose();
            }

            session._selection = bridge ?? throw new ArgumentNullException(nameof(bridge));
            session._selectionBound = true;
            session.BindingsChanged?.Invoke();
        }

        /// <summary>Binds a gameplay command bridge (a host-level command surface, when one exists).</summary>
        public static void BindGameplay(IGameplayCommandBridge bridge)
        {
            instance._gameplay = bridge ?? throw new ArgumentNullException(nameof(bridge));
            instance.BindingsChanged?.Invoke();
        }

        /// <summary>
        /// Registers the running world (a GameplayWorld or NarrativeWorld) for the default reflection bridge; boot code
        /// calls it after booting and passes null on shutdown.
        /// </summary>
        public static void RegisterRunningWorld(object? world)
        {
            instance._runningWorld = world;
            if (instance._gameplay is ReflectionGameplayBridge reflection)
            {
                reflection.Reset();
            }
        }

        /// <summary>The bound selection bridge, or the default one over UnityEditor.Selection for <paramref name="runtime"/>.</summary>
        public IStudioSelectionBridge SelectionFor(StudioRuntime runtime)
        {
            if (_selection != null && (_selectionBound || ReferenceEquals(_selectionRuntime, runtime)))
            {
                return _selection;
            }

            if (_selection is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _selection = new EditorSelectionBridge(runtime);
            _selectionRuntime = runtime;
            return _selection;
        }

        /// <summary>The bound gameplay bridge, or the default reflection bridge.</summary>
        public IGameplayCommandBridge Gameplay => _gameplay ??= new ReflectionGameplayBridge(() => _runningWorld);

        private void OnDisable()
        {
            if (_selection is IDisposable disposable && !_selectionBound)
            {
                disposable.Dispose();
            }

            _selection = null;
        }

        [InitializeOnLoadMethod]
        private static void ResetOnPlayModeChange()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                {
                    instance._runningWorld = null;
                    if (instance._gameplay is ReflectionGameplayBridge reflection)
                    {
                        reflection.Reset();
                    }
                }
            };
        }
    }
}
