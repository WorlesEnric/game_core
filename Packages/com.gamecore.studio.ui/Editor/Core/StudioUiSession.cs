// GameCore.Studio.UI - owner of the project's Studio UI context (ScriptableSingleton: the only place the UI keeps
// session state besides StudioSelection, StudioTaskStore and StudioUiSettings). The context is rebuilt lazily after
// every domain reload over StudioServices.Runtime, the persisted selection and the persisted task rows, and then
// recovers the tray and pending candidates from the gateway.
#nullable enable
using System;
using GameCore.Studio.Edit;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The project's UI context holder.</summary>
    public sealed class StudioUiSession : ScriptableSingleton<StudioUiSession>
    {
        [SerializeField]
        private int contextsCreated;

        [NonSerialized]
        private StudioUiContext? _context;

        /// <summary>The project's context (created on first use).</summary>
        public static StudioUiContext Context => instance.GetOrCreate();

        public static bool HasContext => instance._context != null;

        /// <summary>The project's context when it exists (never creates one).</summary>
        public static StudioUiContext? ContextIfCreated => instance._context;

        /// <summary>Contexts created in this editor session (one per domain).</summary>
        public int ContextsCreated => contextsCreated;

        private StudioUiContext GetOrCreate()
        {
            if (_context != null && ReferenceEquals(_context.Runtime, StudioServices.Runtime))
            {
                return _context;
            }

            _context?.Dispose();
            StudioRuntime runtime = StudioServices.Runtime;
            SelectionModel selection = new SelectionModel(runtime);
            StudioSelection.instance.Bind(selection);
            selection.AttachUnityMirror();
            TaskLedger tasks = new TaskLedger(StudioTaskStore.instance);
            _context = new StudioUiContext(runtime, null, selection, tasks, true);
            _context.Candidates.GhostMaterial = () => StudioUiSettings.instance.GhostMaterial;
            contextsCreated++;
            StudioUiContext created = _context;
            EditorApplication.delayCall += () => _ = created.RecoverAsync();
            return _context;
        }

        private void OnDisable()
        {
            _context?.Dispose();
            _context = null;
        }
    }
}
