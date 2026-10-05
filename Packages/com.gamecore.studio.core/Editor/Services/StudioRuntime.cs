// GameCore.Studio.Edit - one Studio runtime: every boundary-C service wired together (02 s2 C).
// The project's runtime is owned by StudioServices (a ScriptableSingleton, rebuilt after each domain reload from the
// journal and the index cache). Tests create their own runtimes with a temporary state root, explicit type and tool
// sources and fakes for the live world and the agent gateway.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    /// <summary>How a runtime is built; every member is optional.</summary>
    public sealed class StudioRuntimeOptions
    {
        public StudioPaths? Paths { get; set; }

        public IStudioLog? Log { get; set; }

        /// <summary>Where [Authorable] types come from (default TypeCache).</summary>
        public Func<IEnumerable<Type>>? TypeSource { get; set; }

        /// <summary>Where [AuthorOperation] methods come from (default TypeCache).</summary>
        public Func<IEnumerable<MethodInfo>>? ToolMethodSource { get; set; }

        /// <summary>The authored-object source (default: open scenes, prefab stage, assets under <see cref="SearchFolders"/>).</summary>
        public IAuthoringSource? Source { get; set; }

        /// <summary>Asset folders the default source scans (default "Assets").</summary>
        public IReadOnlyList<string>? SearchFolders { get; set; }

        public AuthoringSourceScope IndexScope { get; set; } = AuthoringSourceScope.All;

        /// <summary>Region residency (default: all resident; P1.1's RegionStreamer adapter in the integrated game).</summary>
        public IResidencyQuery? Residency { get; set; }

        /// <summary>The running world (default: GameApplication.Current).</summary>
        public ILiveWorldGateway? Live { get; set; }

        public EngineOptions? Engine { get; set; }

        /// <summary>Restore the index from Library/GameCoreStudio when present (default true).</summary>
        public bool LoadIndexCache { get; set; } = true;
    }

    /// <summary>The services of one project (or one test).</summary>
    public sealed class StudioRuntime : IDisposable
    {
        private StudioRuntime(StudioRuntimeOptions options)
        {
            Paths = options.Paths ?? StudioPaths.ForCurrentProject();
            Log = options.Log ?? new UnityStudioLog();
            Identity = new AuthoringIdentity();
            Types = options.TypeSource != null ? new AuthorableTypeRegistry(Identity, options.TypeSource) : new AuthorableTypeRegistry(Identity);
            Source = options.Source ?? new DefaultAuthoringSource(Identity, Types, options.SearchFolders);
            Resolver = new AuthoringRefResolver(Identity, Source, options.Residency);
            Index = new SemanticIndexService(Paths, Source, Resolver, Log, options.IndexScope);
            Resolver.Lookup = Index;
            Artifacts = new ArtifactStore(Paths);
            Journal = new Journal(Paths);
            Services = new StudioServiceRegistry();
            Staging = new PreviewStaging();
            Live = options.Live ?? new GameApplicationLiveGateway();
            Registry = new ToolRegistry(this);
            if (options.ToolMethodSource != null)
            {
                Registry.MethodSource = options.ToolMethodSource;
            }

            Engine = new ChangeSetEngine(this, options.Engine);
            Queue = new ApplyQueue(() => Engine.IsApplying);
            History = new HistoryService(this);
            StageAdmission.Of(this);
            References = new NestedReferenceContributor(this);
            Index.Contributors.Add(References);
            if (options.LoadIndexCache)
            {
                Index.LoadCache();
            }
        }

        public StudioPaths Paths { get; }

        public IStudioLog Log { get; }

        public AuthoringIdentity Identity { get; }

        public AuthorableTypeRegistry Types { get; }

        public IAuthoringSource Source { get; }

        public AuthoringRefResolver Resolver { get; }

        public SemanticIndexService Index { get; }

        public NestedReferenceContributor References { get; }

        public ArtifactStore Artifacts { get; }

        public Journal Journal { get; }

        public StudioServiceRegistry Services { get; }

        public PreviewStaging Staging { get; }

        public ILiveWorldGateway Live { get; }

        public ToolRegistry Registry { get; }

        public ChangeSetEngine Engine { get; }

        public ApplyQueue Queue { get; }

        public HistoryService History { get; }

        public static StudioRuntime Create(StudioRuntimeOptions? options = null)
        {
            return new StudioRuntime(options ?? new StudioRuntimeOptions());
        }

        /// <summary>A change set of one operation (manual edits: inspector commits, gizmo drags).</summary>
        public static ChangeSet Single(string intent, IntentOrigin origin, Operation operation, ApplyPolicy? policy = null)
        {
            return new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(intent, origin), new[] { operation }, policy: policy);
        }

        /// <summary>Forgets types, tools and the catalog after a code change (rediscovered on next use).</summary>
        public void InvalidateCode()
        {
            Identity.Invalidate();
            Types.Invalidate();
            Registry.Invalidate();
        }

        public void Dispose()
        {
            Queue.Dispose();
            Staging.Dispose();
        }
    }
}
