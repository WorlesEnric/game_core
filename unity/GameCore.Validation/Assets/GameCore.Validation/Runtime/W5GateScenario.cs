// GameCore.Validation.ProbeHost - the Wave 5 integration gate, over one real world per family (W5-GATE).
//
// The gate sentence, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 5):
//
//   "Join retained observation, deterministic faults, checkpoint restore and common adapters in one actual world.
//    Show prewrite rejection, postwrite fail-stop, new-session restore, read-only snapshots and stale asset callback
//    rejection."
//
// This file is the join, and the only thing it owns is the order. Every module it drives is a finished Wave 5 task
// reached through the world that owns it:
//
//   * GC-016's retained observation: `host.Observation` leases bounded, pinned, read-only boundaries. The gate takes
//     one, holds it across a real publication, and proves the bytes it holds are the bytes of its own token.
//   * GC-017's deterministic fault latch: `host.Faults` is armed at named boundaries and the *real* apply path
//     reaches them, so the prewrite refusal and the postwrite fail-stop are the production outcomes, not a fixture.
//   * GC-018's checkpoint: the capture reads the committed boundary through the reconciled lease seam, and the
//     restore rebuilds an unexposed world that is exposed only after validation.
//   * GC-019's adapters: stamped input, bounded asynchronous asset leases and committed-output presentation, bound
//     to the *restored* world, while the retired world's callbacks are rejected.
//
// One world is enough for the fault pair, and the gate proves it that way: the composition publication the prewrite
// fault refuses is left adopted-and-pending by the publisher (P-006 forbids publishing it twice), so the postwrite
// step retries exactly that pending publication and the world fail-stops on it. That is why the gate does not stand
// up a second world for the second fault: the two faults must be shown against one world's one publication series,
// or "the old world never resumes" would be a claim about a world nobody tried to resume.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Fixtures;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;
using CompositionProposal = GameCore.Planning.CompositionProposal;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Runs the Wave 5 integration gate over one family and one catalog.</summary>
    public static class W5GateScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling gates use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Fixed-step declarations of the world this gate builds; all three are recorded in the header.</summary>
        public const ulong StepDurationTicks = 0UL;

        public const ulong TicksPerSecond = 0UL;

        public const uint MaxStepsPerPump = 1U;

        /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        private const int IdlePumpFrames = 4;

        /// <summary>Steps the declared wake is deferred by, so a capture carries a real pending wake (P-038).</summary>
        private const ulong WakeDelaySteps = 5UL;

        /// <summary>Random streams the world declares and advances, so a capture carries real RNG state (P-008).</summary>
        private const int RngStreamCount = 2;

        private const int RngDrawsPerStream = 3;

        /// <summary>
        /// Entities created before the targets, so the gate reports two worlds' native index blocks as evidence
        /// rather than assuming they differ (P-005: an index is never an identity).
        /// </summary>
        private const int AuxiliaryEntityCount = 4;

        /// <summary>Adapter budgets of the gate's worlds: bounded leases, bounded views, bounded pending input.</summary>
        private const ulong AssetByteBudget = 4096UL;

        private const ulong LeaseBytes = 128UL;

        private const uint AdapterTableCapacity = 8U;

        private const uint ViewCapacity = 64U;

        private const uint PendingInputCapacity = 8U;

        /// <summary>Staged-resource and plan budgets of the gate's plans, as the family gates declare them.</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>Bound on the committed events one boundary lease exposes.</summary>
        private const int BoundaryEventWindow = 8;

        private const ulong InputSourceLow = 0x773567617465696EU;

        private const ulong LeaseResourceLow = 0x7735676174656C65UL;

        /// <summary>
        /// The gate's observations, in execution order, without the family qualification. Both families record exactly
        /// these names, so a renamed or dropped observation fails the EditMode suite and the player probe instead of
        /// shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "w5gate-one-world-with-retained-observation",
            "w5gate-pinned-snapshots-are-read-only",
            "w5gate-checkpoint-from-the-committed-boundary",
            "w5gate-prewrite-fault-keeps-the-old-assembly",
            "w5gate-postwrite-fault-fail-stops-the-world",
            "w5gate-restore-into-a-new-session",
            "w5gate-adapters-bind-to-the-restored-world",
            "w5gate-retired-world-callbacks-are-rejected",
        };

        /// <summary>The observation names of one run, qualified with the family label, in order.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        public static W5GateScenarioResult Run(IW5GateFamily family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        /// <summary>
        /// The gate's own executor: one scripted sequence over one family, holding the source world, the restored
        /// world and the adapters bound to each. It is a runner, not a module: it owns no protocol behaviour and it
        /// never re-implements a check a module already performs (it asserts the module's own outcome).
        /// </summary>
        private sealed class Executor
        {
            private readonly IW5GateFamily family;
            private readonly List<W5GateStep> steps = new List<W5GateStep>();
            private readonly IdSequence sessionSequence;

            // The source world (A) and the modules joined to it.
            private UnityWorldHost? host;
            private PipelineDescriptorReport? descriptor;
            private TargetRegistry? registry;
            private AssemblyPublisher? publisher;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private CompositionHost? lane;
            private WorldCompositionBridge? bridge;
            private DerivedAssemblyPipeline? pipeline;
            private WorldTimeDriver? time;
            private RngStreamTable? rng;
            private Gc019StageRuntime? stageRuntime;
            private PluginInstanceId providerInstance;
            private bool providerInstanceKnown;
            private ContentHash catalogFingerprint;
            private WorldId sourceWorld;
            private WorldCreateRequest sourceRequest;
            private int registryBaseline;
            private ulong operationSequence;

            // The checkpoint capture and the declaration the capture reads it under.
            private CheckpointSerializerBindings? bindings;
            private CheckpointCodecSet? codecs;
            private CaptureContext? sourceContext;
            private CheckpointCaptureResult? capture;
            private Dictionary<OperationId, FrozenPayload> commandPayloads = new Dictionary<OperationId, FrozenPayload>();
            private OperationId queuedOperation;
            private IReadOnlyList<SlotRecordValue> capturedSlots = Array.Empty<SlotRecordValue>();
            private WorldBoundaryFacts? boundaryFacts;

            // The pending composition publication the prewrite refusal leaves behind (P-006), and its proposal.
            private DerivationProposalReport? pendingProposal;
            private OperationId refusedOperation;

            // Adapters bound to the source world.
            private TypedInputIngress? ingressA;
            private PendingInputCompletionTable? pendingA;
            private DeterministicAssetBackend? backendA;
            private AssetLeaseTable? assetsA;
            private ViewRegistry? viewsA;
            private RecordingViewBinder? binderA;
            private CommittedImageSource? sourceA;
            private CommittedOutputPresenter? presenterA;
            private InputBindingTable? bindingsA;
            private WorldAdapterFrame? frameA;

            // The restored world (B) and the adapters bound to it.
            private Gc018FamilyRestoreBuilder? builder;
            private RestoreOutcome? restoreOutcome;
            private UnityWorldHost? restoredHost;
            private WorldId restoredSession;
            private TypedInputIngress? ingressB;
            private AssetLeaseTable? assetsB;
            private DeterministicAssetBackend? backendB;
            private ViewRegistry? viewsB;
            private RecordingViewBinder? binderB;
            private CommittedImageSource? sourceB;
            private CommittedOutputPresenter? presenterB;
            private InputBindingTable? bindingsB;
            private WorldAdapterFrame? frameB;

            private string lastFailure = string.Empty;

            public Executor(IW5GateFamily family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public W5GateScenarioResult Run()
            {
                BuildOneWorld();
                ProvePinnedSnapshotsAreReadOnly();
                CaptureCheckpointAtTheCommittedBoundary();
                RefuseThePrewriteFaultAndKeepTheOldAssembly();
                FaultTheWorldAfterItsFirstLiveWrite();
                RestoreIntoANewSession();
                BindAdaptersToTheRestoredWorld();
                RejectTheRetiredWorldsCallbacks();

                return new W5GateScenarioResult(family.Label, steps);
            }

            // ================================================================== 1. one real world

            /// <summary>
            /// Builds the one real world this run faults, captures and restores: the family's compiled schedule, its
            /// live targets, the control lane over the family's own manifest source, the composition bridge, the
            /// derived-assembly pipeline, the command-driven time driver, its declared clock, its random streams and
            /// its queued command - then mounts the family's providers, seeds the declared dormant row, applies the
            /// one boundary enrichment and registers the adapter frame of the world. Nothing here is a model of the
            /// kernel: every object is the production module the earlier gates run (P-002, P-030, P-042).
            /// </summary>
            private void BuildOneWorld()
            {
                const string name = "w5gate-one-world-with-retained-observation";
                try
                {
                    descriptor = family.CompilePipeline();
                    if (!descriptor.Succeeded || descriptor.Descriptor == null || descriptor.Adaptation == null
                        || descriptor.Compilation == null)
                    {
                        Add(name, false, "the ownership and schedule pipeline refused: " + descriptor.Describe());
                        return;
                    }

                    if (!ContentHash.TryParseHex(family.CatalogFingerprint, out catalogFingerprint))
                    {
                        Add(name, false, "the family's catalog fingerprint literal is not 64 lowercase hex characters: "
                            + family.CatalogFingerprint);
                        return;
                    }

                    if (!Gc018CheckpointCodecs.TryBuild(out bindings, out codecs, out string codecDetail))
                    {
                        Add(name, false, codecDetail);
                        return;
                    }

                    registryBaseline = UnityWorldRegistry.Count;
                    sourceWorld = new WorldId(sessionSequence.Next());
                    sourceRequest = family.CreateRequest(sourceWorld, NextOperation(sourceWorld));
                    bool created = UnityWorldRegistry.TryCreate(
                        sourceRequest,
                        family.CreateRegistration(descriptor.Adaptation),
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Add(name, false, "world creation failed: " + result.Code + ": " + result.Detail);
                        return;
                    }

                    registry = new TargetRegistry(sourceWorld, 32);
                    publisher = new AssemblyPublisher(
                        host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource valueSource = family.CreateValues();
                    lane = CompositionHost.CreateDefault(
                        sourceWorld,
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
                        valueSource,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, family.Issuer, host.Faults),
                        new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptor.Adaptation.NativeTable!);

                    // The declared persistent clock with one pending wake, and the streams whose positions a
                    // checkpoint saves (P-008, P-038, P-053).
                    bool clockRegistered = time.Clocks.TryRegister(
                        DeclaredClockSpec(), out DiagnosticCode clockCode);
                    bool wakeScheduled = clockRegistered && time.TryScheduleWake(
                        family.WakeClockId,
                        new Id128(family.WakeClockId.High, family.WakeClockId.Low ^ 0x00000000000000FFUL),
                        family.WakePayloadSchema,
                        WakeDelaySteps,
                        0UL,
                        out WakeRecord? _,
                        out DiagnosticCode _);
                    rng = new RngStreamTable();
                    for (int i = 0; i < RngStreamCount; i++)
                    {
                        var streamId = new Id128(family.WakeClockId.High ^ 0x5253UL, 100UL + (ulong)i);
                        if (!rng.TryDeclare(streamId, 7UL + (ulong)i, 0x1234UL + (ulong)i, out RngStream? stream, out _)
                            || stream == null)
                        {
                            Add(name, false, "declaring random stream " + streamId.ToString() + " was refused");
                            return;
                        }

                        for (int d = 0; d < RngDrawsPerStream; d++)
                        {
                            stream.Next();
                        }
                    }

                    // Auxiliary entities before the targets, so the two worlds' native index blocks are reported
                    // rather than assumed (P-005).
                    EntityManager entityManager = host.EntityWorld.EntityManager;
                    for (int i = 0; i < AuxiliaryEntityCount; i++)
                    {
                        entityManager.CreateEntity();
                    }

                    bool setupEdits = PublishEach(family.SetupEdits);
                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));
                    bool dormantSeeded = seeder.TrySeedSlot(
                        family.DormantTarget,
                        family.DormantOwner,
                        family.DormantSlot,
                        family.DormantVersion,
                        family.DormantValue,
                        false,
                        out DiagnosticCode dormantCode,
                        out string dormantDetail);
                    if (!dormantSeeded)
                    {
                        Add(name, false, "seeding the declared dormant slot was refused: " + dormantCode + ": "
                            + dormantDetail);
                        return;
                    }

                    bool spareScope = PublishEdit(family.SpareScopeEdits[0], "spare-scope");
                    bool enrichment = PublishEdit(family.BoundaryEnrichment(), "boundary-enrichment");
                    CompositionEditPayload providerMount = family.MountProvider();
                    providerInstance = providerMount.Instance;
                    providerInstanceKnown = !providerInstance.Value.IsDefault;
                    bool providerMounted = PublishEdit(providerMount, "mount-provider");
                    bool secondMounted = PublishEdit(family.MountSecondProvider(), "mount-second-provider");

                    // The genre's own stage runtime, attached exactly where its own scenarios attach it: without it
                    // the genre's systems resolve no module and a pumped step would fault on its own ingress lane
                    // (P-043). The restored world gets its equivalent from `Gc018FamilyRestoreBuilder`.
                    stageRuntime = family.AttachStageRuntime(host, descriptor, targets, seeder);

                    RegisterSourceAdapters();

                    var missing = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        TargetId target = family.ViewTargets[i];
                        if (!targets.Contains(target) || publisher.Published.Bindings.BindingsOf(target).Count == 0)
                        {
                            missing.Add(target.ToString());
                        }
                    }

                    ulong idleSteps = PumpIdleFrames();
                    bool joined = MatchesPublishedAssembly();
                    bool dormantPresent = TryReadSlot(
                        family.DormantTarget, family.DormantOwner, family.DormantSlot, out int dormantValue, out uint dormantVersion);

                    bool pass = setupEdits
                        && seeded
                        && idleSteps == 0UL
                        && joined
                        && host.Lifecycle == WorldLifecycleState.Running
                        && UnityWorldRegistry.Count == registryBaseline + 1
                        && targets.Count > 0
                        && family.ViewTargets.Count >= 2
                        && missing.Count == 0
                        && publisher.Published.BindingRowCount > 0
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && dormantPresent
                        && dormantVersion == family.DormantVersion
                        && dormantValue == family.DormantValue
                        && clockRegistered
                        && wakeScheduled
                        && time.Clocks.PendingWakeCount == 1
                        && rng.Count == RngStreamCount
                        && spareScope
                        && enrichment
                        && providerMounted
                        && secondMounted
                        && providerInstanceKnown
                        && stageRuntime != null
                        && frameA != null
                        && AdapterFrameRegistry.Count >= 1
                        && codecs != null
                        && codecs.IsComplete;

                    Add(name, pass,
                        "session=" + sourceWorld.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; codecKinds=" + (codecs == null ? -1 : codecs.CompleteKindCount)
                        + "; liveTargets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; viewTargets=" + family.ViewTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; targetsWithoutRows=" + Join(missing)
                        + "; bindingRows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; revision=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; installs=" + lane.Committed.Installs.Count.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; dormant=" + family.DormantSlot.ToString() + "@"
                        + dormantVersion.ToString(CultureInfo.InvariantCulture) + "="
                        + dormantValue.ToString(CultureInfo.InvariantCulture)
                        + "; clock=" + family.WakeClockId.ToString()
                        + "; wakes=" + time.Clocks.PendingWakeCount.ToString(CultureInfo.InvariantCulture)
                        + "; rngStreams=" + rng.Count.ToString(CultureInfo.InvariantCulture)
                        + "; setupEdits=" + setupEdits
                        + "; spareScope=" + spareScope
                        + "; enrichment=" + enrichment
                        + "; providerMounted=" + providerMounted
                        + "; secondProviderMounted=" + secondMounted
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + "; registry=" + registryBaseline.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lifecycle=" + host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 2. retained observation

            /// <summary>
            /// Read-only snapshots: one committed boundary leased through GC-016's bounded observation and held across
            /// a real assembly publication. The lease exposes only values - the gate asserts the property by
            /// reflection over the interface *and* by mutating its own copy of the leased bytes - its payload is the
            /// payload of its own token, and the image it pins survives a publication that moves the world's
            /// revision and epoch (P-007, P-045, TEST-014).
            /// </summary>
            private void ProvePinnedSnapshotsAreReadOnly()
            {
                const string name = "w5gate-pinned-snapshots-are-read-only";
                try
                {
                    if (host == null || publisher == null)
                    {
                        Add(name, false, "the world or its publisher is missing");
                        return;
                    }

                    CommittedBoundaryLeaseResult leased = host.Observation.LeaseCommittedBoundary(
                        CommittedBoundaryRequest.Latest(BoundaryEventWindow));
                    if (!leased.Leased || leased.Lease == null)
                    {
                        Add(name, false, "leasing the committed boundary was refused: " + leased.Outcome + " ("
                            + leased.CodeText + "); the world must publish a boundary before it can be leased (P-053).");
                        return;
                    }

                    ICommittedBoundaryLease lease = leased.Lease;
                    SnapshotToken token = lease.Token;
                    byte[] before = CopyBytes(lease.State);
                    ContentHash recomputed = ContentHash.Compute(before);
                    bool ownImage = host.Observation.Snapshots.TryGetImage(token, out PublishedStepImage? image)
                        && image != null
                        && image.PayloadHash.Equals(recomputed)
                        && image.IsImageOf(token);
                    bool pinned = host.Observation.Snapshots.IsPinned(token);
                    bool atTheWorldsOwnCounters = lease.Step.Equals(host.CurrentStep)
                        && lease.Epoch.Equals(host.CurrentEpoch)
                        && lease.World.Session.Equals(sourceWorld.Session);

                    // A plain snapshot acquisition of the same token verifies against the store's own image too, so
                    // "the lease is its own token's complete image" is checked on both lease shapes (TEST-014).
                    SnapshotAcquireResult acquired = host.Observation.Acquire(token);
                    bool storeVerified = acquired.Succeeded
                        && acquired.Lease != null
                        && host.Observation.Snapshots.Verify(acquired.Lease, out ContentHash verified)
                        && verified.Equals(recomputed);
                    acquired.Lease?.Dispose();

                    // Mutation isolation: the gate mutates its *own* copy of the leased bytes and both the lease and
                    // the store's image of that token are unchanged, so what a reader receives is a frozen copy rather
                    // than a window into live storage (P-045).
                    byte[] storeBytes = image == null ? Array.Empty<byte>() : CopyBytes(image.State);
                    for (int i = 0; i < before.Length; i++)
                    {
                        before[i] = (byte)(before[i] ^ 0xFF);
                    }

                    byte[] afterMutation = CopyBytes(lease.State);
                    bool frozen = BytesEqual(afterMutation, storeBytes)
                        && !BytesEqual(before, storeBytes)
                        && host.Observation.Snapshots.TryGetImage(token, out PublishedStepImage? reread)
                        && reread != null
                        && BytesEqual(CopyBytes(reread.State), storeBytes);

                    IReadOnlyList<string> writable = WritableReferenceEscapes(typeof(ICommittedBoundaryLease));

                    // A real publication while the lease is held: the world's revision and epoch move, the leased
                    // image does not, and it is still its own token's complete image afterwards (P-007).
                    AssemblyEpoch epochBefore = host.CurrentEpoch;
                    CompositionRevision revisionBefore = publisher.PublishedRevision;
                    int imagesBefore = host.Observation.Snapshots.PublishedCount;
                    bool reparented = PublishEdit(family.ScopeReparent(), "reparent-under-lease");
                    bool moved = reparented
                        && !host.CurrentEpoch.Equals(epochBefore)
                        && !publisher.PublishedRevision.Equals(revisionBefore)
                        && host.Observation.Snapshots.PublishedCount > imagesBefore;
                    bool survived = host.Observation.Snapshots.TryGetImage(token, out PublishedStepImage? still)
                        && still != null
                        && still.PayloadHash.Equals(recomputed)
                        && host.Observation.Snapshots.IsPinned(token)
                        && lease.EventGapCount >= 0
                        && !lease.IsDisposed;
                    bool bytesHeld = BytesEqual(CopyBytes(lease.State), afterMutation);

                    lease.Dispose();
                    bool releasedOnce = lease.IsDisposed;
                    lease.Dispose();

                    bool pass = ownImage
                        && pinned
                        && atTheWorldsOwnCounters
                        && storeVerified
                        && frozen
                        && writable.Count == 0
                        && moved
                        && survived
                        && bytesHeld
                        && releasedOnce
                        && host.Observation.BoundaryLeaseCount >= 1;

                    Add(name, pass,
                        "token=" + token.ToString()
                        + "; step=" + lease.Step.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + lease.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; bytes=" + afterMutation.Length.ToString(CultureInfo.InvariantCulture)
                        + "; payloadHash=" + recomputed.ToHex()
                        + "; ownImage=" + ownImage
                        + "; storeVerified=" + storeVerified
                        + "; pinned=" + pinned
                        + "; writableReferenceEscapes=" + writable.Count.ToString(CultureInfo.InvariantCulture)
                        + (writable.Count == 0 ? string.Empty : "(" + Join(writable) + ")")
                        + "; reparented=" + reparented
                        + "; publicationMovedTheWorld=" + moved
                        + "; pinnedImageSurvived=" + survived
                        + "; leasedBytesHeld=" + bytesHeld
                        + "; releasedOnce=" + releasedOnce
                        + "; boundaryLeases=" + host.Observation.BoundaryLeaseCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 3. the checkpoint

            /// <summary>
            /// The checkpoint, captured at the committed boundary through the reconciled lease seam: the reader leases
            /// GC-016's boundary for the whole copy (the observation's own lease counter proves it), the document's
            /// step is the leased step, and the queued external command is dispositioned explicitly rather than
            /// omitted (P-053). The world is idempotent afterwards: a capture mutates nothing (P-051).
            /// </summary>
            private void CaptureCheckpointAtTheCommittedBoundary()
            {
                const string name = "w5gate-checkpoint-from-the-committed-boundary";
                try
                {
                    if (host == null || lane == null || publisher == null || targets == null || registry == null
                        || rng == null || time == null || codecs == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    queuedOperation = NextOperation(sourceWorld);
                    CommandEnvelope envelope = family.QueuedCommand(sourceWorld, queuedOperation);
                    CommandAdmissionReceipt receipt = host.Submit(envelope);
                    if (!receipt.Admitted)
                    {
                        Add(name, false, "the family's own command was not admitted: " + receipt.Result.Kind + "/"
                            + receipt.Result.Reason);
                        return;
                    }

                    commandPayloads[queuedOperation] = envelope.Payload;

                    // The world's own declaration of what is queued at this boundary. `WorldObservation` reports
                    // `Unspecified` without it, which is never "an empty queue" - so the gate attaches the world's
                    // real facts and asserts the capture therefore has an explicit disposition (P-053).
                    boundaryFacts = new WorldBoundaryFacts(
                        sourceWorld, host.Messages, lane, BoundaryQueueDisposition.Included);
                    host.Observation.AttachBoundaryFacts(boundaryFacts);

                    sourceContext = new CaptureContext(
                        sourceWorld,
                        sourceRequest.Definition,
                        catalogFingerprint,
                        targets,
                        registry,
                        time.Clocks.Clocks,
                        time.Clocks,
                        rng,
                        lane.Committed.Mode,
                        StepDurationTicks,
                        TicksPerSecond,
                        MaxStepsPerPump,
                        false,
                        lane,
                        publisher,
                        Array.Empty<BufferId>(),
                        commandPayloads);

                    var reader = new UnityCommittedBoundaryReader(host, sourceContext);
                    int leasesBefore = host.Observation.BoundaryLeaseCount;
                    int declared = boundaryFacts.QueuedCommandCount;
                    LogicalStepId stepBefore = host.CurrentStep;
                    AssemblyEpoch epochBefore = host.CurrentEpoch;
                    int imagesBefore = host.Observation.Snapshots.PublishedCount;
                    int rowsBefore = publisher.Published.BindingRowCount;

                    capture = CheckpointCapture.Capture(
                        reader,
                        new CheckpointCaptureRequest(
                            sourceWorld, codecs, CheckpointQueuePolicy.IncludeQueued, catalogFingerprint));
                    if (capture == null || !capture.Captured)
                    {
                        Add(name, false, "the boundary capture was refused: "
                            + (capture == null ? "the capture returned no result" : capture.Code + ": " + capture.Detail));
                        return;
                    }

                    int leasesAfter = host.Observation.BoundaryLeaseCount;
                    bool readThroughTheLease = leasesAfter == leasesBefore + 1;
                    HeaderRecordValue header = capture.Header;
                    bool decoded = CheckpointDocument.TryRead(
                            capture.Document, codecs, out CheckpointDocument? document, out DiagnosticCode documentCode,
                            out string documentDetail)
                        && document != null;
                    bool readOnly = host.CurrentStep.Equals(stepBefore)
                        && host.CurrentEpoch.Equals(epochBefore)
                        && host.Observation.Snapshots.PublishedCount == imagesBefore
                        && publisher.Published.BindingRowCount == rowsBefore
                        && host.Lifecycle == WorldLifecycleState.Running;

                    capturedSlots = decoded ? ReadRecords<SlotRecordValue>(document!, CheckpointRecordKind.Slot)
                        : Array.Empty<SlotRecordValue>();

                    bool pass = readThroughTheLease
                        && declared == 1
                        && capture.Code == DiagnosticCode.None
                        && capture.Document.Length > 0
                        && !capture.DocumentHash.IsEmpty
                        && decoded
                        && header.SourceSession.Session.Equals(sourceWorld.Session)
                        && header.WorldDefinition.Equals(sourceRequest.Definition)
                        && header.LogicalStep == stepBefore.Value
                        && header.SourcePublishedEpoch == epochBefore.Value
                        && header.SourcePublishedRevision == publisher.PublishedRevision.Value
                        && header.CatalogFingerprint.Equals(catalogFingerprint)
                        && header.QueuePolicy == (uint)CheckpointQueuePolicy.IncludeQueued
                        && header.CommandCount == 1U
                        && header.RejectedQueuedCount == 0U
                        && capture.Queue.Offered == 1
                        && capture.Queue.Included == 1
                        && capture.Queue.IsAccountedFor
                        && capture.Queue.Cutoff.Value == host.Messages!.Requests.LastAdmissionSequence.Value
                        && capturedSlots.Count >= 2
                        && header.TargetCount == (uint)targets.Count
                        && header.ScopeCount == (uint)lane.Committed.Scopes.Count
                        && header.RngStreamCount == (uint)rng.Count
                        && readOnly;

                    Add(name, pass,
                        "session=" + sourceWorld.Session.ToString()
                        + "; bytes=" + capture.Document.Length.ToString(CultureInfo.InvariantCulture)
                        + "; documentHash=" + capture.DocumentHash.ToHex()
                        + "; counts=" + capture.Counts.ToString()
                        + "; headerStep=" + header.LogicalStep.ToString(CultureInfo.InvariantCulture)
                        + "; headerEpoch=" + header.SourcePublishedEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; declaredQueued=" + declared.ToString(CultureInfo.InvariantCulture)
                        + "; offered=" + capture.Queue.Offered.ToString(CultureInfo.InvariantCulture)
                        + "; readThroughTheLease=" + readThroughTheLease
                        + "; boundaryLeases=" + leasesBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + leasesAfter.ToString(CultureInfo.InvariantCulture)
                        + "; queued=" + queuedOperation.ToString()
                        + "; document=" + (decoded ? capture.Counts.ToString() : documentDetail)
                        + "; readOnly=" + readOnly
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 4. a prewrite fault

            /// <summary>
            /// The prewrite half of TEST-016 row 5 on the gate's own world: GC-017's latch is armed at the validation
            /// boundary, a real composition edit is admitted, drained and derived for, and the publication that would
            /// install it is refused *before* any live write. The old assembly keeps running, its rows, revision,
            /// epoch and image count are unchanged, the plan is terminal without having crossed the live-write
            /// boundary, and the latch's own trace names the boundary, the operation and the plan (P-028, P-029,
            /// P-052).
            /// </summary>
            private void RefuseThePrewriteFaultAndKeepTheOldAssembly()
            {
                const string name = "w5gate-prewrite-fault-keeps-the-old-assembly";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null
                        || seeder == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    IReadOnlyList<LiveSlotState> slotsBefore = seeder.ReadLiveSlots(TargetIds());
                    AssemblyEpoch epochBefore = host.CurrentEpoch;
                    CompositionRevision revisionBefore = publisher.PublishedRevision;
                    int imagesBefore = host.Observation.Snapshots.PublishedCount;
                    int rowsBefore = publisher.Published.BindingRowCount;
                    string rowsTextBefore = PublishedRowsFingerprint();
                    int reachesBefore = host.Faults.ReachCountOf(FaultBoundary.Validation);

                    // The edit that will be refused: the family's own mode switch to Conservative, which retracts the
                    // automatically inherited rows of every eligible target. It is a real change, so the retry in the
                    // postwrite step publishes an assembly that really writes live state.
                    refusedOperation = NextOperation(sourceWorld);
                    EditAdmission admission = lane.SubmitEdit(
                        family.ModeSet(PropagationMode.Conservative), refusedOperation, lane.Committed.Revision);
                    if (!admission.Staged)
                    {
                        Add(name, false, "the lane refused the mode-switch edit: " + admission.Kind + "/"
                            + admission.Code);
                        return;
                    }

                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                    {
                        Add(name, false, "the composition publication was refused: "
                            + (published.Count > 0 ? published[0].Outcome + "/" + published[0].Code : "none"));
                        return;
                    }

                    host.Faults.Arm(FaultBoundary.Validation);
                    OperationId derivationOperation = NextOperation(sourceWorld);
                    DerivedAssemblyReport derived = pipeline.PublishDerived(derivationOperation);
                    host.Faults.Disarm(FaultBoundary.Validation);

                    // The module's own proposal for the publication the refusal leaves adopted and pending: the
                    // postwrite step retries *that* publication, and it must be built from the module's proposal
                    // rather than from a second interpretation of the composition (P-002, P-006).
                    if (derived.Proposal != null && derived.Proposal.Proposal != null)
                    {
                        pendingProposal = derived.Proposal;
                    }

                    AssemblyPublicationReport? publication = derived.Publication;
                    int reachesAfter = host.Faults.ReachCountOf(FaultBoundary.Validation);
                    IReadOnlyList<FaultRecord> records = host.Faults.Trace.Of(FaultBoundary.Validation);
                    FaultRecord? last = records.Count > 0 ? records[records.Count - 1] : (FaultRecord?)null;
                    bool traceNamesTheBoundary = reachesAfter == reachesBefore + 1
                        && last.HasValue
                        && last.Value.Injected
                        && last.Value.Boundary == FaultBoundary.Validation
                        && last.Value.Operation.Equals(derivationOperation)
                        && !last.Value.PlanHash.IsEmpty;

                    IReadOnlyList<LiveSlotState> slotsAfter = seeder.ReadLiveSlots(TargetIds());
                    bool liveStateKept = LiveSlotsEqual(slotsBefore, slotsAfter);

                    bool pass = host.Faults.IsCompiledIn
                        && derived.Outcome == DerivedAssemblyOutcome.Refused
                        && publication != null
                        && publication.Outcome == Outcome.Rejected
                        && publication.Code == DiagnosticCode.ResourceUnavailable
                        && publication.StructuralWrites == 0
                        && !publication.CrossedLiveWriteBoundary
                        && host.Lifecycle == WorldLifecycleState.Running
                        && host.CurrentEpoch.Equals(epochBefore)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && publisher.Published.Epoch.Equals(epochBefore)
                        && host.Observation.Snapshots.PublishedCount == imagesBefore
                        && publisher.Published.BindingRowCount == rowsBefore
                        && string.Equals(PublishedRowsFingerprint(), rowsTextBefore, StringComparison.Ordinal)
                        && liveStateKept
                        && traceNamesTheBoundary
                        && derived.Plan != null
                        && derived.Plan.State.Phase == PlanPhase.Rejected
                        && !derived.Plan.State.HasCrossedLiveWriteBoundary
                        && publisher.HasAdoptedPublication
                        && publisher.PrewriteFailureCount == 1
                        && pendingProposal != null
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "edit=mode-set-conservative"
                        + "; operation=" + refusedOperation.ToString()
                        + "; derivationOperation=" + derivationOperation.ToString()
                        + "; armed=True"
                        + "; outcome=" + DescribePublication(publication)
                        + "; structuralWrites=" + (publication != null ? publication.StructuralWrites : -1).ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + (publication != null && publication.CrossedLiveWriteBoundary)
                        + "; planPhase=" + (derived.Plan != null ? derived.Plan.State.Phase.ToString() : "<none>")
                        + "; planHeld=" + (pendingProposal != null)
                        + "; adoptedAndPending=" + publisher.HasAdoptedPublication
                        + "; prewriteFailures=" + publisher.PrewriteFailureCount.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; revision=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + host.Observation.Snapshots.PublishedCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + host.Lifecycle
                        + "; liveStateKept=" + liveStateKept
                        + "; boundaryReaches=" + (reachesAfter - reachesBefore).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + host.Faults.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + (last.HasValue ? last.Value.ToLine() : "<none>")
                        + "; joined=" + MatchesPublishedAssembly()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 5. a postwrite fault

            /// <summary>
            /// The postwrite half of TEST-016 row 5, on the same world and the same publication series: the
            /// composition publication the prewrite refusal left adopted and pending is retried with the latch armed
            /// at the first live write, so the publisher really applies its structural writes and then fails inside
            /// the fence. The world fail-stops: `Faulted`, `ApplyFault`, no new epoch, no new image, the last good
            /// snapshot still leasable and byte-identical, and no later frame or command resumes it (P-029, P-030,
            /// P-031, P-052).
            /// </summary>
            private void FaultTheWorldAfterItsFirstLiveWrite()
            {
                const string name = "w5gate-postwrite-fault-fail-stops-the-world";
                try
                {
                    if (host == null || pipeline == null || publisher == null || targets == null || seeder == null
                        || pendingProposal?.Proposal == null)
                    {
                        Add(name, false, "the world, its pipeline or the pending publication is missing");
                        return;
                    }

                    CompositionProposal proposal = pendingProposal!.Proposal!;
                    if (!proposal.BaseEpoch.Equals(host.CurrentEpoch)
                        || !proposal.ExpectedRevision.Equals(publisher.PublishedRevision))
                    {
                        Add(name, false, "the pending proposal no longer describes the published assembly: base "
                            + proposal.BaseEpoch.Value.ToString(CultureInfo.InvariantCulture) + "/"
                            + proposal.ExpectedRevision.Value.ToString(CultureInfo.InvariantCulture) + " against "
                            + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture) + "/"
                            + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture));
                        return;
                    }

                    var gate = new StagedResourceGate(StagedByteCeiling, family.Issuer, host.Faults);
                    OperationId retryOperation = NextOperation(sourceWorld);
                    var acquisitions = new InertAcquisitionSet(gate, retryOperation);
                    var staged = new ResourceKey(new Id128(family.SessionSalt, LeaseResourceLow + 0x5150UL));
                    if (!acquisitions.TryAcquire(staged, LeaseBytes, null, out DiagnosticCode acquireCode))
                    {
                        Add(name, false, "the plan's resource gate refused the staged lease: " + acquireCode);
                        return;
                    }

                    PlannedPublication plan = AssemblyPlanner.Build(
                        proposal,
                        publisher.Descriptor,
                        publisher.PublishedRevision,
                        host.CurrentEpoch,
                        publisher.Published.Bindings,
                        publisher.Published.Rules,
                        targets.PlannerTargets(),
                        seeder.ReadLiveSlots(TargetIds()),
                        publisher.Migrations,
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot),
                        acquisitions,
                        new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                    if (!plan.IsPrepared)
                    {
                        Add(name, false, "the retry plan is " + plan.State.Phase + ": " + plan.State.Code + ": "
                            + plan.State.Detail);
                        return;
                    }

                    // The last good image, copied under its own lease before the fault: the postwrite failure must
                    // leave exactly this image as the only safe observation (P-031).
                    SnapshotToken lastGood = default(SnapshotToken);
                    bool hadLastGood = host.Observation.TryGetLatestBoundary(out lastGood);
                    byte[] lastGoodBytes = Array.Empty<byte>();
                    if (hadLastGood)
                    {
                        SnapshotAcquireResult lastLease = host.Observation.Acquire(lastGood);
                        if (lastLease.Succeeded && lastLease.Lease != null)
                        {
                            lastGoodBytes = CopyBytes(lastLease.Lease.State);
                            lastLease.Lease.Dispose();
                        }
                    }

                    AssemblyEpoch epochBefore = host.CurrentEpoch;
                    LogicalStepId stepBefore = host.CurrentStep;
                    int imagesBefore = host.Observation.Snapshots.PublishedCount;
                    int rowsBefore = publisher.Published.BindingRowCount;
                    CompositionRevision revisionBefore = publisher.PublishedRevision;
                    int reachesBefore = host.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite);

                    host.Faults.Arm(FaultBoundary.FirstLiveWrite);
                    AssemblyPublicationReport publication = publisher.Publish(plan);
                    host.Faults.Disarm(FaultBoundary.FirstLiveWrite);

                    IReadOnlyList<FaultRecord> records = host.Faults.Trace.Of(FaultBoundary.FirstLiveWrite);
                    FaultRecord? last = records.Count > 0 ? records[records.Count - 1] : (FaultRecord?)null;
                    bool traceNamesTheBoundary = host.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite) == reachesBefore + 1
                        && last.HasValue
                        && last.Value.Injected
                        && last.Value.Boundary == FaultBoundary.FirstLiveWrite
                        && !last.Value.PlanHash.IsEmpty;

                    // No epoch and no image published: the failed publication wrote live state and stopped there
                    // (P-031). The world is terminal, and the last good image is still the only safe observation.
                    bool nothingPublished = host.CurrentEpoch.Equals(epochBefore)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && host.CurrentStep.Equals(stepBefore)
                        && host.Observation.Snapshots.PublishedCount == imagesBefore
                        && publisher.Published.Epoch.Equals(epochBefore)
                        && publisher.Published.BindingRowCount == rowsBefore
                        && host.Lifecycle == WorldLifecycleState.Faulted
                        && host.FaultCode == DiagnosticCode.ApplyFault
                        && host.FaultCount >= 1;

                    bool lastImageHeld = hadLastGood
                        && host.Observation.Snapshots.TryGetImage(lastGood, out PublishedStepImage? lastImage)
                        && lastImage != null
                        && BytesEqual(CopyBytes(lastImage.State), lastGoodBytes);

                    // The world never resumes: a frame is refused and a command is refused, and neither moves the
                    // step. The old world is not merely idle - it is stopped (P-031).
                    WorldPumpResult frame = host.PumpFrame(IdlePumpTicks);
                    CommandAdmissionReceipt afterFault = host.Submit(family.QueuedCommand(sourceWorld, NextOperation(sourceWorld)));
                    bool neverResumes = !frame.Pumped
                        && !afterFault.Admitted
                        && host.CurrentStep.Equals(stepBefore)
                        && host.CurrentEpoch.Equals(epochBefore)
                        && host.Lifecycle == WorldLifecycleState.Faulted;

                    bool pass = publication.Outcome == Outcome.Faulted
                        && publication.Code == DiagnosticCode.ApplyFault
                        && publication.StructuralWrites > 0
                        && publication.CrossedLiveWriteBoundary
                        && traceNamesTheBoundary
                        && nothingPublished
                        && lastImageHeld
                        && neverResumes
                        && host.Faults.IsCompiledIn;

                    Add(name, pass,
                        "operation=" + retryOperation.ToString()
                        + "; plannedInstalls=" + plan.Installs.Count.ToString(CultureInfo.InvariantCulture)
                        + "; plannedRemovals=" + plan.Removals.Count.ToString(CultureInfo.InvariantCulture)
                        + "; armed=True"
                        + "; outcome=" + DescribePublication(publication)
                        + "; structuralWrites=" + publication.StructuralWrites.ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + publication.CrossedLiveWriteBoundary
                        + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + host.Observation.Snapshots.PublishedCount.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + host.Lifecycle
                        + "; faultCode=" + host.FaultCode
                        + "; faults=" + host.FaultCount.ToString(CultureInfo.InvariantCulture)
                        + "; lastGoodImageHeld=" + lastImageHeld
                        + "; pumpedAfterFault=" + frame.Pumped
                        + "; admittedAfterFault=" + afterFault.Admitted
                        + "; boundaryReaches=" + (host.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite) - reachesBefore).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + host.Faults.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + (last.HasValue ? last.Value.ToLine() : "<none>")
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 6. restore into a new session

            /// <summary>
            /// The checkpoint restore, from the document captured before either fault: a new session, a world built
            /// unexposed and exposed only after its references, cursors, clocks and re-admitted command validated.
            /// The gate verifies the restored world's *state* rather than trusting the outcome value: the captured
            /// slots (active and dormant) are the restored slots with their versions and values, the mode, the scope
            /// tree and the published assembly are the captured ones, the step is the captured step, and every handle
            /// minted in the faulted source session is refused (P-004, P-005, P-032, P-049, P-053).
            /// </summary>
            private void RestoreIntoANewSession()
            {
                const string name = "w5gate-restore-into-a-new-session";
                try
                {
                    if (capture == null || codecs == null || descriptor == null || host == null || targets == null
                        || registry == null || capture.Document.Length == 0)
                    {
                        Add(name, false, "the captured document or the restore builder's inputs are missing");
                        return;
                    }

                    restoredSession = new WorldId(sessionSequence.Next());
                    var migrations = new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>());
                    builder = new Gc018FamilyRestoreBuilder(
                        family, descriptor, catalogFingerprint, UnityWorldRegistry.Count, NextOperation);
                    var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                    restoreOutcome = executor.Restore(
                        capture.Document,
                        restoredSession,
                        NextOperation(restoredSession),
                        builder,
                        migrations,
                        catalogFingerprint,
                        AllocatedSchemas(),
                        true);

                    RestoreOutcome outcome = restoreOutcome;
                    restoredHost = null;
                    bool exposed = UnityWorldRegistry.TryGet(restoredSession, out UnityWorldHost? restored);
                    restoredHost = restored;
                    RestorePlan? plan = outcome.Plan;
                    bool stateMatches = false;
                    int restoredActive = -1;
                    int restoredDormant = -1;
                    string stateDetail = "no restored world";
                    if (plan != null && builder.Seeder != null && builder.Targets != null && builder.Lane != null
                        && restoredHost != null)
                    {
                        IReadOnlyList<LiveSlotState> live = builder.Seeder.ReadLiveSlots(TargetIdsOf(builder.Targets));
                        restoredActive = CountSlots(plan.Slots, true);
                        restoredDormant = CountSlots(plan.Slots, false);
                        stateMatches = SlotsMatchCaptured(capturedSlots, live, out stateDetail)
                            && plan.Targets.Count == targets.Count
                            && plan.Slots.Count == capturedSlots.Count
                            && plan.DormantSlotCount == CountSlots(capturedSlots, false)
                            && plan.Slots.Count == live.Count
                            && builder.Lane.Committed.Mode == lane!.Committed.Mode
                            && builder.Lane.Committed.Scopes.Count == lane.Committed.Scopes.Count
                            && restoredHost.CurrentStep.Value == capture.Header.LogicalStep;
                    }

                    bool oldHandlesRefused = true;
                    if (builder.Publisher != null && publisher != null)
                    {
                        IReadOnlyList<LiveTarget> live = targets.Targets;
                        for (int i = 0; i < live.Count; i++)
                        {
                            if (registry.TryGetHandle(live[i].Target, out TargetHandle oldHandle)
                                && builder.Publisher.TryResolveHandle(oldHandle, out TargetId _, out Entity _))
                            {
                                oldHandlesRefused = false;
                                break;
                            }
                        }
                    }

                    bool pass = outcome.Restored
                        && outcome.Stage == RestoreStage.Expose
                        && outcome.Code == DiagnosticCode.None
                        && outcome.PublishedToken.HasValue
                        && !outcome.Session.Session.Equals(sourceWorld.Session)
                        && outcome.SourceWorld.Session.Equals(sourceWorld.Session)
                        && plan != null
                        && plan.IsDirect
                        && plan.TargetSession.Session.Equals(restoredSession.Session)
                        && exposed
                        && restoredHost != null
                        && ReferenceEquals(restoredHost, builder.Staging)
                        && builder.BuildCount == 1
                        && builder.RegistryCountAfterStaging == builder.RegistryBaseline
                        && builder.RegistryBaseline + 1 == UnityWorldRegistry.Count
                        && builder.RebuiltTargetCount == plan.Targets.Count
                        && builder.RebuiltSlotCount == plan.Slots.Count
                        && builder.RebuiltDormantSlotCount == plan.DormantSlotCount
                        && builder.ReadmittedCommandCount == plan.Commands.Count
                        && builder.RebuiltClockCount == 1
                        && builder.RebuiltWakeCount == 1
                        && builder.PublishedAssemblyCount >= 1
                        && restoredHost.CurrentEpoch.Value >= 2UL
                        && restoredHost.Lifecycle == WorldLifecycleState.Running
                        && AssemblyPublisher.MatchesPublishedAssembly(
                            builder.Lane!.Committed.Revision,
                            builder.Lane.Committed.Epoch,
                            builder.Publisher!.PublishedRevision,
                            restoredHost.CurrentEpoch)
                        && stateMatches
                        && oldHandlesRefused
                        && host.Lifecycle == WorldLifecycleState.Faulted;

                    Add(name, pass,
                        "sourceSession=" + sourceWorld.Session.ToString()
                        + "(" + host.Lifecycle + ")"
                        + "; restoredSession=" + restoredSession.Session.ToString()
                        + "; stage=" + outcome.Stage
                        + "; code=" + outcome.Code
                        + "; capturedStep=" + capture.Header.LogicalStep.ToString(CultureInfo.InvariantCulture)
                        + "; restoredStep=" + (restoredHost == null ? -1UL : restoredHost.CurrentStep.Value)
                        + "; capturedSlots=" + capturedSlots.Count.ToString(CultureInfo.InvariantCulture)
                        + "; restoredSlots=" + outcome.RestoredSlots.ToString(CultureInfo.InvariantCulture)
                        + "(active=" + restoredActive.ToString(CultureInfo.InvariantCulture)
                        + ",dormant=" + restoredDormant.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; replayedScopes=" + builder.ReplayedScopeCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedInstalls=" + builder.ReplayedInstallCount.ToString(CultureInfo.InvariantCulture)
                        + "; readmittedCommands=" + builder.ReadmittedCommandCount.ToString(CultureInfo.InvariantCulture)
                        + "; clocks=" + builder.RebuiltClockCount.ToString(CultureInfo.InvariantCulture)
                        + " wakes=" + builder.RebuiltWakeCount.ToString(CultureInfo.InvariantCulture)
                        + "; publishedAssemblies=" + builder.PublishedAssemblyCount.ToString(CultureInfo.InvariantCulture)
                        + "; restoredEpoch=" + (restoredHost == null ? 0UL : restoredHost.CurrentEpoch.Value)
                        + "; registry=" + builder.RegistryBaseline.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; stateMatches=" + stateMatches
                        + "; stateDetail=" + Clip(stateDetail, 200)
                        + "; oldHandlesRefused=" + oldHandlesRefused
                        + "; exposure=" + Clip(outcome.Detail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 7. adapters on the new world

            /// <summary>
            /// The common adapters, bound to the *restored* world: a bounded asset lease completes under a live token
            /// of the new incarnation, a stamped typed command reaches the new world's own command port and commits a
            /// step, committed-output presentation reads only the restored world's published image, and the
            /// destruction of every view leaves the restored world's gameplay exactly where it was (P-024, P-034,
            /// P-045, TEST-015, TEST-019). The old world's work is refused by the new world's adapters: a token and an
            /// input stamp minted in the faulted session are not accepted by an adapter bound to the restored one
            /// (P-002, P-004).
            /// </summary>
            private void BindAdaptersToTheRestoredWorld()
            {
                const string name = "w5gate-adapters-bind-to-the-restored-world";
                try
                {
                    if (builder?.Staging == null || restoredHost == null || builder.Lane == null || builder.Targets == null
                        || builder.Publisher == null || host == null || lane == null)
                    {
                        Add(name, false, "the restored world or its modules are missing");
                        return;
                    }

                    CompositionHost restoredLane = builder.Lane;
                    bool liveToken = TryLiveToken(
                        restoredLane, restoredHost, providerInstance, workOrdinal: 11U, out AsyncWorkToken token,
                        out string tokenDetail);
                    if (!liveToken)
                    {
                        Add(name, false, "the restored world has no live installation stamp: " + tokenDetail);
                        return;
                    }

                    // The restored world's adapters, built exactly as the source world's were.
                    ingressB = new TypedInputIngress(restoredSession, restoredHost);
                    backendB = new DeterministicAssetBackend();
                    assetsB = new AssetLeaseTable(
                        restoredSession,
                        backendB,
                        restoredLane.Callbacks,
                        restoredHost.Ledger,
                        family.MutableOwner,
                        providerInstance,
                        AdapterTableCapacity,
                        AssetByteBudget);
                    viewsB = new ViewRegistry(restoredSession, ViewCapacity);
                    binderB = new RecordingViewBinder();
                    sourceB = new CommittedImageSource(restoredSession);
                    presenterB = new CommittedOutputPresenter(viewsB, sourceB, binderB);
                    bindingsB = new InputBindingTable().Add(new InputCommandBinding(
                        InputDeviceKind.Button,
                        1,
                        family.CommandRoute,
                        family.CommandTarget,
                        family.CommandSchema,
                        null));
                    frameB = new WorldAdapterFrame(restoredHost, ingressB, bindingsB, null, null, assetsB, presenterB);
                    bool registered = AdapterFrameRegistry.Register(frameB);

                    // One asset lease under a live token of the new incarnation, completed and usable.
                    bool requested = assetsB.TryRequest(
                        LeaseResource(11UL),
                        token,
                        new FrozenPayload(new byte[] { 1, 2, 3, 4 }),
                        LeaseBytes,
                        out Id128 leaseId,
                        out DiagnosticCode leaseCode,
                        out string leaseDetail);
                    bool completed = false;
                    bool usable = false;
                    if (requested && assetsB.TryGet(leaseId, out AssetLease? lease) && lease != null)
                    {
                        completed = backendB.Complete(lease.Handle) && assetsB.PumpCompletions() == 1;
                        usable = completed
                            && lease.State == AssetLeaseState.Ready
                            && assetsB.TryReadPayload(leaseId, out FrozenPayload? payload)
                            && payload != null
                            && payload.Bytes.Count == 4
                            && assetsB.LiveLeaseCount == 1;
                    }

                    // The new world's own command port, driven through the stamped input path: the sample is admitted
                    // and the family's owner answers it on the next frame, committing one real step.
                    InputAdmissionResult admission = ingressB.Submit(NewSample(restoredSession, sequence: 1UL));
                    LogicalStepId stepBefore = restoredHost.CurrentStep;
                    AdapterFrameReport collected = AdapterFrameRegistry.CollectInput(restoredSession);
                    TimeFrameReport frame = builder.Time!.PumpFrame(IdlePumpTicks);
                    LogicalStepId stepAfter = restoredHost.CurrentStep;

                    // Presentation reads the restored world's committed image: the family's own view targets receive
                    // the values their published rows carry, and a composition parent read from the committed index.
                    CommittedAssemblyImage image = LiveAssemblyImageBuilder.Build(
                        builder.Publisher!.Published, new LiveTargetScopeIndex(builder.Targets!));
                    bool refreshed = sourceB.Refresh(image);
                    var created = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        ViewCreateOutcome outcome = presenterB.CreateView(family.ViewTargets[i], 0U, out ViewRecord? _);
                        created.Add(outcome.ToString());
                    }

                    AdapterFrameReport presented = AdapterFrameRegistry.Present(restoredSession);

                    var dissenting = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        if (!PresentedMatchesPublished(family.ViewTargets[i], image.Token, out string dissent))
                        {
                            dissenting.Add(dissent);
                        }
                    }

                    // The old world's work, offered to the new world's adapters.
                    bool foreignTokenRefused = !assetsB.TryRequest(
                        LeaseResource(12UL),
                        ForeignToken(),
                        new FrozenPayload(new byte[] { 5, 6, 7, 8 }),
                        LeaseBytes,
                        out Id128 _,
                        out DiagnosticCode foreignCode,
                        out string foreignDetail);
                    InputAdmissionResult foreignSample = ingressB.Submit(NewSample(sourceWorld, sequence: 1UL));
                    bool foreignInputRefused = foreignSample.Outcome == InputAdmissionOutcome.RejectedForeignWorld
                        && ingressB.RefusedCount == 1;
                    bool foreignImageRefused = !viewsB.TryApply(
                        new ViewKey(family.ViewTargets[0], 0U),
                        new PresentationApplyData(
                            new ViewKey(family.ViewTargets[0], 0U),
                            new SnapshotToken(
                                sourceWorld,
                                new AssemblyEpoch(capture!.Header.SourcePublishedEpoch),
                                new LogicalStepId(capture.Header.LogicalStep)),
                            default(ScopeId),
                            Array.Empty<PresentationField>()),
                        binderB);

                    // Destroying every view leaves the restored world's gameplay exactly where it was (P-024).
                    AssemblyEpoch epochBeforeViews = restoredHost.CurrentEpoch;
                    int rowsBeforeDestroy = builder.Publisher.Published.BindingRowCount;
                    int destroyed = presenterB.DestroyAllViews();
                    bool gameplayIntact = destroyed > 0
                        && restoredHost.CurrentStep.Equals(stepAfter)
                        && restoredHost.CurrentEpoch.Equals(epochBeforeViews)
                        && builder.Publisher.Published.BindingRowCount == rowsBeforeDestroy
                        && builder.Publisher.Published.Targets.Count > 0;

                    bool pass = liveToken
                        && assetsB != null
                        && requested
                        && completed
                        && usable
                        && assetsB.PostRetireCompletionCount == 0
                        && admission.Outcome == InputAdmissionOutcome.Admitted
                        && collected.Ran
                        && frame.StepsCommitted >= 1UL
                        && !stepAfter.Equals(stepBefore)
                        && restoredHost.Lifecycle == WorldLifecycleState.Running
                        && refreshed
                        && image.TargetCount > 0
                        && presented.Ran
                        && presented.Items == family.ViewTargets.Count
                        && dissenting.Count == 0
                        && viewsB.LiveViewCount == 0
                        && !registered && AdapterFrameRegistry.TryGet(restoredSession, out _)
                        && foreignTokenRefused
                        && foreignCode == DiagnosticCode.StaleHandle
                        && foreignInputRefused
                        && foreignImageRefused
                        && gameplayIntact;

                    Add(name, pass,
                        "restoredSession=" + restoredSession.Session.ToString()
                        + "; token=" + token.ToString()
                        + "; tokenState=" + tokenDetail
                        + "; leaseRequested=" + requested
                        + (requested ? string.Empty : "(" + leaseCode + ": " + leaseDetail + ")")
                        + "; leaseCompleted=" + completed
                        + "; leaseUsable=" + usable
                        + "; stepsCommitted=" + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + stepAfter.Value.ToString(CultureInfo.InvariantCulture)
                        + "; admission=" + admission.Outcome
                        + "; image=" + image.ToString()
                        + "; viewsCreated=" + Join(created)
                        + "; presented=" + presented.Outcome
                        + "(" + presented.Items.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; dissenting=" + Join(dissenting)
                        + "; liveViewsAfterDestroy=" + viewsB.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; foreignTokenRefused=" + foreignTokenRefused
                        + "(" + foreignCode + ": " + Clip(foreignDetail, 80) + ")"
                        + "; foreignInputRefused=" + foreignInputRefused
                        + "; foreignImageRefused=" + foreignImageRefused
                        + "; gameplayIntact=" + gameplayIntact
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 8. the retired world's callbacks

            /// <summary>
            /// The retired world's callbacks. The source world's adapter frame is retired after its world stopped, so
            /// a completion that arrives afterwards reaches a retired table: it installs nothing, releases nothing
            /// another caller owns, and is counted as what it is - work that arrived after the world ended (P-007,
            /// P-047, P-048). The retired world's own last committed image is still readable through a lease, because
            /// retention is bounded but never silently dropped (P-045).
            /// </summary>
            private void RejectTheRetiredWorldsCallbacks()
            {
                const string name = "w5gate-retired-world-callbacks-are-rejected";
                try
                {
                    if (host == null || frameA == null || assetsA == null || backendA == null || lane == null
                        || restoredHost == null || builder?.Time == null)
                    {
                        Add(name, false, "the source world's adapters or the restored world are missing");
                        return;
                    }

                    // An acquisition of the faulted world whose completion has not arrived yet: the request itself is
                    // accepted (the token names the activation the world really had), and the completion is what the
                    // retired table refuses (P-007).
                    bool acquiredUnderTheOldWorld = TryLiveToken(
                        lane, host, providerInstance, workOrdinal: 21U, out AsyncWorkToken oldToken, out string oldDetail);
                    // The request is attempted unconditionally, because an `out` variable declared inside a
                    // short-circuit chain is only definitely assigned when that operand ran (the same rule
                    // `FaultScenario` records): the token is either the old world's live stamp or a default one the
                    // table refuses, and both outcomes are reported.
                    bool requested = assetsA.TryRequest(
                        LeaseResource(21UL),
                        oldToken,
                        new FrozenPayload(new byte[] { 21, 22, 23, 24 }),
                        LeaseBytes,
                        out Id128 leaseId,
                        out DiagnosticCode requestCode,
                        out string requestDetail);

                    AdapterTeardownReport teardown = frameA.Retire();
                    bool unregistered = AdapterFrameRegistry.Unregister(sourceWorld);
                    int postRetireBefore = assetsA.PostRetireCompletionCount;
                    AssetCompletionResult late = assetsA.Complete(leaseId, new FrozenPayload(new byte[] { 25, 26, 27, 28 }));
                    int postRetireAfter = assetsA.PostRetireCompletionCount;
                    bool lateRejected = late.Outcome != AssetCompletionOutcome.Completed
                        && postRetireAfter == postRetireBefore + 1
                        && !assetsA.TryReadPayload(leaseId, out FrozenPayload? _)
                        && assetsA.IsRetired
                        && backendA.OutstandingLoadCount == 0
                        && assetsA.ReservedBytes == 0UL;

                    // The retired world's last committed image is still a readable immutable observation.
                    SnapshotToken lastGood = default(SnapshotToken);
                    bool lastGoodHeld = host.Observation.TryGetLatestBoundary(out lastGood)
                        && host.Observation.Acquire(lastGood).Succeeded;

                    // The restored world is unaffected by all of it: still running, still joined, still presenting.
                    bool restoredAlive = restoredHost.Lifecycle == WorldLifecycleState.Running
                        && builder.Lane != null
                        && builder.Publisher != null
                        && AssemblyPublisher.MatchesPublishedAssembly(
                            builder.Lane.Committed.Revision,
                            builder.Lane.Committed.Epoch,
                            builder.Publisher.PublishedRevision,
                            restoredHost.CurrentEpoch);
                    PresentationReport presented = presenterB != null
                        ? presenterB.Present()
                        : new PresentationReport(PresentationOutcome.NoViews, default(SnapshotToken), 0, 0, 0, 0, 0, "no presenter");
                    bool restoredPresented = presented.Outcome == PresentationOutcome.NoViews
                        || presented.Outcome == PresentationOutcome.Presented;

                    bool pass = acquiredUnderTheOldWorld
                        && requested
                        && teardown.Assets != null
                        && lateRejected
                        && unregistered
                        && !AdapterFrameRegistry.TryGet(sourceWorld, out _)
                        && lastGoodHeld
                        && restoredAlive
                        && restoredPresented
                        && host.Lifecycle == WorldLifecycleState.Faulted;

                    Add(name, pass,
                        "oldToken=" + oldToken.ToString()
                        + "; oldTokenState=" + oldDetail
                        + "; leaseRequested=" + requested
                        + (requested ? string.Empty : "(" + requestCode + ": " + requestDetail + ")")
                        + "; lateCompletion=" + late.Outcome
                        + "; lateGate=" + late.Gate
                        + "; postRetireCompletions=" + postRetireBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + postRetireAfter.ToString(CultureInfo.InvariantCulture)
                        + "; tableRetired=" + assetsA.IsRetired
                        + "; outstandingLoads=" + backendA.OutstandingLoadCount.ToString(CultureInfo.InvariantCulture)
                        + "; reservedBytes=" + assetsA.ReservedBytes.ToString(CultureInfo.InvariantCulture)
                        + "; teardown=" + teardown.ToString()
                        + "; lastGoodImageLeasable=" + lastGoodHeld
                        + "; sourceWorld=" + host.Lifecycle
                        + "; restoredWorld=" + restoredHost.Lifecycle
                        + "; restoredPresented=" + presented.Outcome
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== the world and its adapters

            /// <summary>
            /// Registers the source world's adapter frame: a stamped typed ingress into the world's own command port,
            /// a bounded asset lease table over the world's own resource ledger, and a committed-output presenter over
            /// a view registry. The frame is the object the application pump drives at its two named points, so the
            /// gate's adapter statements are about one object rather than a set of loose calls (04 s3, P-002).
            /// </summary>
            private void RegisterSourceAdapters()
            {
                if (host == null || lane == null)
                {
                    return;
                }

                ingressA = new TypedInputIngress(sourceWorld, host);
                pendingA = new PendingInputCompletionTable(lane.Callbacks, ingressA, PendingInputCapacity);
                backendA = new DeterministicAssetBackend();
                assetsA = new AssetLeaseTable(
                    sourceWorld,
                    backendA,
                    lane.Callbacks,
                    host.Ledger,
                    family.MutableOwner,
                    providerInstance,
                    AdapterTableCapacity,
                    AssetByteBudget);
                viewsA = new ViewRegistry(sourceWorld, ViewCapacity);
                binderA = new RecordingViewBinder();
                sourceA = new CommittedImageSource(sourceWorld);
                presenterA = new CommittedOutputPresenter(viewsA, sourceA, binderA);
                bindingsA = new InputBindingTable().Add(new InputCommandBinding(
                    InputDeviceKind.Button,
                    1,
                    family.CommandRoute,
                    family.CommandTarget,
                    family.CommandSchema,
                    null));
                frameA = new WorldAdapterFrame(host, ingressA, bindingsA, null, pendingA, assetsA, presenterA);
                AdapterFrameRegistry.Register(frameA);
            }

            // ================================================================== helpers

            private PluginClockSpec DeclaredClockSpec() =>
                new PluginClockSpec(
                    family.WakeClockId,
                    "w5gate-" + family.Label,
                    PluginClockKind.LogicalStep,
                    WakePausePolicy.Defer,
                    true);

            /// <summary>
            /// One live async work token of a world: the installation's generation and activation epoch read from the
            /// committed composition, so a "live token" is a live activation rather than a remembered one (P-046).
            /// </summary>
            private static bool TryLiveToken(
                CompositionHost worldLane,
                UnityWorldHost world,
                PluginInstanceId instance,
                uint workOrdinal,
                out AsyncWorkToken token,
                out string detail)
            {
                token = default(AsyncWorkToken);
                detail = string.Empty;
                if (instance.Value.IsDefault)
                {
                    detail = "no installation identity";
                    return false;
                }

                if (!worldLane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    detail = "the installation is not in the committed composition";
                    return false;
                }

                token = new AsyncWorkToken(
                    new OperationId(world.World, worldLane.World.Session, 1UL + workOrdinal),
                    instance,
                    entry.Record.Generation,
                    entry.Record.ActivationEpoch,
                    workOrdinal);
                detail = entry.State.ToString();
                return true;
            }

            /// <summary>A token of a world the restored world's adapters must refuse: the gate's own faulted session.</summary>
            private AsyncWorkToken ForeignToken()
            {
                if (!providerInstanceKnown || lane == null || lane.Committed.TryGetInstall(providerInstance, out InstallEntry? entry) == false
                    || entry == null)
                {
                    return default(AsyncWorkToken);
                }

                return new AsyncWorkToken(
                    new OperationId(sourceWorld, lane.World.Session, 0x464F5245UL),
                    providerInstance,
                    entry.Record.Generation,
                    entry.Record.ActivationEpoch,
                    workOrdinal: 99U);
            }

            private SampledInputCommand NewSample(WorldId world, ulong sequence) =>
                new SampledInputCommand(
                    new InputSourceStamp(world, new Id128(InputSourceLow, 0UL), sequence, LogicalStepId.Zero, AssemblyEpoch.First),
                    family.CommandRoute,
                    family.CommandTarget,
                    family.CommandSchema,
                    null,
                    family.CommandPayload(1));

            private ResourceKey LeaseResource(ulong ordinal) =>
                new ResourceKey(new Id128(family.SessionSalt, LeaseResourceLow + ordinal));

            private IReadOnlyList<DerivationTarget> TargetView()
            {
                if (targets == null)
                {
                    return Array.Empty<DerivationTarget>();
                }

                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            private IReadOnlyList<TargetId> TargetIds()
            {
                IReadOnlyList<LiveTarget> live = targets!.Targets;
                var ids = new List<TargetId>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ids.Add(live[i].Target);
                }

                return ids;
            }

            private static IReadOnlyList<TargetId> TargetIdsOf(LiveTargetIndex index)
            {
                IReadOnlyList<LiveTarget> live = index.Targets;
                var ids = new List<TargetId>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ids.Add(live[i].Target);
                }

                return ids;
            }

            /// <summary>Applies every declared edit and requires each one to publish (used for the setup edits).</summary>
            private bool PublishEach(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishEdit(payloads[i], "setup-" + i.ToString(CultureInfo.InvariantCulture)))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Admits one composition edit, drains it (publishing the composition), derives for the new composition
            /// and publishes the assembly. A run whose derivation finds no target change is completed by publishing
            /// the unchanged assembly, because P-006 has one publication series: every composition publication gets
            /// exactly one assembly (a no-op publication consumes the number rather than leaving the lane ahead).
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = label + ": the world or its pipeline is missing";
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(payload, NextOperation(sourceWorld), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    lastFailure = label + ": the lane refused the edit (" + admission.Kind + "/" + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = label + ": the composition publication was refused ("
                        + (published.Count > 0 ? published[0].Outcome + "/" + published[0].Code : "none") + ")";
                    return false;
                }

                DerivedAssemblyReport derived = pipeline.PublishDerived(NextOperation(sourceWorld));
                if (derived.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    OperationId operation = NextOperation(sourceWorld);
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        operation, lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        lastFailure = label + ": the unchanged-assembly publication was refused: " + unchanged.Detail;
                        return false;
                    }
                }
                else if (derived.Outcome != DerivedAssemblyOutcome.Published)
                {
                    lastFailure = label + ": the world refused the assembly: " + derived.Describe();
                    return false;
                }

                if (!MatchesPublishedAssembly())
                {
                    lastFailure = label + ": the composition lane and the world publish different pairs: " + lane.Committed.Revision.Value
                        + "/" + lane.Committed.Epoch.Value + " against " + publisher.PublishedRevision.Value + "/"
                        + host.CurrentEpoch.Value;
                    return false;
                }

                return true;
            }

            private bool MatchesPublishedAssembly()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            private ulong PumpIdleFrames()
            {
                if (time == null)
                {
                    return 0UL;
                }

                ulong committed = 0UL;
                for (int i = 0; i < IdlePumpFrames; i++)
                {
                    committed += time.PumpFrame(IdlePumpTicks).StepsCommitted;
                }

                return committed;
            }

            private bool TryReadSlot(TargetId target, OwnerId owner, SlotId slot, out int value, out uint version)
            {
                value = int.MinValue;
                version = 0U;
                if (seeder == null)
                {
                    return false;
                }

                IReadOnlyList<LiveSlotState> slots = seeder.ReadLiveSlots(new[] { target });
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Target.Equals(target)
                        && slots[i].Slot.Owner.Equals(owner)
                        && slots[i].Slot.Slot.Equals(slot))
                    {
                        value = slots[i].Value;
                        version = slots[i].SchemaVersion;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>The published rows of every committed target, as one comparable text (P-017).</summary>
            private string PublishedRowsFingerprint()
            {
                if (publisher == null)
                {
                    return "<no-publisher>";
                }

                IReadOnlyList<TargetId> publishedTargets = publisher.Published.Targets;
                var text = new StringBuilder();
                text.Append("targets=").Append(publishedTargets.Count.ToString(CultureInfo.InvariantCulture));
                for (int t = 0; t < publishedTargets.Count; t++)
                {
                    TargetId target = publishedTargets[t];
                    IReadOnlyList<TargetBindingRow> rows = publisher.Published.Bindings.BindingsOf(target);
                    text.Append(';').Append(target.ToString()).Append('=')
                        .Append(rows.Count.ToString(CultureInfo.InvariantCulture));
                    for (int r = 0; r < rows.Count; r++)
                    {
                        text.Append('|').Append(rows[r].Capability.ToString())
                            .Append('#').Append(rows[r].OutputSlot.ToString(CultureInfo.InvariantCulture))
                            .Append(':').Append(rows[r].Value.ToString(CultureInfo.InvariantCulture));
                    }
                }

                return text.ToString();
            }

            /// <summary>
            /// True when every captured slot row exists in the restored world with the same version and value. The
            /// captured set carries active *and* dormant rows, so this is where a restore that dropped or reactivated
            /// durable state is caught (P-032, P-053).
            /// </summary>
            private static bool SlotsMatchCaptured(
                IReadOnlyList<SlotRecordValue> captured,
                IReadOnlyList<LiveSlotState> live,
                out string detail)
            {
                detail = string.Empty;
                for (int c = 0; c < captured.Count; c++)
                {
                    SlotRecordValue row = captured[c];
                    var key = new StateSlotKey(
                        new TargetId(new Id128(row.TargetHigh, row.TargetLow)),
                        new OwnerId(new Id128(row.OwnerHigh, row.OwnerLow)),
                        new SlotId(new Id128(row.SlotHigh, row.SlotLow)));
                    bool found = false;
                    for (int l = 0; l < live.Count; l++)
                    {
                        if (live[l].Slot.Equals(key)
                            && live[l].SchemaVersion == row.SchemaVersion
                            && live[l].Value == row.Value)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        detail = "slot " + key.ToString() + "@" + row.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                            + "=" + row.Value.ToString(CultureInfo.InvariantCulture) + " is not in the restored world";
                        return false;
                    }
                }

                bool activeSeen = false;
                bool dormantSeen = false;
                for (int c = 0; c < captured.Count; c++)
                {
                    activeSeen |= captured[c].Active;
                    dormantSeen |= !captured[c].Active;
                }

                bool activeLive = false;
                bool dormantLive = false;
                if (dormantSeen)
                {
                    // Dormant rows are authoritative state: the restored world must carry them as dormant rows, and
                    // `LiveTargetSeeder.ReadLiveSlots` reports every row of a target's buffer, so presence is the
                    // claim (their `Active` flag is proven by the restore plan's own dormant count).
                    dormantLive = live.Count >= captured.Count;
                }

                activeLive = activeSeen && live.Count >= 1;
                if (dormantSeen && (!dormantLive || !activeLive))
                {
                    detail = "the restored world does not carry both the active and the dormant rows";
                    return false;
                }

                return true;
            }

            private static bool LiveSlotsEqual(IReadOnlyList<LiveSlotState> left, IReadOnlyList<LiveSlotState> right)
            {
                if (left.Count != right.Count)
                {
                    return false;
                }

                for (int i = 0; i < left.Count; i++)
                {
                    if (!left[i].Slot.Equals(right[i].Slot)
                        || left[i].SchemaVersion != right[i].SchemaVersion
                        || left[i].Value != right[i].Value)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static int CountSlots(IReadOnlyList<SlotRecordValue> slots, bool active)
            {
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Active == active)
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>
            /// Every public member of the committed-boundary lease whose type could hand a reader something writable.
            /// The list is the check: it is empty for a lease that exposes only values, which is what makes "no
            /// writable component reference escapes inspection" a property of the type rather than a convention
            /// (P-045, TEST-014).
            /// </summary>
            private static IReadOnlyList<string> WritableReferenceEscapes(Type leaseType)
            {
                var findings = new List<string>();
                PropertyInfo[] properties = leaseType.GetProperties();
                for (int i = 0; i < properties.Length; i++)
                {
                    if (IsWritable(properties[i].PropertyType))
                    {
                        findings.Add(leaseType.Name + "." + properties[i].Name + ":" + properties[i].PropertyType.Name);
                    }
                }

                MethodInfo[] methods = leaseType.GetMethods();
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].IsSpecialName || methods[i].DeclaringType == typeof(object))
                    {
                        continue;
                    }

                    Type returned = methods[i].ReturnType;
                    if (returned == typeof(void))
                    {
                        continue;
                    }

                    if (IsWritable(returned))
                    {
                        findings.Add(leaseType.Name + "." + methods[i].Name + ":" + returned.Name);
                    }
                }

                return findings;
            }

            private static bool IsWritable(Type type)
            {
                if (type == typeof(EntityManager)
                    || type == typeof(Entity)
                    || type == typeof(EntityQuery)
                    || type == typeof(Unity.Entities.World)
                    || type == typeof(UnityEngine.GameObject)
                    || type == typeof(UnityEngine.Transform))
                {
                    return true;
                }

                string name = type.Name;
                if (name.StartsWith("DynamicBuffer`", StringComparison.Ordinal)
                    || name.StartsWith("NativeArray`", StringComparison.Ordinal)
                    || name.StartsWith("NativeSlice`", StringComparison.Ordinal)
                    || name.StartsWith("NativeList`", StringComparison.Ordinal)
                    || name.StartsWith("SystemHandle", StringComparison.Ordinal)
                    || name.StartsWith("JobHandle", StringComparison.Ordinal))
                {
                    return true;
                }

                if (type.IsGenericType)
                {
                    Type[] arguments = type.GetGenericArguments();
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        if (IsWritable(arguments[i]))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>The mutable rows of one document, decoded through the generated codecs (P-054).</summary>
            private static IReadOnlyList<TValue> ReadRecords<TValue>(CheckpointDocument document, CheckpointRecordKind kind)
                where TValue : struct
            {
                return document.TryReadRecords(kind, out IReadOnlyList<TValue> values, out DiagnosticCode _, out string _)
                    ? values
                    : Array.Empty<TValue>();
            }

            private static byte[] CopyBytes(FrozenPayload payload)
            {
                var copy = new byte[payload.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = payload.Bytes[i];
                }

                return copy;
            }

            private static bool BytesEqual(byte[] left, byte[] right)
            {
                if (left.Length != right.Length)
                {
                    return false;
                }

                for (int i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            private static string DescribePublication(AssemblyPublicationReport? publication) =>
                publication == null
                    ? "<none>"
                    : publication.Outcome.ToString() + "/" + publication.Code.ToString();

            private string Clip(string value, int limit) =>
                value.Length <= limit ? value : value.Substring(0, limit) + "...";

            private static string Join(IReadOnlyList<string> values)
            {
                if (values.Count == 0)
                {
                    return "<none>";
                }

                var text = new StringBuilder();
                for (int i = 0; i < values.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(',');
                    }

                    text.Append(values[i]);
                }

                return text.ToString();
            }

            private string DescribeFailure() =>
                lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;

            /// <summary>
            /// The schemas this build can allocate at the version it carries: the document container, every recipe
            /// the family's catalog registers, and the payload schema the declared wake names. A captured schema
            /// version outside this set has no path to the build's version, which is what P-054 requires a restore to
            /// refuse.
            /// </summary>
            private IReadOnlyList<SchemaRef> AllocatedSchemas()
            {
                var schemas = new List<SchemaRef> { CheckpointFormat.DocumentSchema };
                SpawnRecipeCatalog recipes = family.CreateRecipes();
                for (int i = 0; i < recipes.Recipes.Count; i++)
                {
                    AddDistinct(schemas, recipes.Recipes[i].Recipe.Schema);
                }

                AddDistinct(schemas, family.WakePayloadSchema);
                return schemas;
            }

            private static void AddDistinct(List<SchemaRef> schemas, SchemaRef candidate)
            {
                for (int i = 0; i < schemas.Count; i++)
                {
                    if (schemas[i].Equals(candidate))
                    {
                        return;
                    }
                }

                schemas.Add(candidate);
            }

            /// <summary>One operation identity of this run; every operation of one world is unique (P-050).</summary>
            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private void Add(string bareName, bool passed, string detail)
            {
                steps.Add(new W5GateStep(family.Label + "/" + bareName, passed, detail));
            }
        }
    }
}
