// GameCore.Validation.ProbeHost — the GC-027 source world, its capture context and its fault path.
//
// This file is the world plumbing one GC-027 run builds, and it is the same chain of real modules GC-018's own
// scenario builds because it is the same chain in both genres:
//
//   `UnityWorldRegistry.TryCreate` + the family's generated registration   -> a real owned world with real ECS storage
//   `TargetRegistry` + `LiveTargetIndex` + `LiveTargetSeeder`              -> the family's live targets, each with its
//                                                                            own active and dormant state rows (P-032)
//   `CompositionHost` + the family's declared lane seed + `WorldCompositionBridge` -> the real control lane, so the
//                                                                            captured composition is committed state
//   `DerivedAssemblyPipeline`                                               -> the real derivation, so a recovered
//                                                                            world's capabilities are rederived
//   `WorldTimeDriver` + `PluginClockRegistry` + `RngStreamTable`            -> the clock and random-stream facts a
//                                                                            checkpoint must carry (P-038, P-053)
//   `WorldDeliveryOwner` + a recording destination port                     -> a real outbox with a real obligation
//                                                                            that is never delivered (P-045)
//   `UnityCommittedBoundaryReader` + the generated checkpoint codecs        -> the real capture half (O-20)
//
// It owns no observation and asserts nothing: it builds, captures, faults and tears down, and the scenario decides
// what the result means. Every method reports a refusal as a value with a code and a detail; none returns a silent
// false (P-052).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Delivery;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Recovery;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// One genre's source world for the GC-027 run: the world, its pipeline, its outbox and its capture context. The
    /// scenario drives it; this type owns only the bring-up, the capture and the fault.
    /// </summary>
    public sealed class Gc027SourceWorld
    {
        /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
        public const int IdlePumpFrames = 3;

        /// <summary>Ticks per idle pump frame; large enough that an idle world still admits nothing (P-036).</summary>
        public const ulong IdlePumpTicks = 1000000UL;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;
        private const ulong ScratchCapacityBytes = 4096UL;
        private const ulong ScratchBytesPerSlot = 64UL;
        private const ulong PrepareBytesLimit = 1024UL * 1024UL;
        private const int AuxiliaryEntityCount = 3;

        /// <summary>
        /// The declared capture surface's temporal facts, identical to GC-018's own scenario constants: a
        /// command-driven world declares no fixed step duration and commits at most one step per pump (P-036).
        /// </summary>
        private const ulong StepDurationTicks = 0UL;

        private const ulong TicksPerSecond = 0UL;

        private const uint MaxStepsPerPump = 1U;

        private readonly IGc027Family family;
        private readonly IdSequence sessionSequence;
        private ulong operationSequence;

        private UnityWorldHost? host;
        private TargetRegistry? registry;
        private AssemblyPublisher? publisher;
        private LiveTargetIndex? targets;
        private LiveTargetSeeder? seeder;
        private DerivedAssemblyPipeline? pipeline;
        private CompositionHost? lane;
        private WorldCompositionBridge? bridge;
        private WorldTimeDriver? time;
        private RngStreamTable? rng;
        private PipelineDescriptorReport? descriptor;
        private CheckpointCodecSet? codecs;
        private WorldDeliveryOwner? delivery;
        private Gc027RecordingDestination? destination;
        private CaptureContext? context;
        private Gc018RuntimeWorld? runtime;
        private WorldCreateRequest? request;
        private WorldId world;
        private ContentHash catalogFingerprint;
        private int auxiliaryFirstIndex = int.MaxValue;
        private int auxiliaryLastIndex = int.MinValue;
        private int registryBeforeCreate;

        public Gc027SourceWorld(IGc027Family family, IdSequence sessionSequence)
        {
            this.family = family ?? throw new ArgumentNullException(nameof(family));
            this.sessionSequence = sessionSequence ?? throw new ArgumentNullException(nameof(sessionSequence));
        }

        public IGc027Family Family => family;

        public UnityWorldHost Host => Require(host, "the source world");

        public TargetRegistry Registry => Require(registry, "the source registry");

        public AssemblyPublisher Publisher => Require(publisher, "the source publisher");

        public LiveTargetIndex Targets => Require(targets, "the source target index");

        public LiveTargetSeeder Seeder => Require(seeder, "the source seeder");

        public DerivedAssemblyPipeline Pipeline => Require(pipeline, "the source pipeline");

        public CompositionHost Lane => Require(lane, "the source lane");

        public WorldCompositionBridge Bridge => Require(bridge, "the source bridge");

        public WorldTimeDriver Time => Require(time, "the source time driver");

        public RngStreamTable Rng => Require(rng, "the source random streams");

        public CheckpointCodecSet Codecs => Require(codecs, "the source codec set");

        /// <summary>
        /// The world's delivery owner: the outbox, its journal and its event cursor (GC-021), or null for a genre
        /// that declares no delivery obligation. A caller checks <see cref="HasDelivery"/> rather than assuming one.
        /// </summary>
        public WorldDeliveryOwner? Delivery => delivery;

        /// <summary>True when this world built a delivery owner, i.e. when its genre declared an obligation (P-045).</summary>
        public bool HasDelivery => delivery != null;

        /// <summary>
        /// The recording destination port; the "test destination effect" the task names (P-045), or null for a genre
        /// with no obligation.
        /// </summary>
        public Gc027RecordingDestination? Destination => destination;

        /// <summary>The declared capture surface of this world (P-015, P-038, P-053).</summary>
        public CaptureContext Context => Require(context, "the source capture context");

        public WorldId World => world;

        public ContentHash CatalogFingerprint => catalogFingerprint;

        public WorldCreateRequest Request => Require(request, "the source create request");

        /// <summary>Registry size before this world was created; a recovery must not change it except by exposing one.</summary>
        public int RegistryBaseline => registryBeforeCreate;

        /// <summary>Native entity indices this world's live targets occupy; the evidence that a restore re-seeded them.</summary>
        public IReadOnlyList<int> TargetIndices
        {
            get
            {
                var indices = new List<int>();
                IReadOnlyList<LiveTarget> live = Targets.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    if (Registry.TryResolveTarget(live[i].Target, out TargetHandle _, out Entity entity))
                    {
                        indices.Add(entity.Index);
                    }
                }

                return indices;
            }
        }

        /// <summary>The auxiliary indices this world created, so a restored world's block can be shown to differ (P-005).</summary>
        public string AuxiliaryIndexRange => auxiliaryFirstIndex.ToString(CultureInfo.InvariantCulture) + ".."
            + auxiliaryLastIndex.ToString(CultureInfo.InvariantCulture);

        /// <summary>Builds the world, its composition, its live state, its outbox and its capture context.</summary>
        public bool TryBuild(out string detail)
        {
            detail = string.Empty;
            try
            {
                descriptor = family.CompilePipeline();
                if (!descriptor.Succeeded || descriptor.Descriptor == null || descriptor.Adaptation == null
                    || descriptor.Compilation == null)
                {
                    detail = "the ownership and schedule pipeline refused: " + descriptor.Describe();
                    return false;
                }

                if (!ContentHash.TryParseHex(family.CatalogFingerprint, out catalogFingerprint))
                {
                    detail = "the family's catalog fingerprint literal is not 64 lowercase hex characters: "
                        + family.CatalogFingerprint;
                    return false;
                }

                if (!Gc018CheckpointCodecs.TryBuild(out CheckpointSerializerBindings? bindings, out CheckpointCodecSet? builtCodecs, out string codecDetail)
                    || builtCodecs == null || bindings == null)
                {
                    detail = "the committed checkpoint catalog's serializers could not be bound: " + codecDetail;
                    return false;
                }

                codecs = builtCodecs;
                registryBeforeCreate = UnityWorldRegistry.Count;
                world = new WorldId(sessionSequence.Next());
                request = family.CreateRequest(world, NextOperation(world));
                bool created = UnityWorldRegistry.TryCreate(
                    request!,
                    family.CreateRegistration(descriptor.Adaptation),
                    out UnityWorldHost? createdHost,
                    out WorldCreateResult result);
                host = createdHost;
                if (!created || host == null)
                {
                    detail = "world creation failed: " + result.Code + ": " + result.Detail;
                    return false;
                }

                registry = new TargetRegistry(world, 16);
                publisher = new AssemblyPublisher(
                    host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor);
                targets = new LiveTargetIndex(publisher.Recipes);
                seeder = new LiveTargetSeeder(host, registry, targets);

                IDerivationValueSource valueSource = family.CreateValues();
                lane = CompositionHost.CreateDefault(
                    world,
                    family.WorldRootScope,
                    new CatalogManifestSource(family.Catalog, family.Declarations),
                    null,
                    family.LaneSeed,
                    new DerivationModeSwitchValidator(valueSource, TargetView));
                bridge = new WorldCompositionBridge(host, lane, publisher);
                pipeline = new DerivedAssemblyPipeline(
                    host,
                    lane,
                    publisher,
                    targets,
                    seeder,
                    valueSource,
                    null,
                    null,
                    publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, family.Issuer),
                    new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                time.AdoptResourceTable(descriptor.Adaptation.NativeTable!);

                // The declared persistent clock, so the capture carries a clock fact (P-038, P-053).
                var clockSpec = new PluginClockSpec(
                    family.WakeClockId,
                    "gc027-" + family.Label,
                    PluginClockKind.LogicalStep,
                    WakePausePolicy.Defer,
                    true);
                if (!time.Clocks.TryRegister(clockSpec, out DiagnosticCode clockCode))
                {
                    detail = "registering the declared plugin clock was refused: " + clockCode;
                    return false;
                }

                // The random streams whose positions the checkpoint saves (P-008, P-053).
                rng = new RngStreamTable();
                for (int i = 0; i < 2; i++)
                {
                    var streamId = new Id128(family.WakeClockId.High ^ 0x5247UL, 200UL + (ulong)i);
                    if (!rng.TryDeclare(streamId, 11UL + (ulong)i, 0x2718UL + (ulong)i, out RngStream? stream, out string declareDetail)
                        || stream == null)
                    {
                        detail = "declaring random stream " + streamId.ToString() + " was refused: " + declareDetail;
                        return false;
                    }

                    stream.Next();
                    stream.Next();
                }

                // Auxiliary entities before the targets, so this world's native index block and a recovered world's
                // cannot be mistaken for each other (P-005: an index is never an identity).
                EntityManager entityManager = host.EntityWorld.EntityManager;
                for (int i = 0; i < AuxiliaryEntityCount; i++)
                {
                    Entity auxiliary = entityManager.CreateEntity();
                    auxiliaryFirstIndex = Math.Min(auxiliaryFirstIndex, auxiliary.Index);
                    auxiliaryLastIndex = Math.Max(auxiliaryLastIndex, auxiliary.Index);
                }

                // A genre whose world definition already carries its tree in the lane seed must NOT publish its
                // declared creates: that would be a duplicate create for scopes the seed owns (P-010, P-006). The
                // capability is the family's own answer, not a genre test here (P-001).
                if (family.PublishesDeclaredSetupEdits && !PublishEdits(family.SetupEdits, out detail))
                {
                    return false;
                }

                if (!family.SeedTargets(new Gc013WorldContext(host, targets, seeder)))
                {
                    detail = "the family's declared targets could not be seeded (P-010, P-024).";
                    return false;
                }

                if (!seeder.TrySeedSlot(
                        family.DormantTarget,
                        family.DormantOwner,
                        family.DormantSlot,
                        family.DormantVersion,
                        family.DormantValue,
                        false,
                        out DiagnosticCode dormantCode,
                        out string dormantDetail))
                {
                    detail = "seeding the declared dormant slot was refused: " + dormantCode + ": " + dormantDetail;
                    return false;
                }

                // The composition the capture carries: one boundary enrichment, one provider mount in Automatic mode,
                // so the recovered world has a non-empty, rederived capability surface (P-013, P-016).
                if (!PublishEdit(family.SpareScopeEdits[0], "spare-scope", out detail)
                    || !PublishEdit(family.BoundaryEnrichment(), "boundary-enrichment", out detail)
                    || !PublishEdit(family.MountProvider(), "provider-mount", out detail))
                {
                    return false;
                }

                if (!TryBuildDelivery(out detail))
                {
                    return false;
                }

                // The family's runtime needs the compiled descriptor before it attaches (the traversal course passes
                // it to its stage runtime; the other two ignore it), so the runner notes it once here (GC-009).
                family.NotePipelineForAttach(descriptor);


                // The genre's own runtime module, so the world's ingress stage consumes its lane like any other
                // world of this genre; a recovered world attaches the same module (P-037, P-042).
                var runtimeWorld = new Gc027RuntimeWorld(host, targets, seeder, descriptor.Compilation.Schedule);
                if (!family.TryAttachRuntime(runtimeWorld.ToGc018RuntimeWorld(), out string attachDetail))
                {
                    detail = "the family runtime could not attach to the source world: " + attachDetail;
                    return false;
                }

                runtime = runtimeWorld.ToGc018RuntimeWorld();

                if (host.Lifecycle != WorldLifecycleState.Running)
                {
                    detail = "the source world is " + host.Lifecycle.ToString() + " and must be running (P-035).";
                    return false;
                }

                // The delivery state is optional: a genre with no obligation has no outbox, so the detail reports
                // that fact rather than dereferencing an owner the family never declared (P-045).
                detail = "source session " + world.Session.ToString() + " with "
                    + targets.Count.ToString(CultureInfo.InvariantCulture) + " live target(s), "
                    + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture) + " committed step(s) and "
                    + (delivery == null
                        ? "no delivery obligation"
                        : delivery.Outbox.Count.ToString(CultureInfo.InvariantCulture) + " outbox row(s)")
                    + ".";
                return true;
            }
            catch (Exception exception)
            {
                detail = "building the source world threw " + exception.GetType().FullName + ": " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Commits the family's one delivery obligation into the world's outbox WITHOUT delivering it: a recovery
        /// carries an owed obligation across a restart, and an obligation that was already delivered would make
        /// "delivery cursor intact" and "the destination effect is not duplicated" unobservable (P-045, P-049).
        /// </summary>
        public bool TryCommitUndeliveredObligation(out Id128 outboxId, out string detail)
        {
            outboxId = default(Id128);
            detail = string.Empty;
            if (!family.HasDeliveryObligation)
            {
                // No obligation, no failure: the observation that reads this is skipped for such a genre, and the
                // run records why rather than a zero that could be mistaken for a committed obligation (P-045).
                detail = "genre '" + family.Label + "' declares no delivery obligation, so the run commits none.";
                return false;
            }

            WorldDeliveryOwner owner = Delivery;
            WorldId session = world;
            // A settled predecessor gives the checkpoint a non-default delivery cursor as well as an open
            // obligation. Recovery must preserve both without replaying either destination effect.
            DeliveryKey settledKey = DeliveryKey.Derive(
                session, new EventSequence(1UL), family.DeliveryDestinationId, family.DeliveryCommandSchema);
            if (owner.Adapter.TryCommit(
                    settledKey, family.DeliveryPayloadSchema, family.DeliveryPayload(), new EventSequence(1UL),
                    host!.CurrentStep, host.CurrentEpoch, new OperationId(session, family.Issuer, 2UL), true,
                    out DeliveryObligation? settled, out DiagnosticCode settledCode, out string settledDetail)
                != OutboxAdmission.Accepted || settled == null)
            {
                detail = "committing the cursor predecessor was refused: " + settledCode + ": " + settledDetail;
                return false;
            }

            if (owner.Adapter.TryAcknowledge(settled.Key.OutboxId, out settledCode, out settledDetail)
                != DeliveryOutcome.Acknowledged)
            {
                detail = "persisting the cursor predecessor was refused: " + settledCode + ": " + settledDetail;
                return false;
            }

            DeliveryKey key = DeliveryKey.Derive(
                session,
                new EventSequence(2UL),
                family.DeliveryDestinationId,
                family.DeliveryCommandSchema);
            OutboxAdmission admission = owner.Adapter.TryCommit(
                key,
                family.DeliveryPayloadSchema,
                family.DeliveryPayload(),
                new EventSequence(2UL),
                host!.CurrentStep,
                host.CurrentEpoch,
                new OperationId(session, family.Issuer, 1UL),
                family.OutboxDurabilityClass == OutboxDurability.Durable,
                out DeliveryObligation? obligation,
                out DiagnosticCode code,
                out string commitDetail);
            if (admission != OutboxAdmission.Accepted || obligation == null)
            {
                detail = "committing the family's delivery obligation was refused: " + admission + "/" + code
                    + ": " + commitDetail;
                return false;
            }

            outboxId = obligation.Key.OutboxId;
            detail = "obligation " + outboxId.ToString() + " is open and undelivered; destination "
                + family.DeliveryDestinationId.ToString() + ".";
            return owner.Outbox.OpenCount == 1;
        }

        /// <summary>Refreshes the declared capture surface after the outbox has committed its obligation.</summary>
        public void RefreshCaptureContext()
        {
            context = new CaptureContext(
                    world,
                    request.Definition,
                    catalogFingerprint,
                    targets,
                    registry,
                    time.Clocks.Clocks,
                    time.Clocks,
                    rng,
                    lane.Committed.Mode,
                    // The temporal facts are the world's own declaration: a command-driven world declares no step
                    // duration, and a fixed-step course declares its 20 ms step and its catch-up bound. A capture that
                    // recorded anything else would describe a world the header does not name (P-036, P-053).
                    request.FixedStep?.StepDurationTicks ?? 0UL,
                    request.FixedStep?.TicksPerSecond ?? 0UL,
                    request.FixedStep?.MaxStepsPerPump ?? 1U,
                    false,
                    lane,
                    publisher,
                    Array.Empty<BufferId>(),
                    null,
                    // A genre with no delivery obligation captures no outbox rows, which is the honest answer of a
                    // world that has no outbox rather than a claim that its outbox was empty (P-045, P-053).
                    delivery?.ToRecords());
        }

        /// <summary>Captures this world's committed boundary and publishes it to the store (O-20, 06 s7).</summary>
        public CheckpointPublicationResult CaptureAndPublish(ICheckpointStore store, RecoveryTranscript transcript)
        {
            RefreshCaptureContext();
            var reader = new UnityCommittedBoundaryReader(Host, Context);
            return CheckpointPublication.CaptureAndPublish(
                new CheckpointPublicationRequest(
                    reader,
                    new CheckpointCaptureRequest(world, Codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint),
                    store,
                    NextOperation(world)),
                transcript);
        }

        /// <summary>
        /// Faults the source world after its first live write, through the real publisher: the family's own fault
        /// edit is applied and the publication is driven with the latch armed, so the publisher really writes and
        /// then fails inside its fence. The world fail-stops: `Faulted`, `ApplyFault`, no new epoch (P-031).
        /// </summary>
        public bool TryFaultAfterFirstLiveWrite(out string detail)
        {
            detail = string.Empty;
            if (host == null || pipeline == null || lane == null)
            {
                detail = "the source world or its pipeline is missing.";
                return false;
            }

            if (!ApplyEdit(family.FaultEdit(), "fault-edit", out detail))
            {
                return false;
            }

            CompositionRevision revisionBefore = publisher!.PublishedRevision;
            int reachesBefore = host.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite);
            host.Faults.Arm(FaultBoundary.FirstLiveWrite);
            DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(world));
            host.Faults.Disarm(FaultBoundary.FirstLiveWrite);

            bool reached = host.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite) == reachesBefore + 1;
            bool faulted = host.Lifecycle == WorldLifecycleState.Faulted
                && host.FaultCode == DiagnosticCode.ApplyFault
                && host.FaultCount > 0;
            bool noNewRevision = publisher.PublishedRevision.Equals(revisionBefore);

            if (!reached || !faulted || !noNewRevision)
            {
                detail = "the postwrite fault did not fail-stop the world as required: reached=" + reached
                    + " lifecycle=" + host.Lifecycle.ToString() + "/" + host.FaultCode
                    + " revision=" + revisionBefore.Value.ToString(CultureInfo.InvariantCulture) + "->"
                    + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + " outcome=" + report.Outcome.ToString() + " (" + report.Detail + ")";
                return false;
            }

            detail = "the source world faulted after its first live write: session " + world.Session.ToString()
                + ", fault=" + host.FaultCode + ", revision stayed "
                + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture) + ".";
            return true;
        }

        /// <summary>
        /// Admits this genre's declared steps before the capture, so the state a recovery carries is a value that has
        /// really advanced rather than a seed. Each admitted step is one input submitted through the world's own
        /// ingress and one pump at the declared step interval; when the genre declares an engine physical domain, the
        /// gate is stepped exactly once for the step that committed, which is the caller obligation 04 s7 states
        /// (P-036, REF-A06).
        ///
        /// The returned counts are the world's own answers: the committed steps its driver reports and the engine
        /// simulations its gate admitted.
        /// </summary>
        public bool TryRunAdmittedSteps(out ulong committed, out int engineSteps, out string detail)
        {
            committed = 0UL;
            engineSteps = 0;
            detail = string.Empty;
            if (host == null || time == null)
            {
                detail = "the world or its time driver is not built.";
                return false;
            }
            DiagnosticCode stateCode = DiagnosticCode.None;
            string stateDetail = string.Empty;
            uint declared = family.AdmittedStepsBeforeFault;

            for (uint i = 0U; i < declared; i++)
            {
                CommandEnvelope? input = family.StepInput(world, NextOperation(world));
                if (input != null)
                {
                    CommandAdmissionReceipt receipt = host.Submit(input);
                    if (!receipt.Admitted)
                    {
                        detail = "the step input for admitted step " + i.ToString(CultureInfo.InvariantCulture)
                            + " was not admitted: " + receipt.Result.Kind + "/" + receipt.Result.Reason;
                        return false;
                    }
                }

                // One declared step of host time, so a fixed-step world commits exactly one logical step and a
                // command-driven world commits none (P-036).
                hostTicks += family.FixedStep?.StepDurationTicks ?? IdlePumpTicks;
                ulong committedNow = time.PumpFrame(hostTicks).StepsCommitted;
                committed += committedNow;

                Gc027PhysicsDomain? physics = family.PhysicsDomain;
                if (physics == null || committedNow == 0UL)
                {
                    continue;
                }

                // The engine is stepped once for the step that really committed, and never for a pump that committed
                // nothing: a presentation rate must not advance the authority twice (04 s7, REF-A06).
                PhysicsStepOutcome outcome = physics.TryStepOnce(host.CurrentStep.Value, family.FixedStepSeconds);
                if (outcome != PhysicsStepOutcome.Stepped)
                {
                    detail = "the engine physics step for admitted step "
                        + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture) + " was " + outcome + ".";
                    return false;
                }

                engineSteps++;
            }

            // The genre's authoritative ECS state is persisted BEFORE the capture's boundary read, while the world
            // holds exactly the values its admitted steps produced: the checkpoint's slot rows then carry the moved
            // pose, the advanced velocity and the accepted-checkpoint progress rather than the seed's values
            // (P-053, 07 s4.3).
            if (!family.TryCaptureAuthoritativeState(host, seeder!, out stateCode, out stateDetail))
            {
                detail = "persisting the genre's authoritative state before the capture was refused: "
                    + stateCode + ": " + stateDetail;
                return false;
            }

            detail = "committed=" + committed.ToString(CultureInfo.InvariantCulture) + "; engineSteps="
                + engineSteps.ToString(CultureInfo.InvariantCulture) + "; declared="
                + declared.ToString(CultureInfo.InvariantCulture) + "; authoritativeState=persisted";
            return true;
        }

        /// <summary>The host-time accumulator this world's pumps advance; only the step loop reads it (P-036).</summary>
        private ulong hostTicks;

        /// <summary>Pumps the world a bounded number of idle frames; an idle command-driven world steps zero times.</summary>
        public ulong PumpIdleFrames()
        {
            ulong committed = 0UL;
            for (int i = 0; i < IdlePumpFrames; i++)
            {
                // The world's own time driver counts the steps it committed, so an idle command-driven world's
                // answer is a number this world produced rather than a count the scenario kept (P-036).
                committed += Require(time, "the source time driver").PumpFrame(IdlePumpTicks).StepsCommitted;
            }

            return committed;
        }

        /// <summary>Stops and disposes the source world, and detaches the family runtime it attached (P-048).</summary>
        public void TearDown()
        {
            if (runtime != null)
            {
                family.DetachRuntime(runtime);
                runtime = null;
            }

            delivery?.Dispose();
            delivery = null;

            if (host != null && host.Lifecycle != WorldLifecycleState.Disposed)
            {
                host.Stop(new OperationId(world, family.Issuer, NextSequence()), "gc-027 teardown");
                if (host.Lifecycle != WorldLifecycleState.Disposed)
                {
                    host.Dispose();
                }
            }

            host = null;
        }

        /// <summary>Reserves one operation identity of this run, strictly increasing per issuer (P-050).</summary>
        public OperationId NextOperation(WorldId target) => new OperationId(target, family.Issuer, NextSequence());

        private ulong NextSequence()
        {
            operationSequence++;
            return operationSequence;
        }

        private static T Require<T>(T? value, string what) where T : class =>
            value == null ? throw new InvalidOperationException(what + " is not built.") : value;

        /// <summary>
        /// The world's targets as the derivation planner's view, exactly as GC-018's own builder reads them: the
        /// index builds the view, so a target that cannot be described contributes nothing rather than a guess.
        /// </summary>
        private IReadOnlyList<DerivationTarget> TargetView()
        {
            if (targets == null)
            {
                return Array.Empty<DerivationTarget>();
            }

            DerivationInputTargets view = targets.BuildDerivationTargets();
            return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
        }

        /// <summary>
        /// Builds this world's delivery state when its genre declares a delivery obligation, and leaves it unbuilt
        /// when it does not. A genre with no external effect has no outbox and no destination, and inventing one for
        /// it would claim an endpoint the protocol says only the recipient's package may name (P-003, P-045).
        /// </summary>
        private bool TryBuildDelivery(out string detail)
        {
            detail = string.Empty;
            if (!family.HasDeliveryObligation)
            {
                delivery = null;
                destination = null;
                return true;
            }

            delivery = new WorldDeliveryOwner(
                host!,
                family.Issuer,
                family.OutboxCapacity,
                family.OutboxTerminalRetention,
                family.OutboxDurabilityClass,
                new MemoryDeliveryJournal("memory://gc027/" + family.Label),
                null);
            destination = new Gc027RecordingDestination(
                family.DeliveryDestinationId, family.DeliveryCommandSchema);
            if (!delivery.TryRegisterDestination(destination, out detail))
            {
                return false;
            }

            return true;
        }

        private bool PublishEdits(IReadOnlyList<CompositionEditPayload> edits, out string detail)
        {
            for (int i = 0; i < edits.Count; i++)
            {
                if (!PublishEdit(edits[i], "setup-" + i.ToString(CultureInfo.InvariantCulture), out detail))
                {
                    return false;
                }
            }

            detail = string.Empty;
            return true;
        }

        /// <summary>Applies one edit and answers it with a real assembly publication (P-006, P-030).</summary>
        private bool PublishEdit(CompositionEditPayload payload, string label, out string detail)
        {
            if (!ApplyEdit(payload, label, out detail))
            {
                return false;
            }

            DerivedAssemblyReport report = pipeline!.PublishDerived(NextOperation(world));
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                detail = label + ": the world refused the assembly: " + report.Describe();
                return false;
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = publisher!.PublishUnchangedAssembly(
                    NextOperation(world), lane!.Committed.Revision, lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    detail = label + ": the unchanged assembly was refused: " + unchanged.Detail;
                    return false;
                }
            }

            detail = string.Empty;
            return true;
        }

        private bool ApplyEdit(CompositionEditPayload payload, string label, out string detail)
        {
            if (lane == null || host == null)
            {
                detail = label + ": the world or its lane is missing.";
                return false;
            }

            EditAdmission admission = lane.SubmitEdit(payload, NextOperation(world), lane.Committed.Revision);
            if (!admission.Staged)
            {
                detail = label + ": the edit was refused by the lane (" + admission.Kind + "/" + admission.Code + ")";
                return false;
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                detail = label + ": the lane did not publish the edit (" + published.Count.ToString(CultureInfo.InvariantCulture)
                    + " operation(s)).";
                return false;
            }

            detail = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// The test destination of the GC-027 run: it records every attempt's idempotency key and mutates exactly once
    /// per key, so "the replayed external delivery does not duplicate the destination effect" is a count rather than
    /// a claim (P-045).
    /// </summary>
    public sealed class Gc027RecordingDestination : IDestinationPort
    {
        private readonly List<Id128> appliedKeys = new List<Id128>();
        private readonly HashSet<Id128> appliedSet = new HashSet<Id128>();

        public Gc027RecordingDestination(Id128 destinationId, SchemaRef commandSchema)
        {
            DestinationId = destinationId;
            CommandSchema = commandSchema;
        }

        public Id128 DestinationId { get; }

        public SchemaRef CommandSchema { get; }

        /// <summary>Attempts the destination was asked to apply, redeliveries included.</summary>
        public int AttemptCount { get; private set; }

        /// <summary>Attempts that mutated the destination; the effect count (P-045).</summary>
        public int AppliedCount { get; private set; }

        /// <summary>Attempts recognised by their idempotency key and therefore not applied again (P-045).</summary>
        public int AlreadyAppliedCount { get; private set; }

        /// <summary>Every key that mutated the destination, in first-seen order.</summary>
        public IReadOnlyList<Id128> AppliedKeys => appliedKeys;

        /// <summary>The one effect this destination ever produced, or the default id when it produced none.</summary>
        public Id128 SoleEffectKey => appliedKeys.Count == 0 ? default(Id128) : appliedKeys[0];

        /// <summary>True when at most one distinct effect was produced, which is the exactly-once observation.</summary>
        public bool EffectIsSingle => appliedKeys.Count <= 1;

        public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            AttemptCount++;

            if (!attempt.DestinationId.Equals(DestinationId))
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "attempt " + attempt.OutboxId.ToString() + " belongs to destination "
                    + attempt.DestinationId.ToString() + " and this port owns " + DestinationId.ToString()
                    + " (P-034).";
                return DestinationOutcome.Refused;
            }

            if (!attempt.PayloadSchema.Equals(CommandSchema))
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "attempt " + attempt.OutboxId.ToString() + " carries schema "
                    + attempt.PayloadSchema.ToString() + " and this port accepts " + CommandSchema.ToString()
                    + " (P-054).";
                return DestinationOutcome.Refused;
            }

            if (appliedSet.Contains(attempt.IdempotencyKey))
            {
                AlreadyAppliedCount++;
                detail = "the destination already applied idempotency key "
                    + attempt.IdempotencyKey.ToString() + "; this attempt mutates nothing (P-045).";
                return DestinationOutcome.AlreadyApplied;
            }

            appliedSet.Add(attempt.IdempotencyKey);
            appliedKeys.Add(attempt.IdempotencyKey);
            AppliedCount++;
            detail = "applied idempotency key " + attempt.IdempotencyKey.ToString() + " for obligation "
                + attempt.OutboxId.ToString() + " (attempt #"
                + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture) + ").";
            return DestinationOutcome.Applied;
        }

        public override string ToString() =>
            "recordingDestination(" + DestinationId.ToString() + ",attempts="
            + AttemptCount.ToString(CultureInfo.InvariantCulture) + ",applied="
            + AppliedCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The deterministic crash point of the GC-027 run: GC-021's own `IDeliveryStepHook` seam, reached at a named
    /// delivery boundary, raising a distinct exception type so a run can tell an injected fault from a real defect.
    /// It is the same mechanism GC-021's own crash fixture uses; this task adds no second one (P-045, 04 s6).
    /// </summary>
    public sealed class Gc027DeliveryCrashHook : IDeliveryStepHook
    {
        private readonly List<string> reaches = new List<string>();

        public Gc027DeliveryCrashHook(string? crashAt)
        {
            CrashAt = crashAt ?? string.Empty;
        }

        /// <summary>
        /// The boundary this hook raises at, or empty to only report (04 s6's "reporting seam"). It is settable so
        /// an observation can disarm the hook after its crash and drive the redelivery half against the same hook.
        /// </summary>
        public string CrashAt { get; set; }

        /// <summary>Boundaries this hook was asked to report, in order.</summary>
        public IReadOnlyList<string> Reaches => reaches;

        public int CrashCount { get; private set; }

        public void Reach(string boundary, string detail)
        {
            reaches.Add(boundary);
            if (CrashAt.Length != 0 && string.Equals(CrashAt, boundary, StringComparison.Ordinal))
            {
                CrashCount++;
                throw new Gc027DeliveryCrashException(boundary, detail);
            }
        }

        public override string ToString() => "crashHook(at=" + (CrashAt.Length == 0 ? "<none>" : CrashAt)
            + ",reaches=" + reaches.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The exception a <see cref="Gc027DeliveryCrashHook"/> raises; distinct so a catch can tell it apart.</summary>
    public sealed class Gc027DeliveryCrashException : Exception
    {
        public Gc027DeliveryCrashException(string boundary, string detail)
            : base("delivery crashed at " + boundary + ": " + (detail ?? string.Empty))
        {
            Boundary = boundary;
        }

        /// <summary>The `DeliveryBoundaries` name the crash happened at.</summary>
        public string Boundary { get; }
    }

    /// <summary>
    /// A store wrapper that refuses the first N reads with a transient `ResourceUnavailable`, so P-049's
    /// host-configured bounded retry is exercised against a real transient failure rather than a fabricated code.
    /// Every other call is the real store's own (P-052).
    /// </summary>
    public sealed class Gc027FlakyStore : ICheckpointStore
    {
        private readonly ICheckpointStore inner;
        private int refusalsLeft;

        public Gc027FlakyStore(ICheckpointStore inner, int refusals)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            refusalsLeft = refusals;
        }

        public string Location => inner.Location;

        public bool Exists => inner.Exists;

        /// <summary>Reads this wrapper refused transiently; one per retry the recovery had to make.</summary>
        public int TransientRefusalCount { get; private set; }

        public bool TryPublish(byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail) =>
            inner.TryPublish(document, out stored, out code, out detail);

        public bool TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail)
        {
            if (refusalsLeft > 0)
            {
                refusalsLeft--;
                TransientRefusalCount++;
                document = null;
                stored = default(StoredCheckpoint);
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the store is transiently unavailable (refusal "
                    + TransientRefusalCount.ToString(CultureInfo.InvariantCulture)
                    + " of this run); a retryable transient failure under the host's bound (P-049).";
                return false;
            }

            return inner.TryRead(out document, out stored, out code, out detail);
        }

        public bool TryRemove(out string detail) => inner.TryRemove(out detail);

        public override string ToString() => "flakyStore(" + inner.Location + ",transient="
            + TransientRefusalCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
