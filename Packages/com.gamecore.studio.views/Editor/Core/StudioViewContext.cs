// GameCore.Studio.Views - what every view works against: the Studio runtime (index, engine, journal, history), the
// selection and gameplay bridges, the edit helper and the read-only tool invoker, and one change signal.
//
// Change propagation (SR-1.7 "all over the same content"): SemanticIndexService.Changed, ChangeSetEngine.Applied,
// Journal.Written and Play Mode transitions raise Changed once per editor update (coalesced), so a burst of edits costs
// one refresh. The index re-projects lazily; while a view is open the context flushes pending index changes at most
// twice a second so views follow edits made elsewhere (inspector, scene view, agents) without polling the assets.
#nullable enable
using System;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;

namespace GameCore.Studio.Views
{
    /// <summary>The services one view uses.</summary>
    public sealed class StudioViewContext : IDisposable
    {
        private const double FlushInterval = 0.5;

        private readonly bool _ownsUpdates;
        private IndexGraph? _graph;
        private bool _pending;
        private double _lastFlush;
        private bool _disposed;

        public StudioViewContext(StudioRuntime runtime, IStudioSelectionBridge selection, IGameplayCommandBridge gameplay, bool hookEditorUpdates = true)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            Gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
            Contributor = runtime.References;
            Edits = new ViewEdits(runtime);
            Tools = new ReadOnlyToolInvoker(runtime);
            runtime.Index.Changed += OnIndexChanged;
            runtime.Engine.Applied += OnApplied;
            runtime.Journal.Written += OnJournalWritten;
            _ownsUpdates = hookEditorUpdates;
            if (hookEditorUpdates)
            {
                EditorApplication.update += OnUpdate;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
            }
        }

        /// <summary>A context over the project's runtime and the session's bridges.</summary>
        public static StudioViewContext ForProject()
        {
            StudioRuntime runtime = StudioServices.Runtime;
            return new StudioViewContext(runtime, StudioViewsSession.instance.SelectionFor(runtime), StudioViewsSession.instance.Gameplay);
        }

        public StudioRuntime Runtime { get; }

        public IStudioSelectionBridge Selection { get; }

        public IGameplayCommandBridge Gameplay { get; }

        public NestedReferenceContributor Contributor { get; }

        public ViewEdits Edits { get; }

        public ReadOnlyToolInvoker Tools { get; }

        /// <summary>Raised (at most once per editor update) after the index, the journal or the play state changed.</summary>
        public event Action? Changed;

        /// <summary>Raised with each journaled apply (views show the report).</summary>
        public event Action<ApplyReport>? Applied;

        public bool IsPlaying => EditorApplication.isPlaying;

        /// <summary>The current index revision as a graph (flushes pending index changes first; cached by revision).</summary>
        public IndexGraph Graph()
        {
            if (_graph != null && !Runtime.Index.HasPendingChanges && _graph.Revision == Runtime.Index.Revision)
            {
                return _graph;
            }

            SemanticIndex snapshot = Runtime.Index.Snapshot();
            if (_graph == null || _graph.Revision != snapshot.Revision)
            {
                _graph = IndexGraph.Build(snapshot);
            }

            return _graph;
        }

        /// <summary>Raises <see cref="Changed"/> now (tests and explicit refresh buttons).</summary>
        public void RaiseChanged()
        {
            _pending = false;
            Changed?.Invoke();
        }

        /// <summary>Runs one coalesced update step: flushes the index when due and raises a pending change.</summary>
        public void Pump()
        {
            double now = EditorApplication.timeSinceStartup;
            if (Runtime.Index.HasPendingChanges && now - _lastFlush >= FlushInterval)
            {
                _lastFlush = now;
                Runtime.Index.Flush();
            }

            if (_pending)
            {
                RaiseChanged();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Runtime.Index.Changed -= OnIndexChanged;
            Runtime.Engine.Applied -= OnApplied;
            Runtime.Journal.Written -= OnJournalWritten;
            if (_ownsUpdates)
            {
                EditorApplication.update -= OnUpdate;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            }
        }

        private void OnUpdate() => Pump();

        private void OnIndexChanged(long revision) => _pending = true;

        private void OnApplied(ApplyReport report)
        {
            _pending = true;
            Applied?.Invoke(report);
        }

        private void OnJournalWritten(string id, ChangeSetState state) => _pending = true;

        private void OnPlayModeChanged(PlayModeStateChange change) => _pending = true;
    }
}
