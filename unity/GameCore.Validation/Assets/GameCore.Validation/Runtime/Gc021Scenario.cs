// GameCore.Validation.ProbeHost — the GC-021 durable-delivery and destination-idempotency scenario.
//
// The gate sentence this file implements, from `docs/game-core/09-implementation-guide.md` (GC-021) and the task's own
// probe sentence, is:
//
//   "Irreversible output adapters consume only committed events, use explicit external idempotency keys, and persist
//    an outbox when delivery must survive crashes" (P-045), with "capacity exhaustion is explicit, never a silent
//    drop" (P-043) beside it, "a committed delivery obligation outlives the unload of the world that committed it"
//    (P-045, P-049, P-053), "one committed event is one obligation" (P-050) and "no new universal gameplay Effect
//    API" (P-003).
//
// ONE RUNNER, TWO FAMILIES, NO THIRD FAMILY IMPLEMENTATION
//
// The world is the family's own: `UnityWorldRegistry.TryCreate(family.CreateRequest(...),
// family.CreateRegistration(descriptor.Adaptation))` plus the same module chain `Gc018Scenario` builds — the target
// registry, the assembly publisher, the live target index and seeder, the control lane and its world join, the
// derivation pipeline and the temporal driver — and the family's own runtime module, attached through
// `IGc018Family.TryAttachRuntime`, so the one committed event this run needs is the family's own declared command
// executed by the family's own stage systems. Nothing here re-implements a kernel module, and nothing is discovered
// reflectively except the one structural clause that is *about* a type surface (step 9).
//
// Part A is the delivery seam itself, and it is genre-free: it needs no family fact beyond the session identity a
// committed event's obligation key is derived from and the one world the checkpoint half captures. Part B is the
// reward bridge over each family's real world, and its two halves are genuinely different on purpose:
//
//   * the narrative world commits `narrative.schema.choice-committed` when its own choice is accepted, so the bridge
//     itself is the obligation source and the reward is derived from a real committed choice (07 s5 step 1-2);
//   * a card world commits card results and no narrative choice, so the obligation is derived from the newest
//     committed card result of that world through a declared `IDeliveryObligationSource` this file owns, and the
//     *destination* half is what the card world proves: the card family's own `Transfer` command really moves the
//     reward card into the recipient's hand (P-034).
//
// The one gap is recorded rather than papered over: the narrative world has no card table module, so its reward
// destination reports `DestinationMissing` and the obligation stays open — which is exactly what "the obligation is
// open after the pass if the transfer has not committed" means, and it is the honest answer of the two-world
// composition this harness can build. 07 s5 asks for one world carrying both families; this file's per-family runs
// are what the shipped family registrations allow, and the reward *content* is the same policy in both.
//
// HOW A STEP IS WRITTEN
//
// Every observation is a named `Gc021Step` carrying the values it was computed from: a step never records a claim it
// did not read, and no helper returns a silent false — a method that cannot do what its name says reports the code and
// detail it received. A crash is placed at a *named* boundary through the delivery seam's own `IDeliveryStepHook`
// (the marker and its exception are test-only types in another assembly, so this file owns a three-line hook of its
// own rather than referencing a test assembly from a shipping probe host). World teardown runs in a `finally` and
// appends a failing observation if anything survived, so a leaked world is a red run rather than a silent one.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Delivery;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Integration.RewardOutbox;
using GameCore.Gameplay.Narrative;
using GameCore.Planning;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// A destination port that records exactly what it was asked to do and honours P-045 the way a real destination
    /// must: it mutates once per external idempotency key and reports `AlreadyApplied` for every later attempt at the
    /// same key. That is what makes "redelivery after acknowledgement loss applies the mutation once" a property of
    /// the run rather than of the adapter alone (P-045).
    /// </summary>
    public sealed class Gc021RecordingDestination : IDestinationPort
    {
        private readonly HashSet<Id128> applied = new HashSet<Id128>();
        private readonly SchemaRef commandSchema;

        public Gc021RecordingDestination(Id128 destinationId, SchemaRef commandSchema)
        {
            if (destinationId.IsDefault)
            {
                throw new ArgumentException(
                    "A delivery destination identity is never the all-zero value (P-004).", nameof(destinationId));
            }

            DestinationId = destinationId;
            this.commandSchema = commandSchema;
        }

        public Id128 DestinationId { get; }

        public SchemaRef CommandSchema => commandSchema;

        /// <summary>Every attempt this port was handed, in order; its count is the "asked twice" evidence (P-045).</summary>
        public List<DeliveryAttempt> Attempts { get; } = new List<DeliveryAttempt>();

        /// <summary>Attempts that really mutated something.</summary>
        public int MutationCount { get; private set; }

        /// <summary>Attempts answered from the idempotency key, so no second mutation happened (P-045).</summary>
        public int AlreadyAppliedCount { get; private set; }

        public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
        {
            Attempts.Add(attempt);
            code = DiagnosticCode.None;
            if (!applied.Add(attempt.IdempotencyKey))
            {
                AlreadyAppliedCount++;
                detail = "the destination already applied external key " + attempt.IdempotencyKey.ToString()
                    + ", so attempt #" + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " mutated nothing (P-045).";
                return DestinationOutcome.AlreadyApplied;
            }

            MutationCount++;
            detail = "the destination applied attempt #" + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture)
                + " under external key " + attempt.IdempotencyKey.ToString() + " (P-045).";
            return DestinationOutcome.Applied;
        }

        public override string ToString() => "gc021RecordingDestination(attempts="
            + Attempts.Count.ToString(CultureInfo.InvariantCulture) + ",mutations="
            + MutationCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The process died at a named delivery boundary. The exception carries the boundary name and the detail the
    /// adapter passed, so a step that catches it reports what the adapter was about to do (P-045, P-052).
    /// </summary>
    public sealed class Gc021DeliveryCrashException : Exception
    {
        public Gc021DeliveryCrashException(string boundary, string detail)
            : base("scripted delivery crash at " + boundary + ": " + detail + " (GC-021).")
        {
            Boundary = boundary;
            Detail = detail ?? string.Empty;
        }

        /// <summary>The named boundary the crash was placed at.</summary>
        public string Boundary { get; }

        /// <summary>What the adapter was doing when the boundary was reached.</summary>
        public string Detail { get; }

        public Gc021DeliveryCrashException()
            : this(DeliveryBoundaries.None, string.Empty)
        {
        }

        public Gc021DeliveryCrashException(string message)
            : base(message)
        {
            Boundary = DeliveryBoundaries.None;
            Detail = string.Empty;
        }

        public Gc021DeliveryCrashException(string message, Exception innerException)
            : base(message, innerException)
        {
            Boundary = DeliveryBoundaries.None;
            Detail = string.Empty;
        }
    }

    /// <summary>
    /// A deterministic crash at one named delivery boundary, as the seam's own test hook. It raises at most once, so
    /// the recovery path that reaches the same boundary a second time is not interrupted — which is what lets one run
    /// say "crash, recover, redeliver, acknowledge" in a single process (P-045, P-049).
    ///
    /// It is declared here rather than reused from `GameCore.Execution.Tests.Delivery` on purpose: that marker lives
    /// in a test assembly, and a shipping probe host must not depend on one. The seam only ever reports where it is
    /// (`IDeliveryStepHook.Reach`), so the production code path is byte-for-byte the same with this hook installed or
    /// with none (04 s6).
    /// </summary>
    public sealed class Gc021CrashHook : IDeliveryStepHook
    {
        /// <summary>The boundary this hook dies at: the post-destination, pre-record acknowledgement window.</summary>
        public const string AfterDelivery = DeliveryBoundaries.AfterDelivery;

        private readonly string boundary;
        private readonly List<string> reached = new List<string>();

        public Gc021CrashHook(string boundary)
        {
            this.boundary = boundary ?? DeliveryBoundaries.None;
        }

        /// <summary>The boundary this hook dies at.</summary>
        public string Boundary => boundary;

        /// <summary>True once the crash has been raised; a second reach is reported and returns.</summary>
        public bool Spent { get; private set; }

        /// <summary>Every boundary the adapter reported to this hook, in order.</summary>
        public IReadOnlyList<string> ReachedBoundaries => reached;

        public int ReachCount => reached.Count;

        /// <summary>Whether the adapter really reported one named boundary to this hook.</summary>
        public bool Observed(string candidate)
        {
            for (int i = 0; i < reached.Count; i++)
            {
                if (string.Equals(reached[i], candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public void Reach(string reachedBoundary, string detail)
        {
            reached.Add(reachedBoundary);
            if (Spent || !string.Equals(reachedBoundary, boundary, StringComparison.Ordinal))
            {
                return;
            }

            Spent = true;
            throw new Gc021DeliveryCrashException(reachedBoundary, detail);
        }

        public override string ToString() => "gc021CrashHook(" + boundary + ",spent=" + Spent + ")";
    }

    /// <summary>
    /// The obligation source of a world whose committed event is a card result rather than a narrative choice: it
    /// claims exactly the one committed event this run selected and describes the reward that event carries.
    ///
    /// The claim is a policy, not a fabrication: P-045 requires an irreversible output adapter to consume *committed*
    /// events, and which committed event a recipient turns into a reward is the recipient's own content decision. The
    /// source never sees a staged event, because the owner reads committed events through the world's own reader, and
    /// it refuses everything else by schema and sequence so a second claim is impossible.
    /// </summary>
    public sealed class Gc021CommittedEventRewardSource : IDeliveryObligationSource
    {
        private readonly SchemaRef sourceSchema;
        private readonly EventSequence sourceSequence;
        private readonly Id128 destinationId;
        private readonly SchemaRef commandSchema;
        private readonly byte[] payload;
        private readonly bool requiresDurability;

        public Gc021CommittedEventRewardSource(
            SchemaRef sourceSchema,
            EventSequence sourceSequence,
            Id128 destinationId,
            SchemaRef commandSchema,
            byte[] payload,
            bool requiresDurability)
        {
            this.sourceSchema = sourceSchema;
            this.sourceSequence = sourceSequence;
            this.destinationId = destinationId;
            this.commandSchema = commandSchema;
            this.payload = payload ?? Array.Empty<byte>();
            this.requiresDurability = requiresDurability;
        }

        /// <summary>The committed schema this source is willing to turn into a reward.</summary>
        public SchemaRef SourceSchema => sourceSchema;

        /// <summary>The committed event sequence it names; every other committed event is unclaimed.</summary>
        public EventSequence SourceSequence => sourceSequence;

        /// <summary>Committed events this source claimed.</summary>
        public int ClaimedCount { get; private set; }

        /// <summary>Committed events it did not claim, counted so "the event was really seen" is visible (P-052).</summary>
        public int UnclaimedCount { get; private set; }

        public bool TryDescribe(in CommittedEvent committed, out DeliveryObligationRequest request)
        {
            request = default(DeliveryObligationRequest);
            if (!committed.Schema.Equals(sourceSchema) || !committed.Cursor.Sequence.Equals(sourceSequence))
            {
                UnclaimedCount++;
                return false;
            }

            ClaimedCount++;
            request = new DeliveryObligationRequest(destinationId, commandSchema, payload, requiresDurability);
            return true;
        }

        public override string ToString() => "gc021CommittedEventRewardSource(sequence="
            + sourceSequence.Value.ToString(CultureInfo.InvariantCulture) + ",claimed="
            + ClaimedCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Runs the GC-021 durable-delivery sequence over one family and one catalog.</summary>
    public static class Gc021Scenario
    {
        /// <summary>
        /// Declared temporal settings of the command-driven world this scenario drives: a world with no fixed step
        /// publishes no simulation duration, and one admitted command is one step (P-036, P-037).
        /// </summary>
        public const ulong StepDurationTicks = 0UL;

        public const ulong TicksPerSecond = 0UL;

        public const uint MaxStepsPerPump = 1U;

        /// <summary>Host ticks every frame of this scenario is pumped at, as the sibling scenarios pump theirs.</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        /// <summary>Capacity and terminal retention of the seam's own outboxes; small, so a refusal is reachable.</summary>
        private const int SeamCapacity = 8;

        private const int SeamTerminalRetention = 4;

        /// <summary>Capacity of the capacity-exhaustion outbox: one open obligation is the whole bound (P-043).</summary>
        private const int OneObligation = 1;

        /// <summary>The reward owner's declared bounds, as the bridge's constructor takes them (07 s5).</summary>
        private const int RewardCapacity = 8;

        private const int RewardTerminalRetention = 8;

        /// <summary>Bound of one committed-event poll and of one dispatch pass (P-043's bounded work).</summary>
        private const int RewardEventWindow = 16;

        private const int RewardDispatchWindow = 4;

        /// <summary>Staged-resource and plan budgets of the derivation, as the sibling scenarios declare them.</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>Target registry capacity of the world this scenario creates.</summary>
        private const int RegistryCapacity = 32;

        /// <summary>
        /// High word of the declared identities of this seam proof: the destination, the payload schema the
        /// destination accepts and the outbox owners. They are declared constants of the scenario because a delivery
        /// destination is supplied by the caller and never invented by the kernel (P-001, P-004) — the reward half
        /// uses the card package's own declared destination instead.
        /// </summary>
        private const ulong SeamIdentityHigh = 0x676330323164656CUL;

        /// <summary>Stable identity of the recording destination port (P-004).</summary>
        public static readonly Id128 SeamDestinationId = new Id128(SeamIdentityHigh, 1UL);

        /// <summary>The payload schema the recording destination accepts (P-054).</summary>
        public static readonly SchemaRef SeamCommandSchema =
            new SchemaRef(new SchemaId(new Id128(SeamIdentityHigh, 2UL)), 1U);

        /// <summary>Stable identity of the outbox owners of Part A (P-017, P-034).</summary>
        public static readonly Id128 SeamOwnerId = new Id128(SeamIdentityHigh, 3UL);

        /// <summary>
        /// Stable identity of the reward bridge's delivery owner. It is deliberately not the card destination's
        /// identity: one destination has one owner and one owner has one outbox (P-017, P-034).
        /// </summary>
        public static readonly Id128 RewardOwnerId = new Id128(SeamIdentityHigh, 4UL);

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite and the player probe
        /// instead of shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc021-key-is-derived-from-committed-data",
            "gc021-commit-persists-before-apply",
            "gc021-crash-after-delivery-loses-the-acknowledgement",
            "gc021-redelivery-applies-the-mutation-once",
            "gc021-capacity-exhaustion-is-never-a-silent-drop",
            "gc021-volatile-delivery-is-distinguishable-from-durable",
            "gc021-obligation-survives-the-source-world-unload",
            "gc021-checkpoint-carries-the-outbox-and-the-cursor",
            "gc021-no-universal-effect-api",
            "gc021-narrative-choice-is-observed",
            "gc021-reward-content-covers-the-observed-node",
            "gc021-reward-obligation-is-durable-and-idempotent",
        };

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole sequence for one family and one genre half.</summary>
        public static Gc021ScenarioResult Run(IGc018Family family, Gc021Genre genre)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family, genre).Run();
        }

        /// <summary>
        /// The one structural clause that is about a type surface rather than about data: the delivery seam declares
        /// no reversible or universal-effect member. It is a reflection over the *declared* members of the seam, which
        /// is the only way to falsify "no new universal gameplay Effect API exists" from inside a scenario (P-003).
        /// </summary>
        private static readonly string[] ForbiddenMemberFragments = { "Undo", "Revert", "Rollback", "Effect" };

        /// <summary>The seam's executor: one scripted sequence over one family, holding the world and every module.</summary>
        private sealed class Executor
        {
            private readonly IGc018Family family;
            private readonly Gc021Genre genre;
            private readonly List<Gc021Step> steps = new List<Gc021Step>();
            private readonly IdSequence sessionSequence;

            // The family's own world and the module chain the GC-018 executor builds for it.
            private UnityWorldHost? host;
            private PipelineDescriptorReport? descriptor;
            private TargetRegistry? registry;
            private AssemblyPublisher? publisher;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private CompositionHost? lane;
            private DerivedAssemblyPipeline? pipeline;
            private WorldTimeDriver? time;
            private RngStreamTable? rng;
            private WorldCompositionBridge? laneJoin;
            private Gc018RuntimeWorld? runtimeWorld;
            private WorldId sourceWorld;
            private WorldCreateRequest sourceRequest;
            private ContentHash catalogFingerprint;
            private CheckpointSerializerBindings? bindings;
            private CheckpointCodecSet? codecs;
            private string codecEvidence = string.Empty;
            private int registryBaseline;
            private ulong operationSequence;

            // Part A: the two outboxes the crash pair and the redelivery share, and the isolated ones beside them.
            private MemoryDeliveryJournal? seamJournal;
            private Gc021RecordingDestination? seamDestination;
            private DeliveryKey seamKey;

            // The obligation the unload step carries across a disposed world, and its delivered successor.
            private UnityWorldHost? unloadHost;
            private UnityWorldHost? successorHost;
            private WorldId unloadSession;
            private WorldId successorSession;

            // Part B: the reward bridge, the committed event it derives from, and the content it declares.
            private NarrativeCardRewardBridge? rewardBridge;
            private WorldDeliveryOwner? rewardOwner;
            private Gc021CommittedEventRewardSource? rewardSource;
            private Gc021RewardContent? content;
            private RewardCatalog? catalog;
            private DeliveryKey rewardKey;
            private EventSequence rewardEvent;
            private LogicalStepId rewardEventStep;
            private AssemblyEpoch rewardEventEpoch;
            private OperationId rewardEventCausal;
            private byte[] rewardPayload = Array.Empty<byte>();
            private int observedNodeOrdinal = Gc021RewardContent.NoObservedNode;
            private string rewardEventEvidence = string.Empty;
            private string rewardSetupFailure = string.Empty;

            private string setupFailure = string.Empty;
            private string lastFailure = string.Empty;

            public Executor(IGc018Family family, Gc021Genre genre)
            {
                this.family = family;
                this.genre = genre;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public Gc021ScenarioResult Run()
            {
                try
                {
                    CreateTheFamilyWorld();
                    ProveTheKeyIsDerivedFromCommittedData();
                    ProveCommitPersistsBeforeApply();
                    ProveCrashAfterDeliveryLosesTheAcknowledgement();
                    ProveRedeliveryAppliesTheMutationOnce();
                    ProveCapacityExhaustionIsNeverASilentDrop();
                    ProveVolatileDeliveryIsDistinguishableFromDurable();
                    ProveObligationSurvivesTheSourceWorldUnload();
                    DriveAndObserveTheRewardSource();
                    ProveCheckpointCarriesTheOutboxAndTheCursor();
                    ProveNoUniversalEffectApi();
                    ProveTheChoiceIsObserved();
                    ProveTheRewardContentCoversTheObservedNode();
                    ProveTheRewardObligationIsDurableAndIdempotent();
                }
                finally
                {
                    TearDownSafely();
                }

                return new Gc021ScenarioResult(family.Label, steps);
            }

            // ------------------------------------------------------------------ 0. the family's own world

            /// <summary>
            /// The world every observation of this run is grounded in: the family's own creation request and generated
            /// registration, the same module chain the GC-018 executor builds, the family's targets seeded as real ECS
            /// storage, and the family's own runtime module attached so the one command this run admits is executed by
            /// the family's own stages rather than stranded in its ingress lane (P-002, P-005, P-037, P-042, P-043).
            ///
            /// The family's own provider mounts are published afterwards, because they are what make the narrative
            /// family's choice *accepted* by gameplay (the addressed villager's chapter has to be derived into it) and
            /// they are a real publication series the checkpoint half then observes (P-006, P-013, P-030).
            /// </summary>
            private void CreateTheFamilyWorld()
            {
                try
                {
                    descriptor = family.CompilePipeline();
                    if (!descriptor.Succeeded || descriptor.Descriptor == null || descriptor.Adaptation == null
                        || descriptor.Compilation == null)
                    {
                        setupFailure = "the ownership and schedule pipeline refused: " + descriptor.Describe();
                        return;
                    }

                    if (!ContentHash.TryParseHex(family.CatalogFingerprint, out catalogFingerprint))
                    {
                        setupFailure = "the family's catalog fingerprint literal is not 64 lowercase hex characters: "
                            + family.CatalogFingerprint;
                        return;
                    }

                    if (!Gc018CheckpointCodecs.TryBuild(out CheckpointSerializerBindings? builtBindings,
                            out CheckpointCodecSet? builtCodecs, out string codecDetail))
                    {
                        setupFailure = "binding the committed checkpoint catalog's serializers was refused: "
                            + codecDetail;
                        return;
                    }

                    bindings = builtBindings;
                    codecs = builtCodecs;
                    codecEvidence = codecDetail;

                    registryBaseline = UnityWorldRegistry.Count;
                    sourceWorld = new WorldId(sessionSequence.Next());
                    sourceRequest = family.CreateRequest(sourceWorld, NextOperation(sourceWorld));
                    bool created = UnityWorldRegistry.TryCreate(
                        sourceRequest,
                        family.CreateRegistration(descriptor.Adaptation!),
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        setupFailure = "world creation failed: " + result.Code + ": " + result.Detail;
                        return;
                    }

                    registry = new TargetRegistry(sourceWorld, RegistryCapacity);
                    publisher = new AssemblyPublisher(
                        host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor!);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource valueSource = family.CreateValues();
                    var modeSwitchValidator = new DerivationModeSwitchValidator(valueSource, TargetView);
                    lane = CompositionHost.CreateDefault(
                        sourceWorld,
                        family.WorldRootScope,
                        new CatalogManifestSource(family.Catalog, family.Declarations),
                        null,
                        family.LaneSeed,
                        modeSwitchValidator);
                    laneJoin = new WorldCompositionBridge(host, lane, publisher);
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
                        new PlanBudget(
                            PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptor.Adaptation!.NativeTable!);

                    // The random streams a capture reads: this world declares none, and an empty table is the honest
                    // answer rather than a missing dependency (P-015).
                    rng = new RngStreamTable();

                    if (!family.SeedTargets(new Gc013WorldContext(host, targets, seeder)))
                    {
                        setupFailure = "seeding the family's declared targets was refused";
                        return;
                    }

                    var attached = new Gc018RuntimeWorld(
                        host, targets, seeder, descriptor.Compilation!.Schedule!);
                    if (!family.TryAttachRuntime(attached, out string attachDetail))
                    {
                        setupFailure = "attaching the family's runtime module was refused: " + attachDetail;
                        return;
                    }

                    runtimeWorld = attached;

                    // The family's own mounts, in the order its scenarios mount them, each with the assembly the
                    // world answers it with: P-006 has one publication series, so an edit the world does not answer
                    // leaves the lane one publication ahead and every later adoption would be refused as stale.
                    if (!PublishEdit(family.MountProvider(), "mount-provider"))
                    {
                        setupFailure = "the family's own provider mount was refused: " + lastFailure;
                        return;
                    }

                    if (!PublishEdit(family.MountSecondProvider(), "mount-second-provider"))
                    {
                        setupFailure = "the family's second provider mount was refused: " + lastFailure;
                        return;
                    }

                    if (!MatchesPublishedAssembly())
                    {
                        setupFailure = "the world's published assembly does not match its committed lane";
                        return;
                    }
                }
                catch (Exception exception)
                {
                    setupFailure = DescribeException(exception);
                }
            }

            // ------------------------------------------------------------------ 1. the derivation

            /// <summary>
            /// One committed event and one destination always derive the same three identities, and a different event
            /// derives a different obligation. The idempotency key is its own value: it is never the obligation id, so
            /// a destination that deduplicates by it cannot confuse "the same obligation" with "my own identity"
            /// (P-004, P-045, P-050).
            /// </summary>
            private void ProveTheKeyIsDerivedFromCommittedData()
            {
                const string name = "gc021-key-is-derived-from-committed-data";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    DeliveryKey first = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(1UL), SeamDestinationId, SeamCommandSchema);
                    DeliveryKey again = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(1UL), SeamDestinationId, SeamCommandSchema);
                    DeliveryKey other = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(2UL), SeamDestinationId, SeamCommandSchema);
                    DeliveryKey otherDestination = DeliveryKey.Derive(
                        sourceWorld,
                        new EventSequence(1UL),
                        new Id128(SeamIdentityHigh, 9UL),
                        SeamCommandSchema);

                    bool stable = first.Equals(again);
                    bool sequenceMatters = !first.OutboxId.Equals(other.OutboxId);
                    bool destinationMatters = !first.OutboxId.Equals(otherDestination.OutboxId);
                    bool keyDistinct = !first.IdempotencyKey.IsDefault
                        && !first.IdempotencyKey.Equals(first.OutboxId);
                    bool obligationWellFormed = !first.OutboxId.IsDefault
                        && first.DestinationId.Equals(SeamDestinationId);

                    Add(name,
                        stable
                        && sequenceMatters
                        && destinationMatters
                        && keyDistinct
                        && obligationWellFormed,
                        "session=" + sourceWorld.Session.ToString()
                        + "; stableDerivation=True; idempotencyKeyIsDistinct=True"
                        + "; sequenceMatters=" + sequenceMatters
                        + "; destinationMatters=" + destinationMatters
                        + "; keyDistinct=" + keyDistinct
                        + "; obligation=" + first.OutboxId.ToString()
                        + "; idempotencyKey=" + first.IdempotencyKey.ToString()
                        + "; destination=" + first.DestinationId.ToString()
                        + "; schema=" + SeamCommandSchema.ToString()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 2. persist, then apply

            /// <summary>
            /// A durable commit is persisted before it is applied: the frame is in the journal at the moment the
            /// caller is told the obligation is committed, and the row the checkpoint projection carries records the
            /// durability the world really had rather than the durability a reader hoped for (P-045).
            /// </summary>
            private void ProveCommitPersistsBeforeApply()
            {
                const string name = "gc021-commit-persists-before-apply";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    seamJournal = new MemoryDeliveryJournal("memory://gc021-seam-persist");
                    var outbox = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var adapter = new DurableDeliveryAdapter(outbox, seamJournal);

                    DeliveryKey key = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(3UL), SeamDestinationId, SeamCommandSchema);
                    int framesBefore = seamJournal.FrameCount;
                    OutboxAdmission admission = Commit(
                        adapter, key, new EventSequence(3UL), sourceWorld, requiresDurability: true,
                        out DeliveryObligation? obligation, out DiagnosticCode code, out string detail);
                    int framesAfter = seamJournal.FrameCount;
                    int openAfter = outbox.OpenCount;

                    IReadOnlyList<OutboxRecordValue> rows = outbox.ToRecords();
                    bool appendedBeforeApply = framesBefore == 0
                        && framesAfter == 1
                        && openAfter == 1
                        && adapter.AppendCount == 1;
                    bool rowRecordsDurability = rows.Count == 1
                        && rows[0].Row == OutboxRowKind.Obligation
                        && rows[0].DurabilityClass == OutboxDurability.Durable
                        && rows[0].PayloadSchema.Equals(SeamCommandSchema)
                        && rows[0].Payload != null
                        && rows[0].Payload.Length == SeamPayloadBytes;

                    bool pass = admission == OutboxAdmission.Accepted
                        && obligation != null
                        && !obligation.IsTerminal
                        && code == DiagnosticCode.None
                        && adapter.IsDurable
                        && outbox.Durability == OutboxDurability.Durable
                        && appendedBeforeApply
                        && rowRecordsDurability;

                    Add(name, pass,
                        "admission=" + admission
                        + "; code=" + code
                        + "; appendedBeforeApply=" + appendedBeforeApply
                        + "; journalFrameCount=" + framesAfter.ToString(CultureInfo.InvariantCulture)
                        + "; appendCount=" + adapter.AppendCount.ToString(CultureInfo.InvariantCulture)
                        + "; openCount=" + openAfter.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                        + "; rowKind=" + (rows.Count > 0 ? rows[0].Row.ToString() : "<none>")
                        + "; durabilityClass=" + (rows.Count > 0 ? rows[0].DurabilityClass.ToString() : "<none>")
                        + "; outbox=" + outbox.ToString()
                        + "; codecs=" + codecEvidence
                        + "; bindings=" + (bindings != null)
                        + "; detail=" + Clip(detail, 120)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. the crash at a named boundary

            /// <summary>
            /// The population the acknowledgement loss is about: the adapter hands the obligation to the destination,
            /// the destination mutates, and the process dies before the attempt is recorded — so the mutation exists
            /// and this process has no local record of it. The crash is at the seam's own `after-delivery` boundary,
            /// which is the only boundary that sits between those two facts (P-045).
            /// </summary>
            private void ProveCrashAfterDeliveryLosesTheAcknowledgement()
            {
                const string name = "gc021-crash-after-delivery-loses-the-acknowledgement";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    var journal = new MemoryDeliveryJournal("memory://gc021-ack-loss");
                    var outbox = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var hook = new Gc021CrashHook(Gc021CrashHook.AfterDelivery);
                    var adapter = new DurableDeliveryAdapter(outbox, journal, hook);
                    var destination = new Gc021RecordingDestination(SeamDestinationId, SeamCommandSchema);

                    DeliveryKey key = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(4UL), SeamDestinationId, SeamCommandSchema);
                    OutboxAdmission admission = Commit(
                        adapter, key, new EventSequence(4UL), sourceWorld, requiresDurability: true,
                        out DeliveryObligation? obligation, out DiagnosticCode commitCode, out string commitDetail);
                    if (admission != OutboxAdmission.Accepted || obligation == null)
                    {
                        Add(name, false, "the obligation was not committed: " + admission + "/" + commitCode
                            + ": " + commitDetail);
                        return;
                    }

                    Gc021DeliveryCrashException? crash = null;
                    DeliveryOutcome outcome = DeliveryOutcome.None;
                    try
                    {
                        outcome = adapter.TryDeliver(
                            obligation.Key.OutboxId, destination, out DiagnosticCode _, out string _);
                    }
                    catch (Gc021DeliveryCrashException exception)
                    {
                        crash = exception;
                    }

                    bool atTheNamedBoundary = crash != null
                        && string.Equals(crash.Boundary, DeliveryBoundaries.AfterDelivery, StringComparison.Ordinal)
                        && hook.Spent
                        && hook.Observed(DeliveryBoundaries.BeforeDelivery)
                        && hook.Observed(DeliveryBoundaries.AfterDelivery);
                    bool destinationMutatedOnce = destination.MutationCount == 1
                        && destination.Attempts.Count == 1
                        && destination.AlreadyAppliedCount == 0;
                    bool acknowledgementLost = outbox.OpenCount == 1
                        && obligation.IsOpen
                        && outbox.AcknowledgeCount == 0
                        && outbox.DeliveryCount == 1
                        && journal.FrameCount == 1;

                    // The two outboxes of the crash pair share their journal and destination, so the recovery step
                    // reads exactly the state this crash left behind.
                    seamJournal = journal;
                    seamDestination = destination;
                    seamKey = key;

                    Add(name,
                        atTheNamedBoundary && destinationMutatedOnce && acknowledgementLost,
                        "crashPoint=AfterDelivery"
                        + "; boundary=" + (crash == null ? DeliveryBoundaries.None : crash.Boundary)
                        + "; threw=" + (crash != null)
                        + "; hookBoundary=" + hook.Boundary
                        + "; hookReaches=" + hook.ReachCount.ToString(CultureInfo.InvariantCulture)
                        + "; beforeDeliveryObserved=" + hook.Observed(DeliveryBoundaries.BeforeDelivery)
                        + "; afterDeliveryObserved=" + hook.Observed(DeliveryBoundaries.AfterDelivery)
                        + "; destinationMutations=" + destination.MutationCount.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAttempts=" + destination.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "; outcomeBeforeCrash=" + outcome
                        + "; openAfterCrash=" + outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                        + "; deliveries=" + outbox.DeliveryCount.ToString(CultureInfo.InvariantCulture)
                        + "; acknowledgements=" + outbox.AcknowledgeCount.ToString(CultureInfo.InvariantCulture)
                        + "; journalFrameCount=" + journal.FrameCount.ToString(CultureInfo.InvariantCulture)
                        + "; key=" + key.OutboxId.ToString()
                        + "; detail=" + Clip(crash == null ? string.Empty : crash.Detail, 120)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. the headline: redelivery applies once

            /// <summary>
            /// The acknowledgement-loss window, closed: a fresh adapter over the same journal, carrying no in-memory
            /// state across, finds the obligation still owed, and the redelivery reaches the same destination with the
            /// same external idempotency key — which is why the destination applies the mutation exactly once. The
            /// marker has already been spent, so this second delivery is not interrupted (P-045, P-049, P-050).
            /// </summary>
            private void ProveRedeliveryAppliesTheMutationOnce()
            {
                const string name = "gc021-redelivery-applies-the-mutation-once";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    MemoryDeliveryJournal? journal = seamJournal;
                    Gc021RecordingDestination? destination = seamDestination;
                    if (journal == null || destination == null)
                    {
                        Add(name, false, "the crash step left no journal or destination to recover over");
                        return;
                    }

                    var recovered = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var second = new DurableDeliveryAdapter(recovered, journal);
                    if (!second.TryRecover(out DiagnosticCode recoverCode, out string recoverDetail))
                    {
                        Add(name, false, "recovering from the journal was refused: " + recoverCode + ": "
                            + recoverDetail);
                        return;
                    }

                    int recoveredOpen = recovered.OpenCount;
                    bool reinstatedCarriesTheKey = recovered.TryGet(
                            seamKey.OutboxId, out DeliveryObligation? reinstated)
                        && reinstated != null
                        && reinstated.Key.IdempotencyKey.Equals(seamKey.IdempotencyKey)
                        && reinstated.IsOpen;

                    DeliveryOutcome outcome = second.TryDeliver(
                        seamKey.OutboxId, destination, out DiagnosticCode deliveryCode, out string deliveryDetail);

                    bool settled = outcome == DeliveryOutcome.Acknowledged
                        && recovered.OpenCount == 0
                        && recovered.AcknowledgeCount == 1
                        && recovered.TryGetCursor(SeamDestinationId, out DeliveryCursor cursor)
                        && cursor.HasAcknowledged
                        && cursor.NewestAcknowledged.Equals(seamKey.OutboxId);
                    bool appliedOnce = destination.MutationCount == 1
                        && destination.AlreadyAppliedCount == 1
                        && destination.Attempts.Count == 2
                        && second.AlreadyAppliedCount == 1;

                    Add(name,
                        recoveredOpen == 1
                        && reinstatedCarriesTheKey
                        && settled
                        && appliedOnce
                        && second.RecoveryCount == 1
                        && second.RecoveredFrameCount >= 1,
                        "recoveredOpen=" + recoveredOpen.ToString(CultureInfo.InvariantCulture)
                        + "; reinstatedKeyMatches=" + reinstatedCarriesTheKey
                        + "; outcome=" + outcome
                        + "; code=" + deliveryCode
                        + "; recoveredCommitCount=" + recovered.CommitCount.ToString(CultureInfo.InvariantCulture)
                        + "; recoveredDeliveryCount=" + recovered.DeliveryCount.ToString(CultureInfo.InvariantCulture)
                        + "; settledOpen=" + recovered.OpenCount.ToString(CultureInfo.InvariantCulture)
                        + "; acknowledgements=" + recovered.AcknowledgeCount.ToString(CultureInfo.InvariantCulture)
                        + "; redeliveredMutations=" + destination.MutationCount.ToString(CultureInfo.InvariantCulture)
                        + "; mutationCount=" + destination.MutationCount.ToString(CultureInfo.InvariantCulture)
                        + "; alreadyApplied=" + destination.AlreadyAppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; adapterAlreadyApplied=" + second.AlreadyAppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAttempts=" + destination.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "; recoveredFrames=" + second.RecoveredFrameCount.ToString(CultureInfo.InvariantCulture)
                        + "; detail=" + Clip(deliveryDetail, 120)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. capacity is never a silent drop

            /// <summary>
            /// An outbox at its declared capacity refuses the next obligation loudly: the caller gets
            /// `OutboxAdmission.AtCapacity` with a coded reason, the refusal is counted, and the refused obligation is
            /// nowhere — not in the tracked set, not in the open order. That is P-043's "no silent drop" applied to an
            /// outbox instead of a buffer.
            /// </summary>
            private void ProveCapacityExhaustionIsNeverASilentDrop()
            {
                const string name = "gc021-capacity-exhaustion-is-never-a-silent-drop";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    var outbox = new DurableOutbox(
                        SeamOwnerId, OneObligation, SeamTerminalRetention, OutboxDurability.Durable);
                    var adapter = new DurableDeliveryAdapter(outbox);

                    DeliveryKey first = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(5UL), SeamDestinationId, SeamCommandSchema);
                    DeliveryKey second = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(6UL), SeamDestinationId, SeamCommandSchema);

                    OutboxAdmission admitted = Commit(
                        adapter, first, new EventSequence(5UL), sourceWorld, requiresDurability: false,
                        out DeliveryObligation? _, out DiagnosticCode firstCode, out string firstDetail);
                    OutboxAdmission refused = Commit(
                        adapter, second, new EventSequence(6UL), sourceWorld, requiresDurability: false,
                        out DeliveryObligation? refusedObligation, out DiagnosticCode code, out string detail);

                    bool tracked = outbox.TryGet(second.OutboxId, out DeliveryObligation? _);
                    bool inOpenOrder = false;
                    IReadOnlyList<Id128> openIds = outbox.OpenIds;
                    for (int i = 0; i < openIds.Count; i++)
                    {
                        if (openIds[i].Equals(second.OutboxId))
                        {
                            inOpenOrder = true;
                        }
                    }

                    Add(name,
                        admitted == OutboxAdmission.Accepted
                        && refused == OutboxAdmission.AtCapacity
                        && code == DiagnosticCode.BudgetExceeded
                        && refusedObligation == null
                        && outbox.OpenCount == 1
                        && outbox.Count == 1
                        && outbox.CapacityRefusalCount == 1
                        && !tracked
                        && !inOpenOrder
                        && outbox.Capacity == OneObligation,
                        "atCapacity=" + (refused == OutboxAdmission.AtCapacity)
                        + "; capacityCode=" + code
                        + "; admitted=" + admitted
                        + "; firstCode=" + firstCode
                        + "; capacity=" + outbox.Capacity.ToString(CultureInfo.InvariantCulture)
                        + "; openCount=" + outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                        + "; count=" + outbox.Count.ToString(CultureInfo.InvariantCulture)
                        + "; capacityRefusals=" + outbox.CapacityRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; refusedTracked=" + tracked
                        + "; refusedInOpenOrder=" + inOpenOrder
                        + "; refusedObligationPresent=" + (refusedObligation != null)
                        + "; detail=" + Clip(detail.Length == 0 ? firstDetail : detail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 6. volatile is not durable

            /// <summary>
            /// Volatile and durable are distinguishable values, not a comment: an adapter with no journal declares
            /// `Volatile`, cannot recover, and refuses an obligation that requires durability instead of accepting one
            /// it would lose with the session. The durable adapter beside it, over the same content, accepts the same
            /// obligation and holds it — so the refusal is about the durability the adapter has, not about the
            /// obligation (P-045).
            /// </summary>
            private void ProveVolatileDeliveryIsDistinguishableFromDurable()
            {
                const string name = "gc021-volatile-delivery-is-distinguishable-from-durable";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    var volatileOutbox = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var volatileAdapter = new DurableDeliveryAdapter(volatileOutbox);
                    bool recoverable = volatileAdapter.TryRecover(
                        out DiagnosticCode recoverCode, out string recoverDetail);

                    DeliveryKey key = DeliveryKey.Derive(
                        sourceWorld, new EventSequence(7UL), SeamDestinationId, SeamCommandSchema);
                    OutboxAdmission volatileAdmission = Commit(
                        volatileAdapter, key, new EventSequence(7UL), sourceWorld, requiresDurability: true,
                        out DeliveryObligation? volatileObligation, out DiagnosticCode volatileCode,
                        out string volatileDetail);

                    var durableJournal = new MemoryDeliveryJournal("memory://gc021-contrast");
                    var durableOutbox = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var durableAdapter = new DurableDeliveryAdapter(durableOutbox, durableJournal);
                    OutboxAdmission durableAdmission = Commit(
                        durableAdapter, key, new EventSequence(7UL), sourceWorld, requiresDurability: true,
                        out DeliveryObligation? durableObligation, out DiagnosticCode durableCode,
                        out string durableDetail);

                    Add(name,
                        !volatileAdapter.IsDurable
                        && volatileAdapter.Journal == null
                        && volatileAdapter.Durability == OutboxDurability.Volatile
                        && !recoverable
                        && recoverCode == DiagnosticCode.ResourceUnavailable
                        && volatileAdmission == OutboxAdmission.DurabilityUnavailable
                        && volatileCode == DiagnosticCode.ResourceUnavailable
                        && volatileObligation == null
                        && volatileOutbox.Count == 0
                        && volatileOutbox.DurabilityRefusalCount == 1
                        && durableAdapter.IsDurable
                        && durableAdapter.Durability == OutboxDurability.Durable
                        && durableOutbox.Durability == OutboxDurability.Durable
                        && durableAdmission == OutboxAdmission.Accepted
                        && durableObligation != null
                        && durableOutbox.Count == 1
                        && durableJournal.FrameCount == 1,
                        "volatileRefused=" + (volatileAdmission == OutboxAdmission.DurabilityUnavailable)
                        + "; volatileCode=" + volatileCode
                        + "; volatileDurability=" + volatileAdapter.Durability
                        + "; volatileRecoverable=" + recoverable
                        + "; recoverCode=" + recoverCode
                        + "; volatileCount=" + volatileOutbox.Count.ToString(CultureInfo.InvariantCulture)
                        + "; durabilityRefusals="
                        + volatileOutbox.DurabilityRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; durableAccepted=" + (durableAdmission == OutboxAdmission.Accepted)
                        + "; durableCode=" + durableCode
                        + "; durability=" + volatileAdapter.Durability + "->" + durableAdapter.Durability
                        + "; durableCount=" + durableOutbox.Count.ToString(CultureInfo.InvariantCulture)
                        + "; durableFrames=" + durableJournal.FrameCount.ToString(CultureInfo.InvariantCulture)
                        + "; recoverDetail=" + Clip(recoverDetail, 100)
                        + "; volatileDetail=" + Clip(volatileDetail, 120)
                        + "; durableDetail=" + Clip(durableDetail, 100)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. the commitment outlives the world

            /// <summary>
            /// The commitment survives the world that made it: the obligation's rows are taken from the source
            /// world's outbox, the source world is stopped and disposed, and an outbox rebuilt from those rows in a
            /// fresh session still owes the same payload under the same external key — and can hand it over and have
            /// it acknowledged, even though the session that committed it is gone (P-045, P-049, P-053).
            ///
            /// The successor world is created through the family's own creation request and registration, with its own
            /// reserved `WorldId`, so "the same way but a different session" is a comparison of two real worlds rather
            /// than of two integers.
            /// </summary>
            private void ProveObligationSurvivesTheSourceWorldUnload()
            {
                const string name = "gc021-obligation-survives-the-source-world-unload";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    unloadSession = new WorldId(sessionSequence.Next());
                    if (!TryCreateBareWorld(unloadSession, out UnityWorldHost? unload, out string unloadDetail))
                    {
                        Add(name, false, "creating the source world for this step was refused: " + unloadDetail);
                        return;
                    }

                    unloadHost = unload;
                    successorSession = new WorldId(sessionSequence.Next());
                    if (!TryCreateBareWorld(successorSession, out UnityWorldHost? successor, out string successorDetail))
                    {
                        Add(name, false, "creating the successor world was refused: " + successorDetail);
                        return;
                    }

                    successorHost = successor;
                    bool sessionsDiffer = !unloadSession.Session.Equals(successorSession.Session);

                    var journal = new MemoryDeliveryJournal("memory://gc021-unload");
                    var outbox = new DurableOutbox(
                        SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Unspecified);
                    var adapter = new DurableDeliveryAdapter(outbox, journal);
                    DeliveryKey key = DeliveryKey.Derive(
                        unloadSession, new EventSequence(1UL), SeamDestinationId, SeamCommandSchema);
                    OutboxAdmission admission = Commit(
                        adapter, key, new EventSequence(1UL), unloadSession, requiresDurability: true,
                        out DeliveryObligation? committed, out DiagnosticCode commitCode, out string commitDetail);
                    if (admission != OutboxAdmission.Accepted || committed == null)
                    {
                        Add(name, false, "the obligation was not committed in the source world: " + admission + "/"
                            + commitCode + ": " + commitDetail);
                        return;
                    }

                    byte[] recorded = committed.PayloadBytes();
                    IReadOnlyList<OutboxRecordValue> rows = outbox.ToRecords();

                    // The source world is stopped and disposed *before* the rows are reinstated, so nothing about the
                    // reinstated obligation can be reading the session that committed it.
                    UnityWorldHost retiring = unloadHost!;
                    OperationResult stop = retiring.Stop(
                        NextOperation(unloadSession), "gc-021 delivery-unload proof");
                    retiring.Dispose();
                    bool sourceGone = !UnityWorldRegistry.TryGet(unloadSession, out UnityWorldHost? _);
                    unloadHost = null;

                    bool restored = DurableOutbox.TryRestore(
                        rows, SeamOwnerId, SeamCapacity, SeamTerminalRetention, OutboxDurability.Durable,
                        out DurableOutbox? rebuilt, out string restoreDetail);
                    if (!restored || rebuilt == null)
                    {
                        Add(name, false, "reinstating the outbox rows was refused: " + restoreDetail);
                        return;
                    }

                    bool carriesTheObligation = rebuilt.OpenCount == 1
                        && rebuilt.CommitCount == 1
                        && rebuilt.TryGet(key.OutboxId, out DeliveryObligation? reinstated)
                        && reinstated != null
                        && reinstated.Key.IdempotencyKey.Equals(key.IdempotencyKey)
                        && reinstated.SourceEvent.Equals(new EventSequence(1UL))
                        && PayloadEquals(reinstated.PayloadBytes(), recorded);

                    var destination = new Gc021RecordingDestination(SeamDestinationId, SeamCommandSchema);
                    var deliverer = new DurableDeliveryAdapter(
                        rebuilt, new MemoryDeliveryJournal("memory://gc021-unload-successor"));
                    DeliveryOutcome outcome = deliverer.TryDeliver(
                        key.OutboxId, destination, out DiagnosticCode deliveryCode, out string deliveryDetail);

                    int registryAfter = UnityWorldRegistry.Count;
                    bool successorAlive = UnityWorldRegistry.TryGet(successorSession, out UnityWorldHost? _);

                    Add(name,
                        sessionsDiffer
                        && sourceGone
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && carriesTheObligation
                        && rebuilt.IsDurable
                        && outcome == DeliveryOutcome.Acknowledged
                        && destination.MutationCount == 1
                        && successorAlive,
                        "sourceSession=" + unloadSession.Session.ToString()
                        + "; successorSession=" + successorSession.Session.ToString()
                        + "; sessionsDiffer=" + sessionsDiffer
                        + "; sourceStopped=" + stop.Outcome
                        + "; sourceUnregistered=" + sourceGone
                        + "; rowsReinstated=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                        + "; rebuiltOpen=" + rebuilt.OpenCount.ToString(CultureInfo.InvariantCulture)
                        + "; rebuiltCommitCount=" + rebuilt.CommitCount.ToString(CultureInfo.InvariantCulture)
                        + "; rebuiltDeliveryCount=" + rebuilt.DeliveryCount.ToString(CultureInfo.InvariantCulture)
                        + "; payloadEqual=" + PayloadEquals(
                            rebuilt.TryGet(key.OutboxId, out DeliveryObligation? round) && round != null
                                ? round.PayloadBytes()
                                : Array.Empty<byte>(),
                            recorded)
                        + "; outcome=" + outcome
                        + "; code=" + deliveryCode
                        + "; destinationMutations=" + destination.MutationCount.ToString(CultureInfo.InvariantCulture)
                        + "; successorRunning=" + successorAlive
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; registryBaseline=" + registryBaseline.ToString(CultureInfo.InvariantCulture)
                        + "; detail=" + Clip(deliveryDetail, 100)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7b. the committed event and its reward source

            /// <summary>
            /// The one committed event this run's reward comes from. The family's own declared command is submitted
            /// through the world's own ingress and pumped, so the event is a *committed* event of the family's real
            /// world executed by the family's own stage systems (P-037, P-042, P-044).
            ///
            /// The narrative family's accepted choice is the event, and the node it landed on is read out of the
            /// committed payload — the observation the reward content is then declared over. A card world commits card
            /// results and no narrative choice, so the event is the newest committed result of that world and the node
            /// is reported as absent rather than invented.
            ///
            /// Nothing is asserted here: it is the setup the two reward observations and the capture step read.
            /// </summary>
            private void DriveAndObserveTheRewardSource()
            {
                try
                {
                    if (setupFailure.Length != 0 || host == null || time == null || lane == null || publisher == null
                        || targets == null || registry == null || rng == null || codecs == null)
                    {
                        rewardSetupFailure = setupFailure.Length != 0
                            ? setupFailure
                            : "the family's world or its pipeline is missing";
                        return;
                    }

                    WorldMessagePlane? plane = host.Messages;
                    if (plane == null)
                    {
                        rewardSetupFailure = "the world declares no message plane, so no committed event exists";
                        return;
                    }

                    CommandEnvelope envelope = family.QueuedCommand(sourceWorld, NextOperation(sourceWorld));
                    CommandAdmissionReceipt receipt = host.Submit(envelope);
                    if (!receipt.Admitted)
                    {
                        rewardSetupFailure = "the family's own command was not admitted: " + receipt.Result.Kind + "/"
                            + receipt.Result.Reason;
                        return;
                    }

                    TimeFrameReport frame = time.PumpFrame(IdlePumpTicks);

                    EventSequence last = plane.LastEventSequence;
                    CommittedEventPage page = plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 32);
                    if (page.Outcome != CursorOutcome.Ok || page.Events.Count == 0)
                    {
                        rewardSetupFailure = "the world published no readable committed events after pumping "
                            + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture) + " step(s): "
                            + page.Outcome;
                        return;
                    }

                    SchemaRef sourceSchema = GenreSchema();
                    EventSequence selected = default(EventSequence);
                    SchemaRef selectedSchema = default(SchemaRef);
                    byte[] selectedPayload = Array.Empty<byte>();
                    bool found = false;
                    for (int i = 0; i < page.Events.Count; i++)
                    {
                        CommittedEvent candidate = page.Events[i];
                        if (!candidate.Schema.Equals(sourceSchema))
                        {
                            continue;
                        }

                        // The newest committed event of the family's own result schema, so the selection is
                        // deterministic and does not depend on retention order (P-008).
                        if (!found || candidate.Cursor.Sequence.Value >= selected.Value)
                        {
                            selected = candidate.Cursor.Sequence;
                            selectedSchema = candidate.Schema;
                            selectedPayload = CopyPayload(candidate.Payload);
                            rewardEventStep = candidate.Step;
                            rewardEventEpoch = candidate.Epoch;
                            rewardEventCausal = candidate.CausalRequest;
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        rewardSetupFailure = "no committed event of schema " + sourceSchema.ToString()
                            + " was published by this world (last=" + last.Value.ToString(CultureInfo.InvariantCulture)
                            + ", events=" + page.Events.Count.ToString(CultureInfo.InvariantCulture)
                            + ", lastSchema=" + page.Events[page.Events.Count - 1].Schema.ToString() + ")";
                        return;
                    }

                    rewardEvent = selected;
                    rewardEventEvidence = "schema=" + selectedSchema.ToString()
                        + "; sequence=" + selected.Value.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + rewardEventStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + rewardEventEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldLastSequence=" + last.Value.ToString(CultureInfo.InvariantCulture);
                    observedNodeOrdinal = genre == Gc021Genre.Narrative
                        ? NarrativePayloadCodec.ReadInt32(selectedPayload, 0)
                        : Gc021RewardContent.NoObservedNode;

                    int contentNode = genre == Gc021Genre.Narrative
                        ? observedNodeOrdinal
                        : Gc021RewardContent.CardsDeclaredNode;
                    content = Gc021RewardContent.ForNode(contentNode);
                    RewardCatalog declaredContent = content.ToCatalog(
                        requiresDurability: true, out string contentDetail);
                    catalog = declaredContent;
                    if (declaredContent.Count != 1
                        || !declaredContent.RequiresDurability
                        || contentDetail.Length != 0)
                    {
                        rewardSetupFailure = "declaring the reward content was refused: " + contentDetail;
                        return;
                    }

                    rewardPayload = RewardPayloadCodec.Write(content.Definition());
                    rewardKey = DeliveryKey.Derive(
                        sourceWorld, rewardEvent, CardTableConstants.DestinationId, CardTableConstants.CommandSchema);

                    var journal = new MemoryDeliveryJournal("memory://gc021-rewards");
                    rewardBridge = new NarrativeCardRewardBridge(
                        host,
                        time,
                        RewardOwnerId,
                        family.Issuer,
                        declaredContent,
                        RewardCapacity,
                        RewardTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        null);
                    rewardOwner = rewardBridge.Owner;

                    // The source is the bridge itself where the world commits a narrative choice, and a declared
                    // one-event source where it commits card results instead (see the file header).
                    rewardSource = genre == Gc021Genre.Narrative
                        ? null
                        : new Gc021CommittedEventRewardSource(
                            selectedSchema, rewardEvent, CardTableConstants.DestinationId,
                            CardTableConstants.CommandSchema, rewardPayload, true);
                }
                catch (Exception exception)
                {
                    rewardSetupFailure = DescribeException(exception);
                }
            }

            // ------------------------------------------------------------------ 8. the checkpoint carries the outbox

            /// <summary>
            /// The outbox and its cursor are part of a committed boundary, not a test-local copy: a real capture is
            /// driven with the world owner's own rows, the document's outbox section decodes back to exactly those
            /// rows, the header's count agrees with them, at least one row is an open obligation, and the restore
            /// planner carries the same rows into its plan — which is the outbox half of P-053 ("checkpoint carries
            /// the outbox and the cursor", and P-045's "persist an outbox when delivery must survive crashes").
            ///
            /// Executing the restore is deliberately not attempted here: GC-018's own suite already proves the executor
            /// path end to end, and this step says so rather than faking it.
            /// </summary>
            private void ProveCheckpointCarriesTheOutboxAndTheCursor()
            {
                const string name = "gc021-checkpoint-carries-the-outbox-and-the-cursor";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    if (rewardSetupFailure.Length != 0 || rewardOwner == null || codecs == null)
                    {
                        Add(name, false, rewardSetupFailure.Length != 0
                            ? rewardSetupFailure
                            : "the reward owner is missing");
                        return;
                    }

                    int enqueued = PollTheRewardSource();
                    IReadOnlyList<OutboxRecordValue> ownerRows = rewardOwner.ToRecords();
                    if (ownerRows.Count == 0)
                    {
                        Add(name, false, "polling committed events enqueued no obligation, so the boundary carries no "
                            + "outbox row to capture (observed="
                            + rewardOwner.ObservedEventCount.ToString(CultureInfo.InvariantCulture) + ", unclaimed="
                            + rewardOwner.UnclaimedEventCount.ToString(CultureInfo.InvariantCulture) + ")");
                        return;
                    }

                    var context = new CaptureContext(
                        sourceWorld,
                        sourceRequest.Definition,
                        catalogFingerprint,
                        targets!,
                        registry!,
                        null,
                        null,
                        rng!,
                        lane!.Committed.Mode,
                        StepDurationTicks,
                        TicksPerSecond,
                        MaxStepsPerPump,
                        false,
                        lane,
                        publisher,
                        Array.Empty<BufferId>(),
                        null,
                        ownerRows);

                    var reader = new UnityCommittedBoundaryReader(host!, context);
                    bool atBoundary = reader.IsAtCommittedBoundary;
                    CheckpointCaptureResult capture = CheckpointCapture.Capture(
                        reader,
                        new CheckpointCaptureRequest(
                            sourceWorld, codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint));
                    if (!capture.Captured)
                    {
                        Add(name, false, "the boundary capture was refused: " + capture.Code + ": " + capture.Detail);
                        return;
                    }

                    if (!CheckpointDocument.TryRead(
                            capture.Document, codecs, out CheckpointDocument? document, out DiagnosticCode code,
                            out string detail)
                        || document == null)
                    {
                        Add(name, false, "the captured document was refused: " + code + ": " + detail);
                        return;
                    }

                    if (!document.TryReadRecords(
                            CheckpointRecordKind.Outbox, out IReadOnlyList<OutboxRecordValue> documentRows,
                            out code, out detail))
                    {
                        Add(name, false, "the document's outbox section did not decode: " + code + ": " + detail);
                        return;
                    }

                    int openRows = 0;
                    for (int i = 0; i < documentRows.Count; i++)
                    {
                        if (documentRows[i].Row == OutboxRowKind.Obligation
                            && documentRows[i].State == OutboxDeliveryState.Pending)
                        {
                            openRows++;
                        }
                    }

                    bool rowsMatch = OutboxRowsEqual(ownerRows, documentRows);
                    bool headerAgrees = document.Header.OutboxCount == (uint)documentRows.Count;

                    // The cursor half of the plan: a fresh reserved session, so the plan is validated against a
                    // session that is not the captured one (P-049, P-053).
                    WorldId planSession = new WorldId(sessionSequence.Next());
                    RestorePlanResult planned = CheckpointRestorePlanner.Plan(
                        new CheckpointRestoreRequest(
                            planSession,
                            document,
                            codecs,
                            new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>()),
                            catalogFingerprint,
                            null,
                            true));
                    bool planCarriesTheRows = planned.Succeeded
                        && planned.Plan != null
                        && OutboxRowsEqual(planned.Plan.Outbox, documentRows)
                        && OutboxRowsEqual(planned.Plan.Outbox, ownerRows);

                    Add(name,
                        atBoundary
                        && enqueued >= 1
                        && rowsMatch
                        && headerAgrees
                        && openRows >= 1
                        && planCarriesTheRows
                        && capture.SourceWorld.Session.Equals(sourceWorld.Session),
                        "session=" + sourceWorld.Session.ToString()
                        + "; atBoundary=" + atBoundary
                        + "; laneJoined=" + (laneJoin != null && ReferenceEquals(laneJoin.Composition, lane))
                        + "; bytes=" + capture.Document.Length.ToString(CultureInfo.InvariantCulture)
                        + "; headerOutboxCount=" + document.Header.OutboxCount.ToString(CultureInfo.InvariantCulture)
                        + "; documentOutboxRows=" + documentRows.Count.ToString(CultureInfo.InvariantCulture)
                        + "; openRows=" + openRows.ToString(CultureInfo.InvariantCulture)
                        + "; ownerRows=" + ownerRows.Count.ToString(CultureInfo.InvariantCulture)
                        + "; rowsMatch=" + rowsMatch
                        + "; headerAgrees=" + headerAgrees
                        + "; plannedRows=" + (planned.Plan == null
                            ? -1
                            : planned.Plan.Outbox.Count)
                        + "; planHolds=" + planCarriesTheRows
                        + "; planSession=" + planSession.Session.ToString()
                        + "; planRefusal=" + (planned.Succeeded ? "none" : planned.Refusal + "/" + planned.Code)
                        + "; planDetail=" + Clip(planned.Detail, 140)
                        + "; executorRestore=not-run (GC-018's suite proves the executor path)"
                        + "; counts=" + capture.Counts.ToString()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 9. no universal effect API

            /// <summary>
            /// The task's non-goal, falsified from the code that would have to declare it: neither the destination
            /// port, nor the durable adapter, nor the reward bridge declares a member whose name promises to reverse
            /// or universally apply a change. A granted card, a retracted capability contribution and a released
            /// resource lease do not have the same semantics, so there is no `Undo`, `Revert`, `Rollback` or `Effect`
            /// on the seam (P-003). The member counts are reported so the check is visibly about a real surface.
            /// </summary>
            private void ProveNoUniversalEffectApi()
            {
                const string name = "gc021-no-universal-effect-api";
                try
                {
                    int portMembers = typeof(IDestinationPort).GetMembers().Length;
                    int adapterMembers = typeof(DurableDeliveryAdapter).GetMembers().Length;
                    int bridgeMembers = typeof(NarrativeCardRewardBridge).GetMembers().Length;
                    int forbidden = CountForbidden(typeof(IDestinationPort))
                        + CountForbidden(typeof(DurableDeliveryAdapter))
                        + CountForbidden(typeof(NarrativeCardRewardBridge));
                    bool declaresItsOwnVerbs = HasMember(typeof(IDestinationPort), "TryApply")
                        && HasMember(typeof(DurableDeliveryAdapter), "TryDeliver")
                        && HasMember(typeof(NarrativeCardRewardBridge), "Run");

                    Add(name,
                        forbidden == 0
                        && declaresItsOwnVerbs
                        && portMembers >= 3
                        && adapterMembers >= 8
                        && bridgeMembers >= 8,
                        "portMembers=" + portMembers.ToString(CultureInfo.InvariantCulture)
                        + "; adapterMembers=" + adapterMembers.ToString(CultureInfo.InvariantCulture)
                        + "; bridgeMembers=" + bridgeMembers.ToString(CultureInfo.InvariantCulture)
                        + "; reverseMembers=" + forbidden.ToString(CultureInfo.InvariantCulture)
                        + "; forbidden=" + Join(ForbiddenMemberFragments)
                        + "; portVerbs=" + MemberNames(typeof(IDestinationPort))
                        + "; adapterVerbs=" + MemberNames(typeof(DurableDeliveryAdapter))
                        + "; bridgeVerbs=" + MemberNames(typeof(NarrativeCardRewardBridge))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 10. the committed choice is observed

            /// <summary>
            /// The one committed event the reward comes from was really observed. The narrative world commits its own
            /// accepted choice, the bridge recognises it against its declared content, and the obligation that appears
            /// in the outbox is the one derived from that committed event's own sequence — so "the reward belongs to a
            /// committed event" is a comparison of two derived identities, not a claim (P-045, P-050).
            ///
            /// A card world commits card results and no narrative choice. That is reported as the truth it is: no
            /// recognised choice, and a reward obligation derived from the newest committed card result instead.
            /// </summary>
            private void ProveTheChoiceIsObserved()
            {
                const string name = "gc021-narrative-choice-is-observed";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    if (rewardSetupFailure.Length != 0 || rewardOwner == null || rewardBridge == null || content == null)
                    {
                        Add(name, false, rewardSetupFailure.Length != 0
                            ? rewardSetupFailure
                            : "the reward bridge is missing");
                        return;
                    }

                    // The open obligations from the boundary capture are handed to the destination by the bridge's own
                    // bounded pass, so the destination's own report is what this half of the run reads (07 s5 step 3).
                    RewardBridgePassReport pass = rewardBridge.Run(
                        default(OperationId), RewardEventWindow, RewardDispatchWindow);

                    bool obligationIsTheDerivedOne = rewardOwner.Outbox.TryGet(
                            rewardKey.OutboxId, out DeliveryObligation? obligation)
                        && obligation != null
                        && obligation.Key.IdempotencyKey.Equals(rewardKey.IdempotencyKey)
                        && obligation.SourceEvent.Equals(rewardEvent);
                    bool recognised = rewardOwner.ObservedEventCount >= 1 && rewardBridge.RecognisedCount >= 1;
                    bool narrative = genre == Gc021Genre.Narrative;
                    bool observed = narrative
                        ? recognised
                            && observedNodeOrdinal >= 0
                            && obligationIsTheDerivedOne
                            && rewardOwner.EnqueuedCount == 1
                        : rewardOwner.ObservedEventCount >= 1
                            && rewardOwner.EnqueuedCount == 1
                            && rewardBridge.RecognisedCount == 0
                            && observedNodeOrdinal == Gc021RewardContent.NoObservedNode
                            && obligationIsTheDerivedOne;

                    Add(name, observed,
                        "family=" + family.Label
                        + "; genre=" + genre
                        + "; observedEvents=" + rewardOwner.ObservedEventCount.ToString(CultureInfo.InvariantCulture)
                        + "; committedEvent={" + rewardEventEvidence + "}"
                        + "; unclaimedEvents=" + rewardOwner.UnclaimedEventCount.ToString(CultureInfo.InvariantCulture)
                        + "; recognisedChoices=" + rewardBridge.RecognisedCount.ToString(CultureInfo.InvariantCulture)
                        + "; unrewardedChoices="
                        + rewardBridge.UnrewardedChoiceCount.ToString(CultureInfo.InvariantCulture)
                        + "; enqueued=" + rewardOwner.EnqueuedCount.ToString(CultureInfo.InvariantCulture)
                        + "; duplicateEvents=" + rewardOwner.DuplicateEventCount.ToString(CultureInfo.InvariantCulture)
                        + "; observedNode=" + observedNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                        + "; rewardEvent=" + rewardEvent.Value.ToString(CultureInfo.InvariantCulture)
                        + "; obligationIsTheDerivedOne=" + obligationIsTheDerivedOne
                        + "; dispatched=" + pass.Dispatched.ToString(CultureInfo.InvariantCulture)
                        + "; destinationStatus=" + rewardBridge.Destination.LastReport.Status
                        + "; destinationMutations="
                        + rewardBridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; mutationCount="
                        + rewardBridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; submitted=" + rewardBridge.Destination.SubmittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; alreadyPresent="
                        + rewardBridge.Destination.AlreadyPresentCount.ToString(CultureInfo.InvariantCulture)
                        + "; missing=" + rewardBridge.Destination.MissingCount.ToString(CultureInfo.InvariantCulture)
                        + "; content={" + content.Describe() + "}"
                        + "; describeDetail=" + Clip(rewardBridge.LastDescribeDetail, 140)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 11. the content covers the node

            /// <summary>
            /// The content the run declared really covers what the run observed, and the basis of that claim is named
            /// rather than implied:
            ///
            ///   * the narrative family: the definition's node ordinal equals the node the committed choice landed on,
            ///     which the step reads out of the committed payload — so an unrewarded node and a covered node are
            ///     distinguishable, and the failure detail names the observed node (P-015);
            ///   * the card family: a card world carries no narrative choice, so the content is proved by its use — the
            ///     card family's own `Transfer` committed, which requires the payload to have decoded, the holding seat
            ///     to stock the card and the recipient's hand to have room for it (P-034, P-054).
            /// </summary>
            private void ProveTheRewardContentCoversTheObservedNode()
            {
                const string name = "gc021-reward-content-covers-the-observed-node";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    if (rewardSetupFailure.Length != 0 || content == null || rewardBridge == null)
                    {
                        Add(name, false, rewardSetupFailure.Length != 0
                            ? rewardSetupFailure
                            : "the reward content is missing");
                        return;
                    }

                    bool narrative = genre == Gc021Genre.Narrative;
                    RewardAttemptReport last = rewardBridge.Destination.LastReport;
                    bool covered = narrative
                        ? observedNodeOrdinal >= 0 && content.NodeOrdinal == observedNodeOrdinal
                        : last.Status == RewardAttemptStatus.Committed && last.Applied;
                    string basis = narrative ? "observed-choice-node" : "committed-reward-transfer";

                    if (narrative && !covered)
                    {
                        Add(name, false, "the reward content does not cover the observed node: observedNode="
                            + observedNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                            + "; contentNode=" + content.NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                            + "; basis=" + basis
                            + "; content={" + content.Describe() + "}"
                            + "; describeDetail=" + Clip(rewardBridge.LastDescribeDetail, 140));
                        return;
                    }

                    Add(name,
                        covered
                        && catalog != null
                        && catalog.TryGetDefinition(content.NodeOrdinal, out RewardDefinition? declared)
                        && declared != null
                        && declared.Card.Equals(content.Card)
                        && declared.RecipientSeat == content.RecipientSeat
                        && declared.HoldingSeat == content.HoldingSeat,
                        "observedNode=" + observedNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                        + "; contentNode=" + content.NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                        + "; covered=" + covered
                        + "; basis=" + basis
                        + "; content={" + content.Describe() + "}"
                        + "; destinationStatus=" + last.Status
                        + "; destinationApplied=" + last.Applied
                        + "; settlement=" + last.Settlement
                        + "; recipientCards=" + last.RecipientCardsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + last.RecipientCardsAfter.ToString(CultureInfo.InvariantCulture)
                        + "; detail=" + Clip(last.Detail, 140)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 12. one committed event, one reward

            /// <summary>
            /// P-050 and P-045 together: the reward obligation is durable, a second pass over the same committed
            /// events creates no second obligation, and re-deriving the same committed event and destination lands on
            /// the obligation that already exists (`Duplicate`) rather than a new one. One committed choice is one
            /// reward, and the obligation is settled or still owed according to the destination's own answer.
            /// </summary>
            private void ProveTheRewardObligationIsDurableAndIdempotent()
            {
                const string name = "gc021-reward-obligation-is-durable-and-idempotent";
                try
                {
                    if (!Ready(name))
                    {
                        return;
                    }

                    if (rewardSetupFailure.Length != 0 || rewardOwner == null || rewardBridge == null
                        || catalog == null)
                    {
                        Add(name, false, rewardSetupFailure.Length != 0
                            ? rewardSetupFailure
                            : "the reward bridge is missing");
                        return;
                    }

                    int enqueuedBefore = rewardOwner.EnqueuedCount;
                    int countBefore = rewardOwner.Outbox.Count;
                    int duplicatesBefore = rewardOwner.Outbox.DuplicateCount;

                    RewardBridgePassReport second = rewardBridge.Run(
                        default(OperationId), RewardEventWindow, RewardDispatchWindow);

                    bool noSecondObligation = rewardOwner.EnqueuedCount == enqueuedBefore
                        && rewardOwner.Outbox.Count == countBefore
                        && second.RewardsEnqueued == 0;

                    // The same committed event and destination, committed again: the outbox must answer `Duplicate`
                    // with the obligation it already tracks (P-050).
                    OutboxAdmission duplicate = rewardOwner.Adapter.TryCommit(
                        rewardKey,
                        CardTableConstants.CommandSchema,
                        rewardPayload,
                        rewardEvent,
                        rewardEventStep,
                        rewardEventEpoch,
                        rewardEventCausal,
                        true,
                        out DeliveryObligation? existing,
                        out DiagnosticCode code,
                        out string detail);
                    bool idempotent = duplicate == OutboxAdmission.Duplicate
                        && existing != null
                        && existing.Key.OutboxId.Equals(rewardKey.OutboxId)
                        && existing.Key.IdempotencyKey.Equals(rewardKey.IdempotencyKey)
                        && rewardOwner.Outbox.DuplicateCount == duplicatesBefore + 1
                        && rewardOwner.Outbox.Count == countBefore;

                    bool durable = rewardOwner.IsDurable
                        && rewardOwner.Adapter.Journal != null
                        && rewardOwner.Outbox.Durability == OutboxDurability.Durable
                        && catalog.RequiresDurability;
                    int open = rewardOwner.Outbox.OpenCount;
                    bool transferCommitted = open == 0;
                    bool openAsTheTransferDecides = transferCommitted || open == 1;

                    Add(name,
                        noSecondObligation
                        && idempotent
                        && durable
                        && openAsTheTransferDecides
                        && countBefore == 1
                        && enqueuedBefore == 1
                        && rewardOwner.Outbox.CommitCount == 1,
                        "oneRewardPerEvent=True"
                        + "; enqueuedBefore=" + enqueuedBefore.ToString(CultureInfo.InvariantCulture)
                        + "; enqueuedAfter=" + rewardOwner.EnqueuedCount.ToString(CultureInfo.InvariantCulture)
                        + "; rewardsEnqueuedInSecondPass="
                        + second.RewardsEnqueued.ToString(CultureInfo.InvariantCulture)
                        + "; duplicateAdmission=" + duplicate
                        + "; duplicateCode=" + code
                        + "; duplicateCount=" + rewardOwner.Outbox.DuplicateCount.ToString(CultureInfo.InvariantCulture)
                        + "; outboxCount=" + countBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + rewardOwner.Outbox.Count.ToString(CultureInfo.InvariantCulture)
                        + "; commitCount=" + rewardOwner.Outbox.CommitCount.ToString(CultureInfo.InvariantCulture)
                        + "; openAfter=" + open.ToString(CultureInfo.InvariantCulture)
                        + "; transferCommitted=" + transferCommitted
                        + "; durable=" + durable
                        + "; durability=" + rewardOwner.Outbox.Durability
                        + "; journalFrames=" + (rewardOwner.Adapter.Journal == null
                            ? -1
                            : rewardOwner.Adapter.Journal.FrameCount)
                        + "; dispatchAttempts="
                        + rewardOwner.DispatchAttemptCount.ToString(CultureInfo.InvariantCulture)
                        + "; acknowledged=" + rewardOwner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)
                        + "; secondPassDispatched=" + second.Dispatched.ToString(CultureInfo.InvariantCulture)
                        + "; detail=" + Clip(detail, 120)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ teardown

            /// <summary>
            /// Every world this run created is settled and disposed and the registry returns to its pre-run baseline,
            /// and the family's runtime module is released before its world — so no family counter can be incremented
            /// after the storage it reads is gone (P-035, P-048).
            ///
            /// Teardown appends an observation only when something failed. A leak is then a red run rather than a
            /// silent one, and the recorded sequence of a healthy run stays exactly the published table.
            /// </summary>
            private void TearDownSafely()
            {
                try
                {
                    if (rewardBridge != null)
                    {
                        rewardBridge.Dispose();
                        rewardBridge = null;
                        rewardOwner = null;
                    }
                    else if (rewardOwner != null)
                    {
                        rewardOwner.Dispose();
                        rewardOwner = null;
                    }

                    if (runtimeWorld != null)
                    {
                        family.DetachRuntime(runtimeWorld);
                        runtimeWorld = null;
                    }

                    bool sourceStopped = true;
                    Outcome sourceOutcome = Outcome.NoChange;
                    int outstanding = -1;
                    int retained = -1;
                    if (host != null)
                    {
                        if (time != null)
                        {
                            time.Clear(out int _, out int _);
                        }

                        OperationResult stopped = host.Stop(
                            NextOperation(sourceWorld), "gc-021 durable-delivery qualification teardown");
                        sourceOutcome = stopped.Outcome;
                        sourceStopped = stopped.Outcome == Outcome.Published || stopped.Outcome == Outcome.NoChange;
                        UnityWorldHost disposable = host;
                        disposable.Dispose();
                        outstanding = disposable.Ledger.OutstandingJobCount;
                        retained = disposable.Ledger.RetainedResourceCount;
                        host = null;
                    }

                    if (successorHost != null)
                    {
                        successorHost.Stop(
                            NextOperation(successorSession), "gc-021 successor-world teardown");
                        successorHost.Dispose();
                        successorHost = null;
                    }

                    if (unloadHost != null)
                    {
                        unloadHost.Dispose();
                        unloadHost = null;
                    }

                    int registryAfter = UnityWorldRegistry.Count;
                    bool clean = sourceStopped
                        && outstanding == 0
                        && retained == 0
                        && registryAfter == registryBaseline;

                    if (!clean)
                    {
                        Add("gc021-teardown-disposes-every-world", false,
                            "sourceStop=" + sourceOutcome
                            + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                            + "; retainedResources=" + retained.ToString(CultureInfo.InvariantCulture)
                            + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                            + "; registryBaseline=" + registryBaseline.ToString(CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception exception)
                {
                    Add("gc021-teardown-disposes-every-world", false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ helpers

            /// <summary>
            /// Reports the shared precondition of every step: the world and its module chain must exist. A step whose
            /// precondition is missing records the setup failure verbatim, so a broken world is one actionable detail
            /// on every observation instead of eleven separate mysteries.
            /// </summary>
            private bool Ready(string name)
            {
                if (setupFailure.Length != 0)
                {
                    Add(name, false, setupFailure);
                    return false;
                }

                return true;
            }

            /// <summary>One commit through the adapter, with the source event, step, epoch and cause the caller names.</summary>
            private OutboxAdmission Commit(
                DurableDeliveryAdapter adapter,
                DeliveryKey key,
                EventSequence sourceEvent,
                WorldId causalWorld,
                bool requiresDurability,
                out DeliveryObligation? obligation,
                out DiagnosticCode code,
                out string detail)
                => adapter.TryCommit(
                    key,
                    SeamCommandSchema,
                    SeamPayload(sourceEvent),
                    sourceEvent,
                    LogicalStepId.First,
                    AssemblyEpoch.First,
                    new OperationId(causalWorld, family.Issuer, sourceEvent.Value),
                    requiresDurability,
                    out obligation,
                    out code,
                    out detail);

            /// <summary>Bytes of one seam payload: fixed width, so a length is never an accident (P-054).</summary>
            private const int SeamPayloadBytes = 8;

            private static byte[] SeamPayload(EventSequence sourceEvent)
            {
                var payload = new byte[SeamPayloadBytes];
                ulong value = sourceEvent.Value;
                for (int i = 0; i < 8; i++)
                {
                    payload[i] = (byte)(value >> (56 - (i * 8)));
                }

                return payload;
            }

            /// <summary>
            /// The committed schema this genre's world publishes the reward's source event under: the narrative
            /// family's own choice-committed schema, or the card family's own result schema. Both are the packages'
            /// declared schemas, never a schema this scenario invented (P-054).
            /// </summary>
            private SchemaRef GenreSchema() => genre == Gc021Genre.Narrative
                ? NarrativeKeys.ChoiceCommittedSchema
                : CardTableKeys.ResultSchema;

            /// <summary>
            /// Polls the world's committed events into the reward owner once. The narrative family's source is the
            /// bridge itself (it claims the choice schema); a card world's source is the declared one-event source
            /// this run built. Returns the obligations the poll committed.
            /// </summary>
            private int PollTheRewardSource()
            {
                if (rewardOwner == null)
                {
                    return 0;
                }

                IDeliveryObligationSource source = genre == Gc021Genre.Narrative
                    ? (IDeliveryObligationSource)rewardBridge!
                    : rewardSource!;
                return rewardOwner.PollCommittedEvents(source, default(OperationId), RewardEventWindow);
            }

            /// <summary>
            /// One world created through the family's own creation request and generated registration, with its own
            /// reserved session. It is how a session other than the scenario's own world exists at all: an outbox is
            /// not ECS storage, so this world carries no module chain — what it carries is an identity, which is
            /// exactly what "a fresh session, created the same way" has to be (P-002, P-004).
            /// </summary>
            private bool TryCreateBareWorld(WorldId session, out UnityWorldHost? created, out string detail)
            {
                created = null;
                detail = string.Empty;
                if (descriptor == null || descriptor.Adaptation == null)
                {
                    detail = "the family's pipeline descriptor is missing";
                    return false;
                }

                bool ok = UnityWorldRegistry.TryCreate(
                    family.CreateRequest(session, NextOperation(session)),
                    family.CreateRegistration(descriptor.Adaptation!),
                    out UnityWorldHost? createdHost,
                    out WorldCreateResult result);
                created = createdHost;
                if (!ok || createdHost == null)
                {
                    detail = result.Code + ": " + result.Detail;
                    return false;
                }

                return true;
            }

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
            /// Applies one composition edit and publishes the world's assembly for that same publication: P-006 has one
            /// publication series, so the two halves are always done together.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = label + ": the world or its pipeline is missing";
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(
                    payload, NextOperation(host.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    lastFailure = label + ": the edit was refused by the lane (" + admission.Kind + "/"
                        + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = label + ": the publication was refused ("
                        + (published.Count > 0 ? published[0].Outcome.ToString() + "/" + published[0].Code : "none")
                        + ")";
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(host.World));
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = label + ": the world refused the assembly: " + report.Describe();
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        NextOperation(host.World), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        lastFailure = label + ": the unchanged assembly was refused: " + unchanged.Detail;
                        return false;
                    }
                }

                return MatchesPublishedAssembly();
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

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private static byte[] CopyPayload(FrozenPayload payload)
            {
                var copy = new byte[payload.Bytes.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = payload.Bytes[i];
                }

                return copy;
            }

            private static bool PayloadEquals(byte[] left, byte[] right)
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

            private static bool OutboxRowsEqual(
                IReadOnlyList<OutboxRecordValue> left,
                IReadOnlyList<OutboxRecordValue> right)
            {
                if (left.Count != right.Count)
                {
                    return false;
                }

                for (int i = 0; i < left.Count; i++)
                {
                    OutboxRecordValue a = left[i];
                    OutboxRecordValue b = right[i];
                    if (a.Row != b.Row
                        || !a.OutboxId.Equals(b.OutboxId)
                        || !a.DestinationId.Equals(b.DestinationId)
                        || !a.IdempotencyKey.Equals(b.IdempotencyKey)
                        || !a.SourceEvent.Equals(b.SourceEvent)
                        || !a.Step.Equals(b.Step)
                        || !a.Epoch.Equals(b.Epoch)
                        || a.State != b.State
                        || a.Attempts != b.Attempts
                        || !a.PayloadSchema.Equals(b.PayloadSchema)
                        || a.Order != b.Order
                        || a.DurabilityClass != b.DurabilityClass)
                    {
                        return false;
                    }

                    byte[] leftPayload = a.Payload ?? Array.Empty<byte>();
                    byte[] rightPayload = b.Payload ?? Array.Empty<byte>();
                    if (!PayloadEquals(leftPayload, rightPayload))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static int CountForbidden(Type type)
            {
                int matches = 0;
                MemberInfo[] members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    string memberName = members[i].Name;
                    for (int f = 0; f < ForbiddenMemberFragments.Length; f++)
                    {
                        if (memberName.IndexOf(ForbiddenMemberFragments[f], StringComparison.Ordinal) >= 0)
                        {
                            matches++;
                            break;
                        }
                    }
                }

                return matches;
            }

            private static bool HasMember(Type type, string memberName)
            {
                MemberInfo[] members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (string.Equals(members[i].Name, memberName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// The declared *verbs* of one type: the members that are not `object`'s own four. A surface assertion is
            /// only meaningful if the surface it lists is the declared one, so the four inherited members are named
            /// rather than counted as part of the seam.
            /// </summary>
            private static string MemberNames(Type type)
            {
                var names = new List<string>();
                MemberInfo[] members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    string memberName = members[i].Name;
                    if (memberName == "GetType" || memberName == "ToString" || memberName == "Equals"
                        || memberName == "GetHashCode")
                    {
                        continue;
                    }

                    if (names.Contains(memberName))
                    {
                        continue;
                    }

                    names.Add(memberName);
                }

                names.Sort(StringComparer.Ordinal);
                return "[" + string.Join(",", names.ToArray()) + "]";
            }

            private static string Join(IReadOnlyList<string> values)
            {
                var text = new System.Text.StringBuilder();
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

            private static string Clip(string text, int limit)
            {
                if (string.IsNullOrEmpty(text) || text.Length <= limit)
                {
                    return text ?? string.Empty;
                }

                return text.Substring(0, limit) + "...";
            }

            /// <summary>
            /// Records one observation. The family qualification is applied here, at the single recording point, so
            /// every step method passes the bare name from <see cref="ObservationNames"/> and the recorded sequence is
            /// exactly the qualified list: a step that qualified its own name (or forgot to) would change the digest,
            /// which is what the EditMode suite and the player probe assert on.
            /// </summary>
            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new Gc021Step(family.Label + "/" + bareName, passed, detail ?? string.Empty));
            }

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
