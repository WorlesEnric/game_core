// GameCore.Unity.App — SADR-010 (studio): the one application root of a game.
//
// The root owns, for exactly one world incarnation, everything the kernel already provides as separate parts and that
// every scenario so far wired by hand: the catalog manifest source, the composition lane, the target registry, the
// recipe catalog, the migration registry, the ownership/stage descriptor, the assembly publisher, the derived-assembly
// pipeline with its validate-before-commit preflight (SADR-011), the world/composition bridge, the derivation
// provenance store and its explanation reader, the observation hub and the world's adapter-frame registration with
// the one-pump counter. It composes them in one order, from one definition, and refuses to exist half-composed.
//
// Lifecycle is explicit: a booted root's world is Paused at a committed boundary until `Start`; `Pause`/`Resume` are
// O-26 run-state changes; `Stop` unregisters the adapter frame and stops the world (O-19). Spawning a new target at
// runtime stays a pipeline call (`Pipeline.PublishSpawn`) because a spawn publishes the assembly of a composition
// publication whose derivation changed no target - it is not a bridge edit (P-024).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Composition.Diagnostics;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Observation;
using GameCore.Execution.Persistence;
using GameCore.Planning;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;

namespace GameCore.Unity.App
{
    /// <summary>Lifecycle of one application root (SADR-010).</summary>
    public enum GameApplicationState
    {
        /// <summary>Composed and booted; the world is paused at a committed boundary until <see cref="GameApplicationRoot.Start"/>.</summary>
        Ready = 0,

        Running = 1,

        Paused = 2,

        /// <summary>Stopped: the adapter frame is unregistered and the world is disposed or blocked in teardown.</summary>
        Stopped = 3,
    }

    /// <summary>The composed application root of one game world (SADR-010).</summary>
    public sealed class GameApplicationRoot : IDisposable
    {
        private ulong operationSequence;

        private GameApplicationRoot(
            GameApplicationDefinition definition,
            UnityWorldHost host,
            CatalogManifestSource manifests,
            PipelineDescriptorReport schedule,
            TargetRegistry registry,
            AssemblyPublisher publisher,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            CompositionHost lane,
            CompositionEditValidatorSet validators,
            DerivationModeSwitchValidator modeSwitch,
            DerivedAssemblyPreflightValidator preflight,
            DerivedAssemblyPipeline pipeline,
            WorldCompositionBridge bridge,
            ProvenanceStore provenance,
            ulong firstOperationSequence)
        {
            Definition = definition;
            Host = host;
            Manifests = manifests;
            Schedule = schedule;
            Registry = registry;
            Publisher = publisher;
            Targets = targets;
            Seeder = seeder;
            Lane = lane;
            Validators = validators;
            ModeSwitch = modeSwitch;
            Preflight = preflight;
            Pipeline = pipeline;
            Bridge = bridge;
            Provenance = provenance;
            Explanations = new ProvenanceExplanationReader(provenance);
            operationSequence = firstOperationSequence;
            PumpCounter = new GameApplicationPumpCounter(host);
            State = GameApplicationState.Ready;
        }

        public GameApplicationDefinition Definition { get; }

        public UnityWorldHost Host { get; }

        public WorldId World => Host.World;

        /// <summary>The catalog fingerprint the world was created with: the real one, never empty (SADR-010).</summary>
        public ContentHash CatalogHash => Definition.CatalogHash;

        public CatalogManifestSource Manifests { get; }

        /// <summary>The ownership/schedule pipeline's report: descriptor, compiled schedule and its adaptation.</summary>
        public PipelineDescriptorReport Schedule { get; }

        public OwnershipStageDescriptor Descriptor => Schedule.Descriptor!;

        public TargetRegistry Registry { get; }

        public AssemblyPublisher Publisher { get; }

        public SpawnRecipeCatalog Recipes => Publisher.Recipes;

        public MigrationRegistry Migrations => Publisher.Migrations;

        public LiveTargetIndex Targets { get; }

        public LiveTargetSeeder Seeder { get; }

        /// <summary>The control lane (P-006, P-027..P-031).</summary>
        public CompositionHost Lane { get; }

        /// <summary>The lane's validator set: the mode-switch validator, then the world preflight (SADR-011).</summary>
        public CompositionEditValidatorSet Validators { get; }

        public DerivationModeSwitchValidator ModeSwitch { get; }

        public DerivedAssemblyPreflightValidator Preflight { get; }

        public DerivedAssemblyPipeline Pipeline { get; }

        /// <summary>The bridge on the validate-before-commit path; every composition edit goes through it.</summary>
        public WorldCompositionBridge Bridge { get; }

        /// <summary>Derivation provenance of every accepted publication (P-026).</summary>
        public ProvenanceStore Provenance { get; }

