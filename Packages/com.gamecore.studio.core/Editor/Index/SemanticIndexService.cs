// GameCore.Studio.Edit - the semantic index (docs/studio/03-authoring-contracts.md s3, 02 boundary B).
//
// A projection of the project's authored content, rebuilt incrementally per source (one asset, prefab, scene or the
// prefab stage). Triggers (AssetPostprocessor, scene events, Undo.postprocessModifications; see StudioIndexTriggers)
// only mark sources dirty; the work happens lazily on the next read, so editing never waits on the index. Each
// change batch increments the revision. Nodes carry the [Authorable] type id as `type` (P0.3 finding 2), field values,
// [AuthorRef] references (also emitted as `references` edges), capabilities and provenance; `contains` edges come
// from the hierarchy (nearest authored ancestor) and from regions (a scene object providing `world.region` contains
// the scene's top-level authored objects). Contributors registered by plugins add edges and scopes of their own.
// The cache is Library/GameCoreStudio/index.json (the SemanticIndex shape) plus index.sources.json (per-source
// records for incremental reloads).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Collects extra edges and scopes from an <see cref="IIndexContributor"/>.</summary>
    public sealed class IndexContributionSink
    {
        internal readonly List<IndexEdge> Edges = new List<IndexEdge>();
        internal readonly List<ScopeEntry> Scopes = new List<ScopeEntry>();
        internal readonly Dictionary<string, List<IndexRef>> References = new Dictionary<string, List<IndexRef>>(StringComparer.Ordinal);

        public void AddReference(AuthoringRef from, AuthoringRef to, string field)
        {
            if (!References.TryGetValue(from.IdentityKey, out List<IndexRef>? refs)) References[from.IdentityKey] = refs = new List<IndexRef>();
            refs.Add(new IndexRef(field, to));
        }

        public void AddEdge(AuthoringRef from, AuthoringRef to, EdgeKind kind)
        {
            Edges.Add(new IndexEdge(SemanticIndexService.EdgeRef(from), SemanticIndexService.EdgeRef(to), kind));
        }

        public void AddScope(ScopeEntry scope)
        {
            Scopes.Add(scope ?? throw new ArgumentNullException(nameof(scope)));
        }
    }

    /// <summary>A plugin hook adding gameplay edges (spawns, bindsUi, triggers) and scope entries to the index.</summary>
    public interface IIndexContributor
    {
        void Contribute(AuthoredObjectEntry entry, IndexNode node, IndexContributionSink sink);
    }

    /// <summary>One reference to a target: the referring node and the field holding the reference.</summary>
    public sealed class IndexReference
    {
        public IndexReference(AuthoringRef from, string field, string fromType)
        {
            From = from;
            Field = field;
            FromType = fromType;
        }

        public AuthoringRef From { get; }

        public string Field { get; }

        public string FromType { get; }

        public override string ToString() => From + "." + Field;
    }

    /// <summary>One item affected by deleting (or changing) a target (SR-2.2).</summary>
    public sealed class ImpactItem
    {
        public ImpactItem(AuthoringRef target, string relation, int depth, AuthoringRef via, string? field)
        {
            Ref = target;
            Relation = relation;
            Depth = depth;
            Via = via;
            Field = field;
        }

        public AuthoringRef Ref { get; }

        /// <summary><c>references</c> (it points at the affected thing) or <c>contains</c> (it is inside the affected thing).</summary>
        public string Relation { get; }

        /// <summary>1 for direct impact, more for transitive impact.</summary>
        public int Depth { get; }

        /// <summary>The affected thing this item depends on.</summary>
        public AuthoringRef Via { get; }

        /// <summary>The referring field for a <c>references</c> item.</summary>
        public string? Field { get; }
    }

    /// <summary>What deleting a target would affect: referrers, contents and, transitively, their referrers.</summary>
    public sealed class ImpactReport
    {
        public ImpactReport(AuthoringRef target, IReadOnlyList<ImpactItem> items)
        {
            Target = target;
            Items = items;
        }

        public AuthoringRef Target { get; }

        public IReadOnlyList<ImpactItem> Items { get; }

        public bool IsEmpty => Items.Count == 0;
    }

    /// <summary>A bounded slice of the index for an agent request (03 s3).</summary>
    public sealed class IndexSlice
    {
        public IndexSlice(SemanticIndex index, bool truncated, int bytes, int omittedNodes)
        {
            Index = index;
            Truncated = truncated;
            Bytes = bytes;
            OmittedNodes = omittedNodes;
        }

        public SemanticIndex Index { get; }

        /// <summary>True when the byte cap cut nodes off (reported explicitly, never silently).</summary>
        public bool Truncated { get; }

        /// <summary>Serialized size of the slice (compact JSON, UTF-8).</summary>
        public int Bytes { get; }

        public int OmittedNodes { get; }
    }

    /// <summary>The incremental semantic index of one project.</summary>
    public sealed class SemanticIndexService : IAuthoringLookup
    {
        /// <summary>The companion's slice cap (03 s3).</summary>
        public const int DefaultByteCap = 2 * 1024 * 1024;

        private readonly StudioPaths _paths;
        private readonly IAuthoringSource _source;
        private readonly AuthoringRefResolver _resolver;
        private readonly IStudioLog _log;
        private readonly Dictionary<string, SourceRecord> _sources = new Dictionary<string, SourceRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> _dirtyAssets = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Scene> _dirtyScenes = new List<Scene>();
        private readonly HashSet<string> _removedSources = new HashSet<string>(StringComparer.Ordinal);
        private bool _stageDirty;
        private bool _built;
        private Maps? _maps;

        public SemanticIndexService(StudioPaths paths, IAuthoringSource source, AuthoringRefResolver resolver, IStudioLog log, AuthoringSourceScope scope = AuthoringSourceScope.All)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            Scope = scope;
        }

        /// <summary>What a full rebuild enumerates.</summary>
        public AuthoringSourceScope Scope { get; }

        /// <summary>The revision counter; increments with every change batch.</summary>
        public long Revision { get; private set; }

        public bool IsBuilt => _built;

        /// <summary>Plugin contributors (edges such as spawns/bindsUi/triggers, scope entries).</summary>
        public List<IIndexContributor> Contributors { get; } = new List<IIndexContributor>();

        /// <summary>Raised after a change batch with the new revision.</summary>
        public event Action<long>? Changed;

        /// <summary>True when sources are waiting to be re-projected.</summary>
        public bool HasPendingChanges => _dirtyAssets.Count > 0 || _dirtyScenes.Count > 0 || _removedSources.Count > 0 || _stageDirty;

        /// <summary>Projects every source again (one revision step).</summary>
        public void Rebuild()
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            _sources.Clear();
            _dirtyAssets.Clear();
            _dirtyScenes.Clear();
            _removedSources.Clear();
            _stageDirty = false;
            Dictionary<string, List<AuthoredObjectEntry>> grouped = new Dictionary<string, List<AuthoredObjectEntry>>(StringComparer.Ordinal);
            foreach (AuthoredObjectEntry entry in _source.Enumerate(Scope))
            {
                if (!grouped.TryGetValue(entry.SourceKey, out List<AuthoredObjectEntry>? list))
                {
                    list = new List<AuthoredObjectEntry>();
                    grouped.Add(entry.SourceKey, list);
                }

                list.Add(entry);
            }

            foreach (KeyValuePair<string, List<AuthoredObjectEntry>> pair in grouped)
            {
                _sources[pair.Key] = BuildSource(pair.Key, pair.Value);
            }

            _built = true;
            _maps = null;
            Revision++;
            _log.Write(StudioLogLevel.Debug, "index", "rebuilt " + _sources.Count + " source(s) in " + watch.ElapsedMilliseconds + " ms, revision " + Revision);
            Changed?.Invoke(Revision);
        }

        /// <summary>Asset import/delete/move notifications (from the AssetPostprocessor).</summary>
        public void MarkAssetsChanged(IEnumerable<string>? imported, IEnumerable<string>? deleted = null, IEnumerable<string>? moved = null, IEnumerable<string>? movedFrom = null)
        {
            foreach (string path in imported ?? Array.Empty<string>())
            {
                if (IsIndexedAsset(path))
                {
                    _dirtyAssets.Add(path);
                }
            }

            foreach (string path in deleted ?? Array.Empty<string>())
            {
                _removedSources.Add(AuthoringSourceKeys.ForAsset(path));
                _dirtyAssets.Remove(path);
            }

            foreach (string path in movedFrom ?? Array.Empty<string>())
            {
                _removedSources.Add(AuthoringSourceKeys.ForAsset(path));
            }

            foreach (string path in moved ?? Array.Empty<string>())
            {
                if (IsIndexedAsset(path))
                {
                    _dirtyAssets.Add(path);
                }
            }
        }

        /// <summary>A scene was opened, saved or edited.</summary>
        public void MarkSceneChanged(Scene scene)
        {
            if (!scene.IsValid())
            {
                return;
            }

            foreach (Scene existing in _dirtyScenes)
            {
                if (existing == scene)
                {
                    return;
                }
            }

            _dirtyScenes.Add(scene);
        }

        /// <summary>A scene was closed: its nodes leave the index.</summary>
        public void MarkSceneClosed(Scene scene)
        {
            _removedSources.Add(_source.SceneKey(scene));
            _dirtyScenes.RemoveAll(existing => existing == scene);
        }

        /// <summary>The prefab stage opened, closed or changed.</summary>
        public void MarkPrefabStageChanged()
        {
            _stageDirty = true;
        }

        /// <summary>An object was modified (Undo.postprocessModifications, the edit engine).</summary>
        public void MarkObjectChanged(UnityEngine.Object? target)
        {
            if (target == null)
            {
                return;
            }

            if (EditorUtility.IsPersistent(target))
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path))
                {
                    _dirtyAssets.Add(path);
                }

                return;
            }

            GameObject? gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null)
            {
                return;
            }

            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                _stageDirty = true;
                return;
            }

            MarkSceneChanged(gameObject.scene);
        }

        /// <summary>Re-projects dirty sources; one revision step when anything changed. Builds first when needed.</summary>
        public void Flush()
        {
            if (!_built)
            {
                if (!LoadCache())
                {
                    Rebuild();
                    return;
                }
            }

            if (!HasPendingChanges)
            {
                return;
            }

            foreach (string key in _removedSources)
            {
                _sources.Remove(key);
            }

            _removedSources.Clear();
            foreach (string path in _dirtyAssets)
            {
                string key = AuthoringSourceKeys.ForAsset(path);
                List<AuthoredObjectEntry> entries = new List<AuthoredObjectEntry>(_source.EnumerateAsset(path));
                if (entries.Count == 0)
                {
                    _sources.Remove(key);
                }
                else
                {
                    _sources[key] = BuildSource(key, entries);
                }
            }

            _dirtyAssets.Clear();
            foreach (Scene scene in _dirtyScenes)
            {
                string key = _source.SceneKey(scene);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    _sources.Remove(key);
                    continue;
                }

                List<AuthoredObjectEntry> entries = new List<AuthoredObjectEntry>(_source.EnumerateScene(scene));
                if (entries.Count == 0)
                {
                    _sources.Remove(key);
                }
                else
                {
                    _sources[key] = BuildSource(key, entries);
                }
            }

            _dirtyScenes.Clear();
            if (_stageDirty)
            {
                List<string> stageKeys = new List<string>();
                foreach (string key in _sources.Keys)
                {
                    if (key.StartsWith(AuthoringSourceKeys.StagePrefix, StringComparison.Ordinal))
                    {
                        stageKeys.Add(key);
                    }
                }

                foreach (string key in stageKeys)
                {
                    _sources.Remove(key);
                }

                Dictionary<string, List<AuthoredObjectEntry>> stage = new Dictionary<string, List<AuthoredObjectEntry>>(StringComparer.Ordinal);
                foreach (AuthoredObjectEntry entry in _source.Enumerate(AuthoringSourceScope.PrefabStage))
                {
                    if (!stage.TryGetValue(entry.SourceKey, out List<AuthoredObjectEntry>? list))
                    {
                        list = new List<AuthoredObjectEntry>();
                        stage.Add(entry.SourceKey, list);
                    }

                    list.Add(entry);
                }

                foreach (KeyValuePair<string, List<AuthoredObjectEntry>> pair in stage)
                {
                    _sources[pair.Key] = BuildSource(pair.Key, pair.Value);
                }

                _stageDirty = false;
            }

            _maps = null;
            Revision++;
            Changed?.Invoke(Revision);
        }

        /// <summary>The current index (flushes pending changes first).</summary>
        public SemanticIndex Snapshot()
        {
            Flush();
            Maps maps = CurrentMaps();
            return new SemanticIndex(Revision, _paths.ProjectName, maps.Nodes, maps.Edges, maps.Scopes);
        }

        /// <summary>The node naming the same authored thing as <paramref name="target"/>, or null.</summary>
        public IndexNode? FindNode(AuthoringRef target)
        {
            Flush();
            Maps maps = CurrentMaps();
            if (maps.ByKey.TryGetValue(target.IdentityKey, out IndexNode? node))
            {
                return node;
            }

            foreach (IndexNode candidate in maps.Nodes)
            {
                if (candidate.Ref.SameTarget(target))
                {
                    return candidate;
                }
            }

            return null;
        }

        AuthoringRef? IAuthoringLookup.FindByAuthoringId(string authoringId)
        {
            if (!_built)
            {
                return null;
            }

            return CurrentMaps().ByAuthoringId.TryGetValue(authoringId, out IndexNode? node) ? node.Ref : null;
        }

        AuthoringRef? IAuthoringLookup.FindByDefinition(string definition)
        {
            if (!_built)
            {
                return null;
            }

            return CurrentMaps().ByDefinition.TryGetValue(definition, out IndexNode? node) ? node.Ref : null;
        }

        /// <summary>Every node that references <paramref name="target"/> in an [AuthorRef] field.</summary>
        public IReadOnlyList<IndexReference> ReferencesTo(AuthoringRef target)
        {
            Flush();
            List<IndexReference> references = new List<IndexReference>();
            foreach (IndexNode node in CurrentMaps().Nodes)
            {
                if (node.Refs == null)
                {
                    continue;
                }

                foreach (IndexRef reference in node.Refs)
                {
                    if (Matches(reference.To, target))
                    {
                        references.Add(new IndexReference(node.Ref, reference.Field, node.Type));
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// Everything deleting <paramref name="target"/> affects (SR-2.2): its referrers, its contents (contains edges)
        /// and, transitively, the referrers of its contents and of its referrers' contents.
        /// </summary>
        public ImpactReport ImpactOf(AuthoringRef target, int maxDepth = 8)
        {
            Flush();
            Maps maps = CurrentMaps();
            List<ImpactItem> items = new List<ImpactItem>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { KeyOf(target) };
            Queue<KeyValuePair<AuthoringRef, int>> queue = new Queue<KeyValuePair<AuthoringRef, int>>();
            queue.Enqueue(new KeyValuePair<AuthoringRef, int>(target, 0));
            while (queue.Count > 0)
            {
                KeyValuePair<AuthoringRef, int> current = queue.Dequeue();
                if (current.Value >= maxDepth)
                {
                    continue;
                }

                foreach (IndexNode node in maps.Nodes)
                {
                    if (node.Refs != null)
                    {
                        foreach (IndexRef reference in node.Refs)
                        {
                            if (Matches(reference.To, current.Key) && visited.Add(node.Ref.IdentityKey))
                            {
                                items.Add(new ImpactItem(node.Ref, "references", current.Value + 1, current.Key, reference.Field));
                                queue.Enqueue(new KeyValuePair<AuthoringRef, int>(node.Ref, current.Value + 1));
                            }
                        }
                    }
                }

                foreach (IndexEdge edge in maps.Edges)
                {
                    if (edge.Kind == EdgeKind.Contains && Matches(edge.From, current.Key) && visited.Add(KeyOf(edge.To)))
                    {
                        AuthoringRef child = maps.ByKey.TryGetValue(edge.To.IdentityKey, out IndexNode? childNode) ? childNode.Ref : edge.To;
                        items.Add(new ImpactItem(child, "contains", current.Value + 1, current.Key, null));
                        queue.Enqueue(new KeyValuePair<AuthoringRef, int>(child, current.Value + 1));
                    }
                }
            }

            return new ImpactReport(target, items);
        }

        /// <summary>
        /// The selection closure to <paramref name="depth"/> over every edge (both directions), plus nodes of the
        /// requested types, cut at <paramref name="byteCap"/> with an explicit truncation flag (03 s3).
        /// </summary>
        public IndexSlice Slice(IReadOnlyList<AuthoringRef> selection, int depth = 2, int byteCap = DefaultByteCap, IEnumerable<string>? includeTypes = null)
        {
            Flush();
            Maps maps = CurrentMaps();
            List<string> order = new List<string>();
            HashSet<string> included = new HashSet<string>(StringComparer.Ordinal);
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            foreach (AuthoringRef target in selection ?? Array.Empty<AuthoringRef>())
            {
                IndexNode? node = FindNodeIn(maps, target);
                if (node != null && included.Add(node.Ref.IdentityKey))
                {
                    order.Add(node.Ref.IdentityKey);
                    queue.Enqueue(new KeyValuePair<string, int>(node.Ref.IdentityKey, 0));
                }
            }

            while (queue.Count > 0)
            {
                KeyValuePair<string, int> current = queue.Dequeue();
                if (current.Value >= depth)
                {
                    continue;
                }

                if (!maps.Neighbours.TryGetValue(current.Key, out List<string>? neighbours))
                {
                    continue;
                }

                foreach (string neighbour in neighbours)
                {
                    if (maps.ByKey.ContainsKey(neighbour) && included.Add(neighbour))
                    {
                        order.Add(neighbour);
                        queue.Enqueue(new KeyValuePair<string, int>(neighbour, current.Value + 1));
                    }
                }
            }

            if (includeTypes != null)
            {
                HashSet<string> types = new HashSet<string>(includeTypes, StringComparer.Ordinal);
                foreach (IndexNode node in maps.Nodes)
                {
                    if (types.Contains(node.Type) && included.Add(node.Ref.IdentityKey))
                    {
                        order.Add(node.Ref.IdentityKey);
                    }
                }
            }

            int bytes = Utf8Length(StudioJson.Serialize(new SemanticIndex(Revision, _paths.ProjectName, Array.Empty<IndexNode>()), false));
            List<IndexNode> nodes = new List<IndexNode>();
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
            bool truncated = false;
            int omitted = 0;
            foreach (string key in order)
            {
                IndexNode node = maps.ByKey[key];
                int size = Utf8Length(StudioJson.Serialize(node, false)) + 1;
                if (truncated || bytes + size > byteCap)
                {
                    truncated = true;
                    omitted++;
                    continue;
                }

                bytes += size;
                nodes.Add(node);
                kept.Add(key);
            }

            List<IndexEdge> edges = new List<IndexEdge>();
            foreach (IndexEdge edge in maps.Edges)
            {
                if (!kept.Contains(edge.From.IdentityKey) || !kept.Contains(edge.To.IdentityKey))
                {
                    continue;
                }

                int size = Utf8Length(StudioJson.Serialize(edge, false)) + 1;
                if (bytes + size > byteCap)
                {
                    truncated = true;
                    continue;
                }

                bytes += size;
                edges.Add(edge);
            }

            List<ScopeEntry> scopes = new List<ScopeEntry>();
            foreach (ScopeEntry scope in maps.Scopes)
            {
                if (scope.Region == null || kept.Contains(scope.Region.IdentityKey))
                {
                    int size = Utf8Length(StudioJson.Serialize(scope, false)) + 1;
                    if (bytes + size > byteCap)
                    {
                        truncated = true;
                        continue;
                    }

                    bytes += size;
                    scopes.Add(scope);
                }
            }

            SemanticIndex slice = new SemanticIndex(Revision, _paths.ProjectName, nodes, edges.Count == 0 ? null : edges, scopes.Count == 0 ? null : scopes);
            return new IndexSlice(slice, truncated, bytes, omitted);
        }

        /// <summary>Writes the full index (the SemanticIndex JSON shape) to <paramref name="path"/>.</summary>
        public void Export(string path)
        {
            StudioPaths.WriteAllTextAtomic(path, StudioJson.Serialize(Snapshot()));
        }

        /// <summary>Writes the cache files under Library/GameCoreStudio.</summary>
        public void SaveCache()
        {
            Flush();
            SourceCache cache = new SourceCache { Revision = Revision, Project = _paths.ProjectName };
            List<string> keys = new List<string>(_sources.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                SourceRecord record = _sources[key];
                cache.Sources.Add(new SourceCacheEntry { Key = key, Nodes = record.Nodes, Edges = record.Edges, Scopes = record.Scopes });
            }

            StudioPaths.WriteAllTextAtomic(_paths.IndexSourcesPath, StudioJson.Serialize(cache));
            Export(_paths.IndexCachePath);
        }

        /// <summary>Restores the index from the cache (open scenes are re-projected on the next read). False when absent or unreadable.</summary>
        public bool LoadCache()
        {
            if (!File.Exists(_paths.IndexSourcesPath))
            {
                return false;
            }

            SourceCache? cache;
            try
            {
                cache = JsonConvert.DeserializeObject<SourceCache>(File.ReadAllText(_paths.IndexSourcesPath), StudioJson.CreateSettings());
            }
            catch (Exception error) when (error is JsonException || error is IOException || error is ArgumentException)
            {
                _log.Write(StudioLogLevel.Warning, "index", "index cache unreadable, rebuilding: " + error.Message);
                return false;
            }

            if (cache == null || cache.Schema != "gamecore.studio.indexsources/2" || !string.Equals(cache.Project, _paths.ProjectName, StringComparison.Ordinal))
            {
                return false;
            }

            _sources.Clear();
            foreach (SourceCacheEntry entry in cache.Sources)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                if (entry.Key.StartsWith(AuthoringSourceKeys.ScenePrefix, StringComparison.Ordinal) || entry.Key.StartsWith(AuthoringSourceKeys.StagePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                _sources[entry.Key] = new SourceRecord(entry.Nodes ?? new List<IndexNode>(), entry.Edges ?? new List<IndexEdge>(), entry.Scopes ?? new List<ScopeEntry>());
            }

            Revision = cache.Revision;
            _built = true;
            _maps = null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && (Scope & AuthoringSourceScope.OpenScenes) != 0)
                {
                    MarkSceneChanged(scene);
                }
            }

            if ((Scope & AuthoringSourceScope.PrefabStage) != 0)
            {
                _stageDirty = true;
            }

            return true;
        }

        /// <summary>A ref used as an edge endpoint: no stamp, no scope.</summary>
        public static AuthoringRef EdgeRef(AuthoringRef reference)
        {
            return reference.WithStamp(null).WithScope(null);
        }

        private static bool IsIndexedAsset(string path)
        {
            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);

        private static string KeyOf(AuthoringRef reference) => reference.IdentityKey;

        private static bool Matches(AuthoringRef candidate, AuthoringRef target)
        {
            return string.Equals(candidate.IdentityKey, target.IdentityKey, StringComparison.Ordinal) || candidate.SameTarget(target)
                || (candidate.Global != null && string.Equals(candidate.Global, target.Global, StringComparison.Ordinal));
        }

        private static IndexNode? FindNodeIn(Maps maps, AuthoringRef target)
        {
            if (maps.ByKey.TryGetValue(target.IdentityKey, out IndexNode? node))
            {
                return node;
            }

            foreach (IndexNode candidate in maps.Nodes)
            {
                if (Matches(candidate.Ref, target))
                {
                    return candidate;
                }
            }

            return null;
        }

        private SourceRecord BuildSource(string key, List<AuthoredObjectEntry> entries)
        {
            List<IndexNode> nodes = new List<IndexNode>();
            List<IndexEdge> edges = new List<IndexEdge>();
            IndexContributionSink sink = new IndexContributionSink();
            Dictionary<UnityEngine.Object, AuthoringRef> refs = new Dictionary<UnityEngine.Object, AuthoringRef>();
            List<KeyValuePair<AuthoredObjectEntry, IndexNode>> built = new List<KeyValuePair<AuthoredObjectEntry, IndexNode>>();
            foreach (AuthoredObjectEntry entry in entries)
            {
                IndexNode? node;
                try
                {
                    node = BuildNode(entry, edges);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    _log.Write(StudioLogLevel.Warning, "index", "could not project " + entry.Target.name + " (" + key + "): " + error.Message);
                    continue;
                }

                if (node == null)
                {
                    continue;
                }

                nodes.Add(node);
                refs[entry.Target] = EdgeRef(node.Ref);
                built.Add(new KeyValuePair<AuthoredObjectEntry, IndexNode>(entry, node));
            }

            AuthoringRef? region = null;
            foreach (KeyValuePair<AuthoredObjectEntry, IndexNode> pair in built)
            {
                if (pair.Key.Location == AuthoredObjectLocation.Scene && pair.Value.Provides("world.region"))
                {
                    region = EdgeRef(pair.Value.Ref);
                    break;
                }
            }

            foreach (KeyValuePair<AuthoredObjectEntry, IndexNode> pair in built)
            {
                if (!(pair.Key.Target is Component component))
                {
                    continue;
                }

                AuthoringRef self = refs[pair.Key.Target];
                MonoBehaviour? parent = component.transform.parent == null ? null : _resolver.Identity.FindLogicalOwner(component.transform.parent);
                if (parent != null && refs.TryGetValue(parent, out AuthoringRef? parentRef))
                {
                    edges.Add(new IndexEdge(parentRef, self, EdgeKind.Contains));
                }
                else if (region != null && !string.Equals(region.IdentityKey, self.IdentityKey, StringComparison.Ordinal))
                {
                    edges.Add(new IndexEdge(region, self, EdgeKind.Contains));
                }

                foreach (IIndexContributor contributor in Contributors)
                {
                    contributor.Contribute(pair.Key, pair.Value, sink);
                }
            }

            foreach (KeyValuePair<AuthoredObjectEntry, IndexNode> pair in built)
            {
                if (!(pair.Key.Target is Component))
                {
                    foreach (IIndexContributor contributor in Contributors)
                    {
                        contributor.Contribute(pair.Key, pair.Value, sink);
                    }
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                IndexNode node = nodes[i];
                if (!sink.References.TryGetValue(EdgeRef(node.Ref).IdentityKey, out List<IndexRef>? added)) continue;
                List<IndexRef> all = new List<IndexRef>(node.Refs ?? Array.Empty<IndexRef>());
                all.AddRange(added);
                nodes[i] = new IndexNode(node.Ref, node.Type, node.Name, node.Fields, all, node.Capabilities, node.Provenance);
            }
            edges.AddRange(sink.Edges);
            return new SourceRecord(nodes, edges, sink.Scopes);
        }

        private IndexNode? BuildNode(AuthoredObjectEntry entry, List<IndexEdge> edges)
        {
            AuthorScope scope = entry.Location == AuthoredObjectLocation.Asset ? AuthorScope.Definition
                : (entry.Location == AuthoredObjectLocation.Prefab ? AuthorScope.Prefab : AuthorScope.Instance);
            AuthoringRef? reference = _resolver.BuildRef(entry.Target, scope, true);
            if (reference == null)
            {
                return null;
            }

            AuthoringRef from = EdgeRef(reference);
            SortedDictionary<string, IndexField> fields = new SortedDictionary<string, IndexField>(StringComparer.Ordinal);
            List<IndexRef> outgoing = new List<IndexRef>();
            foreach (AuthorMemberInfo member in entry.Type.Members)
            {
                object? value = member.GetValue(entry.Target);
                if (member.IsReference)
                {
                    IReadOnlyList<object?> targets = member.IsCollection ? AuthoringIdentity.Items(value) : new[] { value };
                    foreach (object? item in targets)
                    {
                        if (!(item is UnityEngine.Object target) || target == null)
                        {
                            continue;
                        }

                        AuthoringRef? to = _resolver.BuildRef(target, null, false);
                        if (to == null)
                        {
                            continue;
                        }

                        outgoing.Add(new IndexRef(member.Name, to));
                        edges.Add(new IndexEdge(from, to, EdgeKind.References));
                    }

                    continue;
                }

                FieldSpec spec = member.Spec;
                IReadOnlyList<double>? range = spec.Min.HasValue && spec.Max.HasValue ? new[] { spec.Min.Value, spec.Max.Value } : null;
                fields[member.Name] = new IndexField(spec.Type, _resolver.Codec.FromClr(value), spec.Unit, range);
            }

            string name = entry.Target is Component component ? component.gameObject.name : entry.Target.name;
            return new IndexNode(
                reference,
                entry.Type.TypeId,
                name,
                fields.Count == 0 ? null : fields,
                outgoing.Count == 0 ? null : outgoing,
                _resolver.Identity.GetCapabilities(entry.Target),
                new Provenance(entry.AssetPath));
        }

        private Maps CurrentMaps()
        {
            if (_maps != null)
            {
                return _maps;
            }

            Maps maps = new Maps();
            List<string> keys = new List<string>(_sources.Keys);
            keys.Sort(StringComparer.Ordinal);
            SortedDictionary<string, IndexNode> nodes = new SortedDictionary<string, IndexNode>(StringComparer.Ordinal);
            List<IndexEdge> edges = new List<IndexEdge>();
            foreach (string key in keys)
            {
                SourceRecord record = _sources[key];
                foreach (IndexNode node in record.Nodes)
                {
                    if (!nodes.ContainsKey(node.Ref.IdentityKey))
                    {
                        nodes.Add(node.Ref.IdentityKey, node);
                    }
                }

                edges.AddRange(record.Edges);
                maps.Scopes.AddRange(record.Scopes);
            }

            edges.Sort((left, right) =>
            {
                int compare = string.CompareOrdinal(left.From.IdentityKey, right.From.IdentityKey);
                if (compare == 0)
                {
                    compare = string.CompareOrdinal(left.To.IdentityKey, right.To.IdentityKey);
                }

                return compare != 0 ? compare : left.Kind.CompareTo(right.Kind);
            });
            maps.Scopes.Sort((left, right) => string.CompareOrdinal(left.Scope, right.Scope));
            foreach (KeyValuePair<string, IndexNode> pair in nodes)
            {
                maps.Nodes.Add(pair.Value);
                maps.ByKey[pair.Key] = pair.Value;
                if (pair.Value.Ref.AuthoringId != null && !maps.ByAuthoringId.ContainsKey(pair.Value.Ref.AuthoringId))
                {
                    maps.ByAuthoringId.Add(pair.Value.Ref.AuthoringId, pair.Value);
                }

                if (pair.Value.Ref.Definition != null && !maps.ByDefinition.ContainsKey(pair.Value.Ref.Definition))
                {
                    maps.ByDefinition.Add(pair.Value.Ref.Definition, pair.Value);
                }
            }

                AuthoringRef ResolveEndpoint(AuthoringRef endpoint)
                {
                    if (endpoint.AuthoringId != null && maps.ByAuthoringId.TryGetValue(endpoint.AuthoringId, out IndexNode? byId)) return EdgeRef(byId.Ref);
                    if (endpoint.Path != null && endpoint.Path.StartsWith("byname:", StringComparison.Ordinal))
                    {
                        string[] parts = endpoint.Path.Substring(7).Split(new[] { ':' }, 2);
                        if (parts.Length == 2)
                            foreach (IndexNode node in maps.Nodes)
                                if (node.Provides(parts[0]))
                                {
                                    if (node.Name == parts[1] || node.Ref.AuthoringId == parts[1] || node.Ref.Definition == parts[1] || node.Ref.Definition?.StartsWith(parts[1] + "@", StringComparison.Ordinal) == true) return EdgeRef(node.Ref);
                                    foreach (IndexField field in node.Fields?.Values ?? Array.Empty<IndexField>())
                                        if (field.Value?.Type == JTokenType.String && field.Value.Value<string>() == parts[1]) return EdgeRef(node.Ref);
                                }
                    }
                    return endpoint;
                }
            for (int i = 0; i < maps.Nodes.Count; i++)
            {
                IndexNode node = maps.Nodes[i];
                if (node.Refs == null) continue;
                List<IndexRef> resolved = new List<IndexRef>();
                foreach (IndexRef reference in node.Refs) resolved.Add(new IndexRef(reference.Field, ResolveEndpoint(reference.To)));
                IndexNode updated = new IndexNode(node.Ref, node.Type, node.Name, node.Fields, resolved, node.Capabilities, node.Provenance);
                maps.Nodes[i] = updated;
                maps.ByKey[node.Ref.IdentityKey] = updated;
            }
            string? previousKey = null;
            foreach (IndexEdge rawEdge in edges)
            {
                IndexEdge edge = new IndexEdge(ResolveEndpoint(rawEdge.From), ResolveEndpoint(rawEdge.To), rawEdge.Kind);
                string edgeKey = edge.From.IdentityKey + ">" + edge.To.IdentityKey + ">" + edge.Kind.ToString();
                if (string.Equals(edgeKey, previousKey, StringComparison.Ordinal))
                {
                    continue;
                }

                previousKey = edgeKey;
                maps.Edges.Add(edge);
                AddNeighbour(maps, edge.From.IdentityKey, edge.To.IdentityKey);
                AddNeighbour(maps, edge.To.IdentityKey, edge.From.IdentityKey);
            }

            _maps = maps;
            return maps;
        }

        private static void AddNeighbour(Maps maps, string from, string to)
        {
            if (!maps.Neighbours.TryGetValue(from, out List<string>? list))
            {
                list = new List<string>();
                maps.Neighbours.Add(from, list);
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }

        private sealed class SourceRecord
        {
            public SourceRecord(List<IndexNode> nodes, List<IndexEdge> edges, List<ScopeEntry> scopes)
            {
                Nodes = nodes;
                Edges = edges;
                Scopes = scopes;
            }

            public List<IndexNode> Nodes { get; }

            public List<IndexEdge> Edges { get; }

            public List<ScopeEntry> Scopes { get; }
        }

        private sealed class Maps
        {
            public readonly List<IndexNode> Nodes = new List<IndexNode>();
            public readonly List<IndexEdge> Edges = new List<IndexEdge>();
            public readonly List<ScopeEntry> Scopes = new List<ScopeEntry>();
            public readonly Dictionary<string, IndexNode> ByKey = new Dictionary<string, IndexNode>(StringComparer.Ordinal);
            public readonly Dictionary<string, IndexNode> ByAuthoringId = new Dictionary<string, IndexNode>(StringComparer.Ordinal);
            public readonly Dictionary<string, IndexNode> ByDefinition = new Dictionary<string, IndexNode>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<string>> Neighbours = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class SourceCache
        {
            [JsonProperty("schema")]
            public string Schema = "gamecore.studio.indexsources/2";

            [JsonProperty("project")]
            public string Project = string.Empty;

            [JsonProperty("revision")]
            public long Revision;

            [JsonProperty("sources")]
            public List<SourceCacheEntry> Sources = new List<SourceCacheEntry>();
        }

        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class SourceCacheEntry
        {
            [JsonProperty("key")]
            public string? Key;

            [JsonProperty("nodes")]
            public List<IndexNode>? Nodes;

            [JsonProperty("edges")]
            public List<IndexEdge>? Edges;

            [JsonProperty("scopes")]
            public List<ScopeEntry>? Scopes;
        }
    }
}
