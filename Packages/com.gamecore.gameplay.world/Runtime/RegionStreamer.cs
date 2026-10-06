// GameCore.Gameplay.World - RegionStreamer: additive region scenes driven by committed residency (P1.1).
//
// The streamer follows one focus traveller: the region its world.region slot names (or the start region when there is
// no focus) is wanted resident, plus its neighbours when preloading is on; every other region is wanted unloaded. Each
// tick (after the host pump, from the presentation frame) it walks every region through the residency state machine,
// one legal step at a time, and owns the scene operation that belongs to each step:
//
//   committed Unloaded,  wanted     -> submit world.setResidency(Loading)
//   committed Loading               -> start LoadSceneAsync (additive); when done: submit Resident, or - when no longer
//                                      wanted, or cancelled - unload the scene again and submit Unloaded
//   committed Resident,  not wanted -> submit Unloading (listeners suspend their views on the committed change)
//   committed Unloading             -> start UnloadSceneAsync; when done submit Unloaded
//
// world.setResidency is submitted with the application root's own issuer, the only issuer the world system accepts, so
// the committed residency slot always describes what the streamer actually did. Residency listeners (IResidencyAware,
// e.g. the view binder) are told of every committed change. The streamer is also the IResidencyQuery Studio reads.
// Cancellation (Cancel or the CancellationToken) stops all streaming: nothing new is loaded and loaded regions unload.
//
// P1.7a (A3): a streamer is created per attach, so its first tick after an attach - a fresh boot or a restored root -
// reconciles each region's committed residency with the scene the loader actually has (StreamingRules.Reconcile):
// committed Resident with no loaded scene loads the scene without moving the residency (a restored world's focus
// region), committed Unloaded with a loaded scene unloads it (the previous root's scenes). A failed load backs off
// (StreamingRules.BackoffFrames) and, after StreamingRules.MaxLoadFailures consecutive failures, latches that region with
// a GP-WLD-032 diagnostic instead of retrying every frame; ResetLatch re-arms it. In the Editor a region scene that is
// not in the build settings is reported once (GP-WLD-031): it streams in Play Mode but a player build cannot load it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.World
{
    /// <summary>One asynchronous scene operation.</summary>
    public interface ISceneOperation
    {
        bool IsDone { get; }

        bool Failed { get; }
    }

    /// <summary>Loads and unloads region scenes additively.</summary>
    public interface ISceneLoader
    {
        ISceneOperation Load(string scenePath);

        ISceneOperation Unload(string scenePath);

        bool IsLoaded(string scenePath);
    }

    /// <summary>A finished operation.</summary>
    public sealed class CompletedSceneOperation : ISceneOperation
    {
        public CompletedSceneOperation(bool failed)
        {
            Failed = failed;
        }

        public bool IsDone => true;

        public bool Failed { get; }
    }

    /// <summary>
    /// A loader that loads nothing and completes every operation at once: the residency machine runs without scenes
    /// (EditMode tests, headless simulation).
    /// </summary>
    public sealed class ImmediateSceneLoader : ISceneLoader
    {
        private readonly HashSet<string> loaded = new HashSet<string>(StringComparer.Ordinal);

        public int Loads { get; private set; }

        public int Unloads { get; private set; }

        public ISceneOperation Load(string scenePath)
        {
            Loads++;
            loaded.Add(scenePath);
            return new CompletedSceneOperation(false);
        }

        public ISceneOperation Unload(string scenePath)
        {
            Unloads++;
            loaded.Remove(scenePath);
            return new CompletedSceneOperation(false);
        }

        public bool IsLoaded(string scenePath) => loaded.Contains(scenePath);
    }

    /// <summary>
    /// Additive scene loading through Unity. In the Editor scenes load by path without a build-settings entry
    /// (EditorSceneManager.LoadSceneAsyncInPlayMode); a player loads them through SceneManager by path, which requires the
    /// region scenes in the build settings. A loaded region scene's authored entity proxies are deactivated: views come
    /// from the committed state, never from the proxies.
    /// </summary>
    public sealed class UnitySceneLoader : ISceneLoader
    {
        public ISceneOperation Load(string scenePath)
        {
#if UNITY_EDITOR
            AsyncOperation? operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            AsyncOperation? operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
#endif
            if (operation == null)
            {
                return new CompletedSceneOperation(true);
            }

            return new UnitySceneOperation(operation, scenePath, true);
        }

        public ISceneOperation Unload(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return new CompletedSceneOperation(false);
            }

            AsyncOperation? operation = SceneManager.UnloadSceneAsync(scene);
            return operation == null ? new CompletedSceneOperation(true) : new UnitySceneOperation(operation, scenePath, false);
        }

        public bool IsLoaded(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>Deactivates every authored entity proxy of a loaded region scene.</summary>
        public static int DeactivateProxies(Scene scene)
        {
            int count = 0;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return count;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                AuthoredEntity[] proxies = roots[r].GetComponentsInChildren<AuthoredEntity>(true);
                for (int i = 0; i < proxies.Length; i++)
                {
                    if (proxies[i].gameObject.activeSelf)
                    {
                        proxies[i].gameObject.SetActive(false);
                        count++;
                    }
                }
            }

            return count;
        }

        private sealed class UnitySceneOperation : ISceneOperation
        {
            private readonly AsyncOperation operation;
            private readonly string scenePath;
            private readonly bool isLoad;
            private AsyncOperation? release;
            private bool finished;

            public UnitySceneOperation(AsyncOperation operation, string scenePath, bool isLoad)
            {
                this.operation = operation;
                this.scenePath = scenePath;
                this.isLoad = isLoad;
            }

            public bool IsDone
            {
                get
                {
                    if (finished)
                    {
                        return true;
                    }

                    if (!operation.isDone)
                    {
                        return false;
                    }

                    if (!isLoad)
                    {
                        if (release == null) release = Resources.UnloadUnusedAssets();
                        if (!release.isDone) return false;
                    }
                    else
                    {
                        DeactivateProxies(SceneManager.GetSceneByPath(scenePath));
                    }

                    finished = true;
                    return true;
                }
            }

            public bool Failed
            {
                get
                {
                    if (!IsDone || !isLoad)
                    {
                        return false;
                    }

                    Scene scene = SceneManager.GetSceneByPath(scenePath);
                    return !scene.IsValid() || !scene.isLoaded;
                }
            }
        }
    }

    /// <summary>Streams region scenes after the committed residency of each region (see the file header).</summary>
    public sealed class RegionStreamer : IResidencyQuery
    {
        /// <summary>Frames to wait for a submitted residency change to commit before re-deciding.</summary>
        public const int CommitTimeoutFrames = 600;

        private readonly GameplayWorld world;
        private readonly ISceneLoader loader;
        private readonly List<RegionState> regions = new List<RegionState>();
        private readonly List<string> regionIds = new List<string>();
        private readonly List<IResidencyAware> listeners = new List<IResidencyAware>();
        private readonly List<GameplayDiagnostic> diagnostics = new List<GameplayDiagnostic>();
        private readonly int startRegionKey;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private CancellationToken external;

        internal RegionStreamer(GameplayWorld world, TargetId focus, string startRegionId, bool preloadNeighbours, ISceneLoader loader)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            Focus = focus;
            PreloadNeighbours = preloadNeighbours;
            startRegionKey = world.Worlds.TryRegion(startRegionId, out RegionRecord? start) && start != null ? start.Key : 0;
            for (int i = 0; i < world.Worlds.Regions.Count; i++)
            {
                regions.Add(new RegionState(world.Worlds.Regions[i]));
                regionIds.Add(world.Worlds.Regions[i].AuthoringId);
            }

            WarnScenesOutsideBuild();
        }

        /// <summary>The traveller whose region is kept resident; default means "the start region".</summary>
        public TargetId Focus { get; set; }

        public bool PreloadNeighbours { get; set; }

        public bool IsCancelled => cancellation.IsCancellationRequested || external.IsCancellationRequested;

        public IReadOnlyList<RegionRecord> Regions => world.Worlds.Regions;

        public IReadOnlyList<string> RegionIds => regionIds;

        public int LoadsStarted { get; private set; }

        public int UnloadsStarted { get; private set; }

        public int LoadFailures { get; private set; }

        public int SubmittedChanges { get; private set; }

        public int RefusedSubmits { get; private set; }

        public int CommitTimeouts { get; private set; }

        public int Ticks { get; private set; }

        /// <summary>Scenes loaded or unloaded by the first-tick reconciliation (A3).</summary>
        public int Reconciliations { get; private set; }

        /// <summary>Regions whose streaming latched after repeated load failures.</summary>
        public int LatchedRegions
        {
            get
            {
                int count = 0;
                for (int i = 0; i < regions.Count; i++)
                {
                    if (StreamingRules.IsLatched(regions[i].Failures))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Streaming diagnostics (GP-WLD-030/031/032), oldest first (at most 64).</summary>
        public IReadOnlyList<GameplayDiagnostic> Diagnostics => diagnostics;

        public ISceneLoader Loader => loader;

        /// <summary>Observes an external cancellation token (e.g. the owning behaviour's lifetime).</summary>
        public void Observe(CancellationToken token) => external = token;

        public void AddListener(IResidencyAware listener)
        {
            if (listener != null && !listeners.Contains(listener))
            {
                listeners.Add(listener);
                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i].Notified >= 0)
                    {
                        listener.OnResidencyChanged(regions[i].Record.AuthoringId, (RegionResidency)regions[i].Notified);
                    }
                }
            }
        }

        /// <summary>True when a region's streaming latched after repeated load failures.</summary>
        public bool IsLatched(string regionId)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                if (string.Equals(regions[i].Record.AuthoringId, regionId, StringComparison.Ordinal))
                {
                    return StreamingRules.IsLatched(regions[i].Failures);
                }
            }

            return false;
        }

        /// <summary>Re-arms a latched region (after its scene was fixed); false for an unknown region.</summary>
        public bool ResetLatch(string regionId)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                if (string.Equals(regions[i].Record.AuthoringId, regionId, StringComparison.Ordinal))
                {
                    regions[i].Failures = 0;
                    regions[i].RetryAtTick = 0;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stops streaming: nothing new loads, and loaded regions unload on the following ticks.</summary>
        public void Cancel() => cancellation.Cancel();

        /// <summary>Resumes streaming after <see cref="Cancel"/>.</summary>
        public void Resume()
        {
            if (cancellation.IsCancellationRequested)
            {
                cancellation.Dispose();
                cancellation = new CancellationTokenSource();
            }
        }

        public bool TryGetResidency(string regionId, out RegionResidency residency)
        {
            residency = RegionResidency.Unloaded;
            if (!world.Worlds.TryRegion(regionId, out RegionRecord? region) || region == null)
            {
                return false;
            }

            residency = (RegionResidency)world.Slots.ReadOrDefault(region.Target, GameplaySlots.WorldOwner, GameplaySlots.Residency, 0);
            return true;
        }

        public RegionResidency ResidencyOf(string regionId) =>
            TryGetResidency(regionId, out RegionResidency residency) ? residency : RegionResidency.Unloaded;

        /// <summary>The region key the focus traveller is committed in (the start region without a focus).</summary>
        public int FocusRegionKey =>
            !Focus.IsDefault && world.Slots.TryRead(Focus, GameplaySlots.WorldOwner, GameplaySlots.Region, out int key) ? key : startRegionKey;

        /// <summary>True when every region's committed residency is final for the current focus and no scene operation runs.</summary>
        public bool IsSettled
        {
            get
            {
                HashSet<int> wanted = Wanted();
                for (int i = 0; i < regions.Count; i++)
                {
                    RegionState state = regions[i];
                    int committed = CommittedOf(state.Record);
                    int final = wanted.Contains(state.Record.Key) ? Residency.Resident : Residency.Unloaded;
                    if (committed != final || state.Operation != null || state.Reconciling != null || state.Expected >= 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>One streaming step for every region; called after each host pump.</summary>
        public void Tick()
        {
            Ticks++;
            HashSet<int> wanted = Wanted();
            for (int i = 0; i < regions.Count; i++)
            {
                Step(regions[i], wanted.Contains(regions[i].Record.Key));
            }
        }

        private HashSet<int> Wanted()
        {
            var wanted = new HashSet<int>();
            if (IsCancelled)
            {
                return wanted;
            }

            int focus = FocusRegionKey;
            if (focus != 0)
            {
                wanted.Add(focus);
                if (PreloadNeighbours)
                {
                    IReadOnlyList<int> neighbours = world.Worlds.Graph.NeighboursOf(focus);
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        wanted.Add(neighbours[i]);
                    }
                }
            }

            return wanted;
        }

        private int CommittedOf(RegionRecord region) =>
            world.Slots.ReadOrDefault(region.Target, GameplaySlots.WorldOwner, GameplaySlots.Residency, Residency.Unloaded);

        private void Step(RegionState state, bool want)
        {
            int committed = CommittedOf(state.Record);
            if (committed != state.Notified)
            {
                state.Notified = committed;
                for (int i = 0; i < listeners.Count; i++)
                {
                    listeners[i].OnResidencyChanged(state.Record.AuthoringId, (RegionResidency)committed);
                }
            }

            if (!state.Reconciled)
            {
                state.Reconciled = true;
                if (Reconcile(state, committed, want))
                {
                    return;
                }
            }

            if (state.Reconciling != null)
            {
                FinishReconcile(state, committed);
                return;
            }

            if (state.Expected >= 0)
            {
                if (committed == state.Expected)
                {
                    state.Expected = -1;
                }
                else if (++state.WaitedFrames > CommitTimeoutFrames)
                {
                    CommitTimeouts++;
                    state.Expected = -1;
                }
                else
                {
                    return;
                }
            }

            string path = state.Record.ScenePath;
            switch (committed)
            {
                case Residency.Unloaded:
                    if (want && !StreamingRules.IsLatched(state.Failures) && Ticks >= state.RetryAtTick)
                    {
                        Submit(state, Residency.Loading);
                    }

                    break;

                case Residency.Loading:
                    if (state.Operation == null)
                    {
                        state.Unloading = false;
                        state.Operation = loader.IsLoaded(path) ? new CompletedSceneOperation(false) : loader.Load(path);
                        LoadsStarted++;
                        break;
                    }

                    if (!state.Operation.IsDone)
                    {
                        break;
                    }

                    if (state.Unloading)
                    {
                        state.Operation = null;
                        state.Unloading = false;
                        Submit(state, Residency.Unloaded);
                        break;
                    }

                    if (state.Operation.Failed)
                    {
                        state.Operation = null;
                        RecordLoadFailure(state);
                        Submit(state, Residency.Unloaded);
                        break;
                    }

                    if (want)
                    {
                        state.Operation = null;
                        state.Failures = 0;
                        Submit(state, Residency.Resident);
                        break;
                    }

                    // Cancelled or no longer wanted while loading: unload again and fall back to Unloaded.
                    state.Operation = loader.Unload(path);
                    state.Unloading = true;
                    UnloadsStarted++;
                    break;

                case Residency.Resident:
                    if (!want)
                    {
                        Submit(state, Residency.Unloading);
                    }

                    break;

                case Residency.Unloading:
                    if (state.Operation == null)
                    {
                        state.Operation = loader.Unload(path);
                        state.Unloading = true;
                        UnloadsStarted++;
                        break;
                    }

                    if (state.Operation.IsDone)
                    {
                        state.Operation = null;
                        state.Unloading = false;
                        Submit(state, Residency.Unloaded);
                    }

                    break;
            }
        }

        /// <summary>The first-tick reconciliation of one region; true when a scene operation started.</summary>
        private bool Reconcile(RegionState state, int committed, bool want)
        {
            string path = state.Record.ScenePath;
            if (string.IsNullOrEmpty(path) || state.Operation != null)
            {
                return false;
            }

            ReconcileAction action = StreamingRules.Reconcile(committed, loader.IsLoaded(path), want);
            if (action == ReconcileAction.LoadScene)
            {
                state.Reconciling = loader.Load(path);
                state.ReconcileLoads = true;
                LoadsStarted++;
                Reconciliations++;
                return true;
            }

            if (action == ReconcileAction.UnloadScene)
            {
                state.Reconciling = loader.Unload(path);
                state.ReconcileLoads = false;
                UnloadsStarted++;
                Reconciliations++;
                return true;
            }

            return false;
        }

        private void FinishReconcile(RegionState state, int committed)
        {
            ISceneOperation? operation = state.Reconciling;
            if (operation == null || !operation.IsDone)
            {
                return;
            }

            state.Reconciling = null;
            if (state.ReconcileLoads && operation.Failed)
            {
                // The restored region's scene did not load: leave Resident through the legal path so the ordinary
                // machine retries it with back-off.
                RecordLoadFailure(state);
                if (committed == Residency.Resident)
                {
                    Submit(state, Residency.Unloading);
                }
            }
        }

        private void RecordLoadFailure(RegionState state)
        {
            LoadFailures++;
            state.Failures++;
            state.RetryAtTick = Ticks + StreamingRules.BackoffFrames(state.Failures);
            bool latched = StreamingRules.IsLatched(state.Failures);
            string failures = state.Failures.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string message = latched
                ? "region scene " + state.Record.ScenePath + " failed to load " + failures + " times; streaming of region "
                  + state.Record.Name + " is latched until ResetLatch"
                : "region scene " + state.Record.ScenePath + " failed to load (attempt " + failures + "); retrying in "
                  + StreamingRules.BackoffFrames(state.Failures).ToString(System.Globalization.CultureInfo.InvariantCulture) + " frames";
            Report(new GameplayDiagnostic(latched ? WorldRefusalCodes.SceneLoadLatched : WorldRefusalCodes.SceneLoadFailed, state.Record.AuthoringId, message));
        }

        private void Report(GameplayDiagnostic diagnostic)
        {
            if (diagnostics.Count >= 64)
            {
                diagnostics.RemoveAt(0);
            }

            diagnostics.Add(diagnostic);
            Debug.LogWarning("[GameCore] " + diagnostic);
        }

        private void WarnScenesOutsideBuild()
        {
#if UNITY_EDITOR
            if (!(loader is UnitySceneLoader))
            {
                return;
            }

            var inBuild = new HashSet<string>(StringComparer.Ordinal);
            UnityEditor.EditorBuildSettingsScene[] scenes = UnityEditor.EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null && scenes[i].enabled)
                {
                    inBuild.Add(scenes[i].path);
                }
            }

            for (int i = 0; i < regions.Count; i++)
            {
                string path = regions[i].Record.ScenePath;
                if (!string.IsNullOrEmpty(path) && !inBuild.Contains(path))
                {
                    Report(new GameplayDiagnostic(WorldRefusalCodes.SceneNotInBuild, regions[i].Record.AuthoringId,
                        "region scene " + path + " is not in the build settings: it streams in Play Mode, but a player build cannot load it"));
                }
            }
#endif
        }

        private void Submit(RegionState state, int residency)
        {
            OperationId operation = world.Root.NextOperation();
            var envelope = new CommandEnvelope(
                operation,
                WorldDeclarations.SetResidencyRoute,
                state.Record.Target,
                WorldDeclarations.SetResidencyCommand,
                null,
                ResidencyPayload.Encode(residency));
            CommandAdmissionReceipt receipt = world.Root.Host.Submit(envelope);
            if (!receipt.Admitted)
            {
                RefusedSubmits++;
                return;
            }

            SubmittedChanges++;
            state.Expected = residency;
            state.WaitedFrames = 0;
        }

        private sealed class RegionState
        {
            public RegionState(RegionRecord record)
            {
                Record = record;
            }

            public RegionRecord Record { get; }

            public ISceneOperation? Operation { get; set; }

            /// <summary>True while <see cref="Operation"/> is an unload.</summary>
            public bool Unloading { get; set; }

            /// <summary>Residency submitted and not yet committed; -1 when none.</summary>
            public int Expected { get; set; } = -1;

            public int WaitedFrames { get; set; }

            /// <summary>Last committed residency told to listeners; -1 before the first tick.</summary>
            public int Notified { get; set; } = -1;

            /// <summary>True once the first-tick reconciliation ran (A3).</summary>
            public bool Reconciled { get; set; }

            /// <summary>The reconciliation's scene operation (no residency change rides on it); null when none.</summary>
            public ISceneOperation? Reconciling { get; set; }

            /// <summary>True while <see cref="Reconciling"/> is a load.</summary>
            public bool ReconcileLoads { get; set; }

            /// <summary>Consecutive load failures (reset by a successful load or ResetLatch).</summary>
            public int Failures { get; set; }

            /// <summary>The tick before which a failed region is not loaded again.</summary>
            public int RetryAtTick { get; set; }
        }
    }
}