        /// <summary>The explanation reader over <see cref="Provenance"/> (O-25).</summary>
        public ProvenanceExplanationReader Explanations { get; }

        /// <summary>The world's observation hub: lifecycle, diagnostics, committed-image events.</summary>
        public ObservationHub Observations => Host.Observations;

        /// <summary>The world's committed-image observation surface (P-045).</summary>
        public WorldObservation Observation => Host.Observation;

        /// <summary>The one-pump counter, registered as this world's adapter frame (SADR-010).</summary>
        public GameApplicationPumpCounter PumpCounter { get; }

        public GameApplicationState State { get; private set; }

        /// <summary>Provenance publications that were refused or had no committed boundary to attach to.</summary>
        public int ProvenanceMisses { get; private set; }

        /// <summary>The most recent provenance publication, or null before one.</summary>
        public ProvenancePublicationReport? LastProvenance { get; private set; }

        /// <summary>A fresh operation identity of this root's issuer (P-050).</summary>
        public OperationId NextOperation()
        {
            operationSequence++;
            return new OperationId(Host.World, Definition.Issuer, operationSequence);
        }

        /// <summary>
        /// Submits one composition edit against the currently published revision through the validate-before-commit
        /// bridge (SADR-011). The edit changes the assembly epoch only; it never advances the logical step.
        /// </summary>
        public WorldAdmissionReport Submit(CompositionEditPayload payload) => Submit(payload, Lane.Committed.Revision);

        /// <summary>Submits one composition edit prepared against <paramref name="expectedRevision"/> (SADR-011).</summary>
        public WorldAdmissionReport Submit(CompositionEditPayload payload, CompositionRevision expectedRevision)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            RequireLive("submit an edit");
            PumpCounter.ObserveHost();
            WorldAdmissionReport report = Bridge.SubmitAndExecute(payload, NextOperation(), expectedRevision);
            if (report.Outcome == BridgeOutcome.Executed)
            {
                PublishProvenance(report.Assembly);
            }

            return report;
        }

        /// <summary>Starts the world: Ready or Paused becomes Running (O-26).</summary>
        public OperationResult Start()
        {
            RequireLive("start");
            return SetRunState(WorldLifecycleState.Running, GameApplicationState.Running);
        }

        public OperationResult Pause()
        {
            RequireLive("pause");
            return SetRunState(WorldLifecycleState.Paused, GameApplicationState.Paused);
        }

        public OperationResult Resume()
        {
            RequireLive("resume");
            return SetRunState(WorldLifecycleState.Running, GameApplicationState.Running);
        }

        /// <summary>
        /// Stops the application: the adapter frame is unregistered first, so no pump reaches a world that is being
        /// torn down, then the world stops (O-19). A blocked teardown reports TeardownBlocked and leaves the root
        /// Stopped; nothing is freed while still reachable (P-048).
        /// </summary>
        public OperationResult Stop(string reason)
        {
            if (State == GameApplicationState.Stopped)
            {
                return new OperationResult(NextOperation(), Outcome.NoChange, DiagnosticCode.None, null);
            }

            PumpCounter.ObserveHost();
            if (AdapterFrameRegistry.TryGet(Host.World, out IAdapterFrame? registered) && ReferenceEquals(registered, PumpCounter))
            {
                AdapterFrameRegistry.Unregister(Host.World);
            }

            State = GameApplicationState.Stopped;
            OperationResult result = Host.Stop(NextOperation(), string.IsNullOrEmpty(reason) ? "application stop" : reason);
            GameApplication.NotifyStopped(this);
            return result;
        }

        public void Dispose()
        {
            Stop("application root disposed");
        }

        /// <summary>The newest committed image token of the world, when it has published one (P-045).</summary>
        public bool TryGetLatestBoundary(out SnapshotToken token) => Host.Observation.TryGetLatestBoundary(out token);

        /// <summary>
        /// The capture surface of this world as the checkpoint seam declares it (O-20): the real catalog fingerprint,
        /// the live targets, the lane and the publisher, and the world's own temporal facts. A game with plugin clocks,
        /// RNG streams, next-step buffers or an outbox extends this in a later packet (P1.2).
        /// </summary>
        public CaptureContext CreateCaptureContext()
        {
            FixedStepSettings? fixedStep = Definition.FixedStep;
            return new CaptureContext(
                Host.World,
                Definition.WorldDefinition,
                Definition.CatalogHash,
                Targets,
                Registry,
                null,
                null,
                new RngStreamTable(),
                Lane.Committed.Mode,
                fixedStep != null ? fixedStep.StepDurationTicks : 0UL,
                fixedStep != null ? fixedStep.TicksPerSecond : 0UL,
                fixedStep != null ? fixedStep.MaxStepsPerPump : 1U,
                fixedStep != null && fixedStep.UsesUnscaledHostClock,
                Lane,
                Publisher);
        }

