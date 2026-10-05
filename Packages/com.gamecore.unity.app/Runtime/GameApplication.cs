// GameCore.Unity.App — SADR-010 (studio): the MonoBehaviour-free entry of a game application.
//
// Two ways in, one composition:
//
//   * `GameApplication.Boot(definition)` validates the definition, creates the world with the real catalog
//     fingerprint, composes the root and returns it, or throws `GameApplicationBootException` with the typed failure.
//     No scene object and no MonoBehaviour is involved; a test, a headless tool or a game's own entry point calls it.
//   * `GameApplication.Register(definition)` is the player path. Called from the game's
//     `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` method, it sets the application bootstrap's root factory,
//     switches the bootstrap to the hard-failure policy and hands it the catalog fingerprint, so the one
//     `ICustomBootstrap` creates the game's world and this package composes the root over it. A failure there is a
//     logged, named boot failure and no world - never the infrastructure-only fallback world.
//
// The validation project keeps the legacy bootstrap: it never calls `Register`, so the bootstrap's policy stays
// `InfrastructureFallback` and its root factory stays the probe composition (04 s9 note, PACKET.md "switch").
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Unity.App
{
    /// <summary>Host-integration choices of one boot; the defaults are the production player's.</summary>
    public sealed class GameApplicationBootOptions
    {
        /// <summary>The production defaults: install the loop node, assign the default world, assert pumps per frame.</summary>
        public static GameApplicationBootOptions Default => new GameApplicationBootOptions();

        /// <summary>Install the one application-owned PlayerLoop node (idempotent; 04 s3).</summary>
        public bool InstallPlayerLoop { get; set; } = true;

        /// <summary>Assign <c>World.DefaultGameObjectInjectionWorld</c> to the booted world when none is assigned.</summary>
        public bool AssignDefaultWorld { get; set; } = true;

        /// <summary>Start the world after boot instead of leaving it Ready (paused at a committed boundary).</summary>
        public bool StartImmediately { get; set; }

        /// <summary>Override of the pump counter's assertion switch; null keeps the Editor/development default.</summary>
        public bool? PumpAssertions { get; set; }

        /// <summary>Override of the pump counter's frame clock; null uses <c>UnityEngine.Time.frameCount</c>.</summary>
        public Func<long>? FrameClock { get; set; }
    }

    /// <summary>The static entry of a game application (SADR-010).</summary>
    public static class GameApplication
    {
        private static GameApplicationDefinition? registered;
        private static GameApplicationBootOptions registeredOptions = GameApplicationBootOptions.Default;
        private static PipelineDescriptorReport? registeredSchedule;
        private static CatalogManifestSource? registeredManifests;
        private static readonly Action<UnityWorldHost> ComposeHook = ComposeRegistered;
        private static readonly Func<GameCoreApplicationCompositionRoot> RootFactoryHook = CreateRegisteredRoot;

        /// <summary>The live root booted by either path or adopted by a successful restore, or null.</summary>
        public static GameApplicationRoot? Current { get; private set; }

        /// <summary>The most recent boot failure, or null when the most recent boot succeeded.</summary>
        public static GameApplicationBootFailed? LastFailure { get; private set; }

        /// <summary>Successful boots since process start.</summary>
        public static int BootCount { get; private set; }

        /// <summary>Failed boots since process start.</summary>
        public static int FailureCount { get; private set; }

        /// <summary>Raised with every boot failure, after it was logged.</summary>
        public static event Action<GameApplicationBootFailed>? BootFailed;

        /// <summary>The definition <see cref="Register"/> installed for the player path, or null.</summary>
        public static GameApplicationDefinition? Registered => registered;

        /// <summary>
        /// P1.7a (A11): with domain reload disabled (Enter Play Mode options) statics survive from one play session to the
        /// next, so a stale <see cref="Current"/> root, failure, counters and BootFailed subscribers would leak into the
        /// new session. They are reset at SubsystemRegistration. The registration itself is not: a game registers from
        /// its own SubsystemRegistration method, and Unity does not order those calls, so clearing it here could undo it.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetSessionStatics()
        {
            Current = null;
            LastFailure = null;
            BootCount = 0;
            FailureCount = 0;
            BootFailed = null;
        }

        /// <summary>Boots <paramref name="definition"/> with the production options; throws on failure.</summary>
        public static GameApplicationRoot Boot(GameApplicationDefinition definition) =>
            Boot(definition, GameApplicationBootOptions.Default);

        /// <summary>Boots <paramref name="definition"/>; throws <see cref="GameApplicationBootException"/> on failure.</summary>
        public static GameApplicationRoot Boot(GameApplicationDefinition definition, GameApplicationBootOptions options)
        {
            if (TryBoot(definition, options, out GameApplicationRoot? root, out GameApplicationBootFailed? failure) && root != null)
            {
                return root;
            }

            throw new GameApplicationBootException(failure ?? new GameApplicationBootFailed(
                GameApplicationBootCode.CompositionFault, DiagnosticCode.ApplyFault, "boot", "no root and no failure"));
        }

        /// <summary>Boots <paramref name="definition"/>; false with the typed, logged failure when it refuses.</summary>
        public static bool TryBoot(
            GameApplicationDefinition definition,
            GameApplicationBootOptions? options,
            out GameApplicationRoot? root,
            out GameApplicationBootFailed? failure)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            GameApplicationBootOptions effective = options ?? GameApplicationBootOptions.Default;
            root = null;
            GameCoreThreading.CaptureMainThread();

            if (Current != null && IsLive(Current))
            {
                failure = Fail(new GameApplicationBootFailed(
                    GameApplicationBootCode.AlreadyBooted, DiagnosticCode.IdempotencyConflict, "boot",
                    "application root " + Current + " is still live; stop it before booting another (SADR-010)"));
                return false;
            }

            if (GameCoreApplicationBootstrap.FallbackCount != 0)
            {
                failure = Fail(new GameApplicationBootFailed(
                    GameApplicationBootCode.BootstrapFallback, DiagnosticCode.MissingDependency, "bootstrap",
                    "the application bootstrap fell back to an infrastructure-only world "
                    + GameCoreApplicationBootstrap.FallbackCount.ToString(CultureInfo.InvariantCulture)
                    + " time(s) (" + GameCoreApplicationBootstrap.LastDetail + "); a game boots with FallbackCount == 0"));
                return false;
            }

            if (!TryPrepare(definition, out CatalogManifestSource? manifests, out PipelineDescriptorReport? schedule, out failure)
                || manifests == null
                || schedule == null)
            {
                Fail(failure!);
                return false;
            }

            WorldId session = GameCoreApplicationComposition.ReserveSessionId();
            var creation = new OperationId(session, definition.Issuer, 1UL);
            var request = new WorldCreateRequest(
                session,
                definition.WorldDefinition,
                definition.TemporalModel,
                definition.Propagation,
                definition.CatalogHash,
                creation,
                definition.FixedStep);

            if (!UnityWorldRegistry.TryCreate(
                    request, CreateRegistration(definition, schedule), out UnityWorldHost? host, out WorldCreateResult result)
                || host == null)
            {
                failure = Fail(new GameApplicationBootFailed(
                    GameApplicationBootCode.WorldCreationFailed, result.Code, "world", result.Detail));
                return false;
            }

            root = GameApplicationRoot.TryCompose(definition, host, manifests, schedule, effective, 1UL, out failure);
            if (root == null)
            {
                Fail(failure!);
                return false;
            }

            if (effective.InstallPlayerLoop)
            {
                GameCorePlayerLoopInstaller.EnsureInstalled();
            }

            if (effective.AssignDefaultWorld && global::Unity.Entities.World.DefaultGameObjectInjectionWorld == null)
            {
                global::Unity.Entities.World.DefaultGameObjectInjectionWorld = host.EntityWorld;
            }

            if (effective.StartImmediately)
            {
                root.Start();
            }

            Succeed(root);
            return true;
        }

        /// <summary>
        /// The player path (SADR-010): installs <paramref name="definition"/> as the application bootstrap's root, in
        /// hard-failure mode, with the real catalog fingerprint. Call it from the game's
        /// <c>RuntimeInitializeOnLoadMethod(SubsystemRegistration)</c> method. A definition that does not validate is
        /// reported here and again when the bootstrap runs; the bootstrap then creates no world.
        /// </summary>
        public static void Register(GameApplicationDefinition definition, GameApplicationBootOptions? options = null)
        {
            registered = definition ?? throw new ArgumentNullException(nameof(definition));
            registeredOptions = options ?? new GameApplicationBootOptions { InstallPlayerLoop = false, StartImmediately = true };
            registeredSchedule = null;
            registeredManifests = null;

            GameCoreApplicationBootstrap.FailurePolicy = GameCoreBootstrapFailurePolicy.HardFailure;
            GameCoreApplicationBootstrap.CatalogHash = definition.CatalogHash;
            GameCoreApplicationBootstrap.Propagation = definition.Propagation;
            GameCoreApplicationBootstrap.WorldCreated = ComposeHook;
            GameCoreApplicationComposition.RootFactory = RootFactoryHook;
        }

        /// <summary>
        /// Reverts <see cref="Register"/>: the bootstrap goes back to the legacy infrastructure-fallback policy with no
        /// root factory, catalog hash or world hook (the validation project's configuration).
        /// </summary>
        public static void Unregister()
        {
            registered = null;
            registeredSchedule = null;
            registeredManifests = null;
            if (ReferenceEquals(GameCoreApplicationBootstrap.WorldCreated, ComposeHook))
            {
                GameCoreApplicationBootstrap.WorldCreated = null;
            }

            if (ReferenceEquals(GameCoreApplicationComposition.RootFactory, RootFactoryHook))
            {
                GameCoreApplicationComposition.RootFactory = null;
            }

            GameCoreApplicationBootstrap.FailurePolicy = GameCoreBootstrapFailurePolicy.InfrastructureFallback;
            GameCoreApplicationBootstrap.CatalogHash = ContentHash.Empty;
            GameCoreApplicationBootstrap.Propagation = PropagationMode.Automatic;
        }

        /// <summary>The registration a definition's world is created with, from its compiled schedule (04 s3).</summary>
        public static UnityWorldRegistration CreateRegistration(GameApplicationDefinition definition, PipelineDescriptorReport schedule)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ScheduleAdaptation adaptation = schedule?.Adaptation
                ?? throw new ArgumentException("A registration is built from a succeeded schedule adaptation.", nameof(schedule));
            return new UnityWorldRegistration(
                definition.Name,
                adaptation.Stages,
                definition.Systems,
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan!,
                GuardedDispatchPlan.Empty,
                definition.SeedWorldState,
                definition.Messages,
                definition.MessageReaders);
        }

        /// <summary>
        /// Validates a definition up to, but not including, world creation: structure, the declared catalog
        /// fingerprint against the catalog, every plugin declaration, the configuration bindings and the
        /// ownership/schedule pipeline. Nothing is created.
        /// </summary>
        public static bool TryPrepare(
            GameApplicationDefinition definition,
            out CatalogManifestSource? manifests,
            out PipelineDescriptorReport? schedule,
            out GameApplicationBootFailed? failure)
        {
            manifests = null;
            schedule = null;
            failure = null;
            try
            {
                if (definition.RootScope.IsDefault || definition.Issuer.IsDefault || definition.WorldDefinition.IsDefault)
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.InvalidDefinition, DiagnosticCode.MissingDependency, "definition",
                        "a game application names its root scope, its issuer and its world definition (P-004, P-010)");
                    return false;
                }

                if (definition.CatalogHash.IsEmpty)
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.CatalogHashMissing, DiagnosticCode.MissingDependency, "catalog",
                        "the definition declares no catalog fingerprint; a game world is created with the real one");
                    return false;
                }

                ContentHash actual = definition.Catalog.Fingerprint;
                if (!actual.Equals(definition.CatalogHash))
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.CatalogFingerprintMismatch, DiagnosticCode.UnsupportedVersion, "catalog",
                        "the catalog's fingerprint " + actual.ToHex() + " differs from the declared "
                        + definition.CatalogHash.ToHex() + ": the catalog is corrupted or substituted (P-028, P-053)");
                    return false;
                }

                var source = new CatalogManifestSource(definition.Catalog, definition.Plugins);
                if (source.Rejected.Count != 0)
                {
                    CatalogDeclarationRejection first = source.Rejected[0];
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.PluginDeclarationRejected, first.Code, "catalog",
                        source.Rejected.Count.ToString(CultureInfo.InvariantCulture) + " declaration(s) refused; first: "
                        + first.ToString());
                    return false;
                }

                if (!InstallConfigBinding.TryValidate(definition.ConfigBindings, out string bindingDetail))
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.ConfigBindingInvalid, DiagnosticCode.IdempotencyConflict, "config-bindings",
                        bindingDetail);
                    return false;
                }

                var plugins = new List<PluginManifest>(definition.Plugins.Count);
                for (int i = 0; i < definition.Plugins.Count; i++)
                {
                    plugins.Add(definition.Plugins[i].Manifest);
                }

                PipelineDescriptorReport report = OwnershipSchedulePipeline.Build(
                    plugins, definition.DispatchKinds, definition.SlotPolicyMigrations);
                if (!report.Succeeded || report.Descriptor == null || report.Adaptation == null
                    || !report.Adaptation.Succeeded || report.Adaptation.StepPlan == null)
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.ScheduleRejected, report.Code, "schedule", report.Describe());
                    return false;
                }

                manifests = source;
                schedule = report;
                return true;
            }
            catch (Exception exception)
            {
                failure = new GameApplicationBootFailed(
                    GameApplicationBootCode.CompositionFault, DiagnosticCode.ApplyFault, "prepare",
                    exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Transfers application ownership during SaveService's main-thread, callback-free active-root swap.
        /// Call before stopping the old root: its lifecycle observers must already see the replacement, and
        /// NotifyStopped must not clear it. Restoring a secondary or scratch world is not an application boot.
        /// </summary>
        internal static void AdoptRestoredRoot(GameApplicationRoot previous, GameApplicationRoot restored)
        {
            if (ReferenceEquals(Current, previous))
            {
                Current = restored;
            }
        }

        internal static void NotifyStopped(GameApplicationRoot root)
        {
            if (ReferenceEquals(Current, root))
            {
                Current = null;
            }
        }

        private static GameCoreApplicationCompositionRoot CreateRegisteredRoot()
        {
            GameApplicationDefinition definition = registered
                ?? throw new InvalidOperationException("no game application definition is registered (SADR-010)");
            if (!TryPrepare(definition, out CatalogManifestSource? manifests, out PipelineDescriptorReport? schedule, out GameApplicationBootFailed? failure)
                || manifests == null
                || schedule == null)
            {
                // The bootstrap turns this exception into its own logged hard failure; the typed failure is kept here.
                Fail(failure!);
                throw new GameApplicationBootException(failure!);
            }

            registeredManifests = manifests;
            registeredSchedule = schedule;
            return new GameCoreApplicationCompositionRoot(
                CreateRegistration(definition, schedule),
                definition.WorldDefinition,
                definition.TemporalModel,
                definition.FixedStep);
        }

        private static void ComposeRegistered(UnityWorldHost host)
        {
            GameApplicationDefinition definition = registered
                ?? throw new InvalidOperationException("no game application definition is registered (SADR-010)");
            if (registeredManifests == null || registeredSchedule == null)
            {
                throw new InvalidOperationException("the registered root factory did not prepare the definition");
            }

            GameApplicationRoot? root = GameApplicationRoot.TryCompose(
                definition, host, registeredManifests, registeredSchedule, registeredOptions, 1UL,
                out GameApplicationBootFailed? failure);
            if (root == null)
            {
                Fail(failure!);
                throw new GameApplicationBootException(failure!);
            }

            if (registeredOptions.StartImmediately)
            {
                root.Start();
            }

            Succeed(root);
        }

        private static bool IsLive(GameApplicationRoot root) =>
            root.State != GameApplicationState.Stopped
            && (root.Host.Lifecycle == WorldLifecycleState.Running || root.Host.Lifecycle == WorldLifecycleState.Paused);

        private static void Succeed(GameApplicationRoot root)
        {
            Current = root;
            LastFailure = null;
            BootCount++;
        }

        private static GameApplicationBootFailed Fail(GameApplicationBootFailed failure)
        {
            LastFailure = failure;
            FailureCount++;
            UnityEngine.Debug.LogError("[GameCore] " + failure);
            Action<GameApplicationBootFailed>? handler = BootFailed;
            handler?.Invoke(failure);
            return failure;
        }
    }
}
