// GameCore.Studio.Etos - main-thread index capture, serialized authenticated delta publication.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos
{
    /// <summary>Publishes the current project on session open, then revisions and committed history transitions.
    /// Capture is deferred until after engine notifications: Journal.Written precedes NotifyIndex on apply.</summary>
    public sealed class EtosIndexPublisher : IDisposable
    {
        private readonly CompanionClient _client;
        private readonly StudioRuntime _runtime;
        private readonly MainThreadQueue _queue;
        private readonly IStudioLog _log;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Stopwatch _sincePost = new Stopwatch();
        private Dictionary<string, JObject> _published = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private Dictionary<string, JObject>? _sendingNodes;
        private Task<IndexDeltaAck>? _sending;
        private long? _revision;
        private long _sendingRevision;
        private bool _started;
        private bool _dirty;
        private bool _disposed;
        private Exception? _failure;

        public EtosIndexPublisher(CompanionClient client, StudioRuntime runtime, MainThreadQueue queue, IStudioLog log)
        {
            _client = client;
            _runtime = runtime;
            _queue = queue;
            _log = log;
        }

        /// <summary>The last revision accepted by the companion, not an eventual RG delivery acknowledgement.</summary>
        public long? PublishedRevision => _revision;

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EtosIndexPublisher));
            if (_started) return;
            _started = true;
            _runtime.Index.Changed += OnIndexChanged;
            _runtime.Engine.Applied += OnApplied;
            _runtime.Journal.Written += OnWritten;
            _dirty = true;
            Tick();
        }

        private void OnIndexChanged(long revision) => _dirty = true;
        private void OnApplied(ApplyReport report) => _dirty = true;
        private void OnWritten(string id, ChangeSetState state)
        {
            if (state == ChangeSetState.Applied || state == ChangeSetState.Undone) _dirty = true;
        }

        /// <summary>Main-thread pump. Coalesces changes to at most one POST per second, with one in flight.</summary>
        public void Tick()
        {
            if (!_started || _disposed) return;
            if (_sending != null)
            {
                if (!_sending.IsCompleted) return;
                try
                {
                    IndexDeltaAck ack = _sending.GetAwaiter().GetResult();
                    if (ack.Revision != _sendingRevision) throw EtosException.Protocol("index_revision_mismatch");
                    _published = _sendingNodes!;
                    _revision = _sendingRevision;
                    _failure = null;
                }
                catch (Exception error) when (error is EtosException || error is OperationCanceledException)
                {
                    _failure = error;
                    _dirty = true;
                    _log.Write(StudioLogLevel.Warning, "etos.index", EtosRedaction.Redact("Index publication failed: " + error.Message));
                }
                finally
                {
                    _sending = null;
                    _sendingNodes = null;
                }
            }

            if (!_dirty || (_sincePost.IsRunning && _sincePost.Elapsed < TimeSpan.FromSeconds(1))) return;
            SemanticIndex snapshot = _runtime.Index.Snapshot();
            var current = new Dictionary<string, JObject>(snapshot.Nodes.Count, StringComparer.Ordinal);
            var changed = new JArray();
            var removals = new JArray();
            foreach (IndexNode node in snapshot.Nodes)
            {
                JObject json = (JObject)StudioJson.ToToken(node);
                string key = node.Ref.Kind + ":" + node.Ref.IdentityKey;
                current[key] = json;
                if (!_published.TryGetValue(key, out JObject? previous) || !JToken.DeepEquals(previous, json)) changed.Add(json);
            }
            foreach (KeyValuePair<string, JObject> previous in _published)
                if (!current.ContainsKey(previous.Key)) removals.Add(previous.Value["ref"]!.DeepClone());

            var delta = new JObject
            {
                ["project"] = snapshot.Project,
                ["revision"] = snapshot.Revision,
                ["nodes"] = changed,
                ["edges"] = StudioJson.ToToken(snapshot.Edges ?? Array.Empty<IndexEdge>()),
                ["removals"] = removals,
            };
            if (_revision.HasValue) delta["baseRevision"] = _revision.Value;
            _dirty = false;
            _sendingNodes = current;
            _sendingRevision = snapshot.Revision;
            _sincePost.Restart();
            _sending = _client.PostIndexDeltaAsync(delta, _stop.Token);
        }

        /// <summary>Drains lifecycle changes through the ordinary main-thread pump. Does not seed or rebuild the index.
        /// The caller must keep pumping the gateway queue, and poll RG separately for its coalesced delivery.</summary>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token))
            {
                while (true)
                {
                    bool done = false;
                    await _queue.Run(() =>
                    {
                        Tick();
                        if (_failure != null) throw _failure;
                        done = _started && !_dirty && _sending == null;
                    }, linked.Token).ConfigureAwait(false);
                    if (done) return;
                    await Task.Delay(20, linked.Token).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _runtime.Index.Changed -= OnIndexChanged;
            _runtime.Engine.Applied -= OnApplied;
            _runtime.Journal.Written -= OnWritten;
            _stop.Cancel();
            _stop.Dispose();
        }
    }
}