        /// <summary>The committed-boundary reader of this world over a fresh capture context (O-20, P-045).</summary>
        public UnityCommittedBoundaryReader CreateBoundaryReader() => new UnityCommittedBoundaryReader(Host, CreateCaptureContext());

        /// <summary>
        /// The restore seam (SADR-010): the definition's hook supplies the staging-world builder a checkpoint restore
        /// uses. False when the game declares no hook yet.
        /// </summary>
        public bool TryCreateRestoreTargetBuilder(out IRestoreTargetBuilder? builder)
        {
            builder = Definition.RestoreHook?.CreateRestoreTargetBuilder(this);
            return builder != null;
        }

        public override string ToString() =>
            "GameApplicationRoot{" + Definition.Name + ";world=" + Host.DiagnosticName + ";state=" + State.ToString()
            + ";lane=" + Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
            + ";epoch=" + Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
            + ";step=" + Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture) + ";" + PumpCounter + "}";

        // ------------------------------------------------------------------ composition

        /// <summary>
        /// Composes the root over a world that was just created from <paramref name="schedule"/>'s registration. Any
        /// refusal disposes what was composed so far, including the world, and returns the typed failure.
        /// </summary>
        internal static GameApplicationRoot? TryCompose(
            GameApplicationDefinition definition,
            UnityWorldHost host,
            CatalogManifestSource manifests,
            PipelineDescriptorReport schedule,
            GameApplicationBootOptions options,
            ulong firstOperationSequence,
            out GameApplicationBootFailed? failure)
        {
            failure = null;
            GameApplicationRoot? root = null;
            try
            {
                WorldId world = host.World;
                var registry = new TargetRegistry(world, (uint)definition.TargetCapacity);
                var publisher = new AssemblyPublisher(
                    host,
                    registry,
                    definition.Recipes,
                    new MigrationRegistry(definition.Migrations),
                    schedule.Descriptor!);
                var targets = new LiveTargetIndex(publisher.Recipes);
                var seeder = new LiveTargetSeeder(host, registry, targets);

                // SADR-011: the lane validates every proposal against the world before it commits. The mode-switch
                // validator keeps its P-014 behaviour and is asked first; the world preflight runs the pipeline's dry
                // run. The preflight is attached to the pipeline once both exist.
                var modeSwitch = new DerivationModeSwitchValidator(
                    definition.Values,
                    () => targets.BuildDerivationTargets().Targets,
                    definition.RuleKeys,
                    definition.Overrides,
                    null,
                    definition.ConfigBindings);
                var preflight = new DerivedAssemblyPreflightValidator(definition.Issuer);
                var validators = new CompositionEditValidatorSet(
                    new List<ICompositionEditValidator> { modeSwitch, preflight });

                CompositionLaneSeed seed = definition.ScopeSeeds.Count == 0
                    ? CompositionLaneSeed.InitialAssembly
                    : CompositionLaneSeed.InitialAssembly.WithScopes(definition.ScopeSeeds);
                CompositionHost lane = CompositionHost.CreateDefault(
                    world,
                    definition.RootScope,
                    manifests,
                    null,
                    seed,
                    validators);

                var pipeline = new DerivedAssemblyPipeline(
                    host,
                    lane,
                    publisher,
                    targets,
                    seeder,
                    definition.Values,
                    definition.RuleKeys,
                    definition.Overrides,
                    publisher.Migrations,
                    new StagedResourceGate(definition.StagedByteCeiling, definition.Issuer),
                    definition.Budget,
                    null,
                    null,
                    definition.ConfigBindings);
                preflight.Attach(pipeline);
                var bridge = new WorldCompositionBridge(host, lane, publisher, pipeline);

                GameApplicationProvenanceSettings bounds = definition.Provenance;
                var provenance = new ProvenanceStore(
                    world, bounds.EpochRetention, bounds.MaxRecordsPerEpoch, bounds.MaxEntriesPerEpoch, bounds.StagedRetention);

                root = new GameApplicationRoot(
                    definition, host, manifests, schedule, registry, publisher, targets, seeder, lane, validators,
                    modeSwitch, preflight, pipeline, bridge, provenance, firstOperationSequence);

                root.PumpCounter.AssertionsEnabled = options.PumpAssertions ?? root.PumpCounter.AssertionsEnabled;
                if (options.FrameClock != null)
                {
                    root.PumpCounter.UseFrameClock(options.FrameClock);
                }

                root.PumpCounter.SetInner(definition.AdapterFrame?.Invoke(root));
                AdapterFrameRegistry.Register(root.PumpCounter);

                // The world waits at a committed boundary while the boot script runs; Start makes it Running.
                OperationResult paused = host.SetRunState(root.NextOperation(), WorldLifecycleState.Paused);
                if (paused.Outcome == Outcome.Rejected)
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.WorldCreationFailed, paused.Code, "world",
                        "the new world could not be held at its first committed boundary: " + paused.Code);
                    root.Abandon();
                    return null;
                }

                if (!root.RunBootScript(out failure))
                {
                    root.Abandon();
                    return null;
                }

                if (!AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision, lane.Committed.Epoch, publisher.PublishedRevision, host.CurrentEpoch))
                {
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.PublicationSeriesSplit, DiagnosticCode.StalePlan, "publication",
                        "after boot the lane publishes " + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture) + " but the world "
                        + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture) + "/"
                        + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture) + " (P-006)");
                    root.Abandon();
                    return null;
                }

                return root;
            }
            catch (Exception exception)
            {
                failure = new GameApplicationBootFailed(
                    GameApplicationBootCode.CompositionFault, DiagnosticCode.ApplyFault, "compose",
                    exception.GetType().Name + ": " + exception.Message);
                if (root != null)
                {
                    root.Abandon();
                }
                else
                {
                    TryDisposeHost(host);
                }

                return null;
            }
        }

        private bool RunBootScript(out GameApplicationBootFailed? failure)
        {
            failure = null;
            IReadOnlyList<GameApplicationBootStep> steps = Definition.BootSteps;
            for (int i = 0; i < steps.Count; i++)
            {
                GameApplicationBootStep step = steps[i];
                if (step.Kind == GameApplicationBootStepKind.SeedTarget)
                {
                    if (!Seeder.TrySeed(step.Target, step.Scope, step.Recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                    {
                        failure = new GameApplicationBootFailed(
                            GameApplicationBootCode.TargetSeedRefused, code, "boot-step:" + step.Name, detail);
                        return false;
                    }

                    continue;
                }

                WorldAdmissionReport report = Submit(step.Edit!);
                bool noChange = report.Outcome == BridgeOutcome.AdmissionRejected && report.Refusal == null;
                if (report.Outcome != BridgeOutcome.Executed && !noChange)
                {
                    BridgeRefusal? refusal = report.Refusal;
                    failure = new GameApplicationBootFailed(
                        GameApplicationBootCode.BootEditRefused,
                        refusal != null ? refusal.Code : report.RefusalCode,
                        "boot-step:" + step.Name,
                        refusal != null ? refusal.ToString() : report.Outcome.ToString() + ": " + report.RefusalDetail);
                    return false;
                }
            }

            return true;
        }

        private void PublishProvenance(DerivedAssemblyReport? assembly)
        {
            DerivationResult? derivation = assembly != null && assembly.Derivation != null && assembly.Derivation.Accepted
                ? assembly.Derivation
                : null;
            if (derivation == null)
            {
                return;
            }

            if (!Host.Observation.TryGetLatestBoundary(out SnapshotToken token))
            {
                ProvenanceMisses++;
                return;
            }

            LastProvenance = DerivationProvenancePublisher.Publish(
                derivation, token, Provenance, assembly!.Plan != null ? assembly.Plan.Dispositions : null);
            if (!LastProvenance.Published)
            {
                ProvenanceMisses++;
            }
        }

        private OperationResult SetRunState(WorldLifecycleState destination, GameApplicationState state)
        {
            PumpCounter.ObserveHost();
            OperationResult result = Host.SetRunState(NextOperation(), destination);
            if (result.Outcome != Outcome.Rejected)
            {
                State = state;
            }

            return result;
        }

        private void RequireLive(string action)
        {
            if (State == GameApplicationState.Stopped)
            {
                throw new InvalidOperationException(
                    "The application root of " + Definition.Name + " is stopped and cannot " + action + " (O-19).");
            }
        }

        /// <summary>Tears down a half-composed root: no world survives a failed boot (SADR-010).</summary>
        private void Abandon()
        {
            if (AdapterFrameRegistry.TryGet(Host.World, out IAdapterFrame? registered) && ReferenceEquals(registered, PumpCounter))
            {
                AdapterFrameRegistry.Unregister(Host.World);
            }

            State = GameApplicationState.Stopped;
            TryDisposeHost(Host);
        }

        private static void TryDisposeHost(UnityWorldHost host)
        {
            try
            {
                host.Dispose();
            }
            catch (InvalidOperationException exception)
            {
                // A blocked teardown is reported, never hidden; the boot failure the caller receives already names
                // why the root does not exist (P-048).
                UnityEngine.Debug.LogError("[GameCore] a failed boot could not dispose its world: " + exception.Message);
            }
        }
    }
}
