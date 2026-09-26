// GameCore.Validation.ProbeHost — the Wave 6 integration-gate runtime scenario.
//
// The exit gate this file implements, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 6):
//
//   "Integrate the fixed-step action reference, durable reward delivery, unload stress and replay/cost counters.
//    Demonstrate that optional physics/animation are absent from cards/narrative. Complete 1,000-cycle teardown and
//    repeatability fixtures before broader qualification."
//
// One runner, three family adapters (`W6GateFamily.cs`), and this file owns the order and the observations. Every
// world it builds is a REAL world of the genre — the genre's own creation request, compiled schedule, control lane,
// manifest source, recipes, live targets and stage runtime — so nothing here is a model of the kernel (P-002). The
// runner owns no protocol behaviour: each observation asserts a module's own report, and a check that raises is
// recorded as a failing step carrying the exception text rather than swallowed into a passing one (P-060).
//
// WHAT EACH GROUP PROVES.
//
//   * Traversal (GC-020 + GC-023). The course runs at its configured fixed duration and commits no step while no host
//     time elapses; the world's own owners are sampled through GC-023's fixed telemetry schema and their counters
//     record the admitted steps; the physics gate steps its dedicated local `PhysicsScene` exactly once per admitted
//     step and refuses a repeated step; and a replay of the RECORDED input into a second world reproduces the rule
//     digest exactly at tolerance zero, while a perturbed engine observation mismatches at zero and matches at the
//     package's declared tolerance — so rule repeatability is never conflated with native physics (07 s4, P-008,
//     P-036, P-038, TEST-011, TEST-022).
//   * Durable reward delivery (GC-021) across a GC-022 unload/reload cycle of the RECEIVING world. One committed
//     event of a real card market becomes one durable obligation with its own external idempotency key; the receiving
//     world is stopped and disposed exactly as a GC-022 lifecycle cycle stops one; the obligation's rows are
//     reinstated into a world of a NEW session, dispatched, and acknowledged; the acknowledgement is then lost — the
//     at-least-once boundary P-045 states — and the same rows are reinstated once more into a third session, where the
//     destination is asked a second time under the SAME external key and mutates nothing. The mutation count across
//     the whole sequence is exactly one (P-045, P-050, P-053, TEST-016, TEST-017).
//   * The composition audit (P-001, P-059): the card and narrative declarations, their compiled descriptors and their
//     loaded assemblies carry no traversal stage, system, buffer, slot, capability, rule, schema, factory or adapter
//     assembly, while the traversal course really carries the optional physics, animation and audio stages.
//   * The 1,000-cycle create/mount/step/unmount/teardown loop, run for all three genres through ONE loop, with the
//     ledger and fence registries bounded, their high-water marks reported and the world's counters back at the
//     baselines the world itself reported (P-046, P-047, P-048, TEST-015, TEST-023).
//   * Repeatability: the canonical digest over the whole observation table, which the EditMode suite and the player
//     probe both recompute from the frozen name table, so a renamed, reordered, added or dropped observation changes
//     the literal instead of shrinking the gate (P-008, TEST-022).
//
// THE DELIVERY SEAT. The receiving world is a real card market whose command port commits a real table result; the
// destination the gate dispatches to is the delivery seam's *recording* destination, the same shape GC-021's own seat
// uses, because this gate owns the unload/reload half rather than the card family's `Transfer` rules (which GC-021's
// `-probeGc021` exercises). The destination's mutation table is the destination's own durable fact: it is deliberately
// owned by the gate rather than by a world, which is what makes "asked twice, mutated once" observable across three
// world incarnations.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution.Delivery;
using GameCore.Execution.Time;
using GameCore.Execution;
using GameCore.Gameplay.Traversal;
using GameCore.Planning;
using GameCore.Replay;
using GameCore.Rules.Narrative;
using GameCore.Rules.Traversal;
using GameCore.Unity.Adapters.Animation;
using GameCore.Unity.Adapters.Audio;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Physics;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Runs the Wave 6 gate over one family and one catalog.</summary>
    public static class W6GateScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling gates use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// Host-clock ticks one cycle of the loop advances in a command-driven world: the same million-tick frame the
        /// GC-019 and GC-018 gates deliver, which commits exactly one logical step per admitted command (P-036).
        /// </summary>
        public const ulong CommandDrivenPumpTicks = 1000000UL;

        /// <summary>Environment variable that overrides the counted cycles (a positive integer).</summary>
        public const string CycleCountVariable = "GC_W6_GATE_CYCLES";

        /// <summary>Cycles one run executes unless <see cref="CycleCountVariable"/> overrides them.</summary>
        public const int DefaultCycleCount = 1000;

        /// <summary>Cycles the fixture-catalog run executes: it proves the second catalog, it does not repeat 1,000.</summary>
        public const int FixtureCycleCount = 3;

        // ------------------------------------------------------------------ shared world-build budget

        /// <summary>Queue capacity of a gate lane: the protocol default every production lane carries (P-050).</summary>
        private const int LaneQueueCapacity = 256;

        /// <summary>
        /// Settled results a gate lane retains: a bound well under the queue capacity, so the lane's own history frees
        /// admission as the loop churns instead of refusing the run's own later submissions (P-050). This is the same
        /// reconciliation GC-022's stress made, for the same reason.
        /// </summary>
        private const int LaneRetainedResults = 64;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        private const int TargetCapacity = 32;

        /// <summary>Low half of the input-source identity: this gate's own headless sampling source (P-008).</summary>
        private const ulong InputSourceLow = 0x573647415445494EUL;

        /// <summary>Admitted steps the traversal observations drive before they read the counters (P-036).</summary>
        public const int TraversalSteps = 5;

        /// <summary>The traversal course's observation names, in execution order, without the family qualification.</summary>
        public static readonly string[] TraversalObservationNames =
        {
            "w6-fixed-step-course-runs",
            "w6-telemetry-counters-record-admitted-steps",
            "w6-one-physics-simulation-per-admitted-step",
            "w6-replay-reproduces-the-rule-digest",
            "w6-carries-the-optional-engine-surface",
            "w6-thousand-cycle-teardown-is-bounded",
            "w6-ledger-and-fence-high-water-marks-are-bounded",
            "w6-counters-return-to-baseline",
            "w6-repeatable-digest",
        };

        /// <summary>The narrative slice's observation names, in execution order.</summary>
        public static readonly string[] NarrativeObservationNames =
        {
            "w6-declares-no-action-physics-or-audio-surface",
            "w6-adapter-assembly-is-absent",
            "w6-thousand-cycle-teardown-is-bounded",
            "w6-ledger-and-fence-high-water-marks-are-bounded",
            "w6-counters-return-to-baseline",
            "w6-repeatable-digest",
        };

        /// <summary>The card market's observation names, in execution order.</summary>
        public static readonly string[] CardsObservationNames =
        {
            "w6-declares-no-action-physics-or-audio-surface",
            "w6-adapter-assembly-is-absent",
            "w6-reward-delivery-is-exactly-once-across-a-reload",
            "w6-thousand-cycle-teardown-is-bounded",
            "w6-ledger-and-fence-high-water-marks-are-bounded",
            "w6-counters-return-to-baseline",
            "w6-repeatable-digest",
        };

        private static readonly List<string> FamilyLabels =
            new List<string> { Gc013NarrativeHost.Label, Gc013CardsHost.Label, Gc020TraversalHost.Label };

        /// <summary>Cycles one counted run executes, resolved once from <see cref="CycleCountVariable"/>.</summary>
        public static int CycleCount { get; } = ResolveCycleCount();

        /// <summary>The family labels this scenario accepts, in the order the contract fixes them.</summary>
        public static IReadOnlyList<string> Families() => FamilyLabels;

        /// <summary>The observation names one family's run records, in execution order.</summary>
        public static string[] ObservationNames(string label)
        {
            switch (RequireFamily(label))
            {
                case Gc020TraversalHost.Label:
                    return (string[])TraversalObservationNames.Clone();
                case Gc013NarrativeHost.Label:
                    return (string[])NarrativeObservationNames.Clone();
                default:
                    return (string[])CardsObservationNames.Clone();
            }
        }

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            string[] bare = ObservationNames(label);
            var names = new string[bare.Length];
            for (int i = 0; i < bare.Length; i++)
            {
                names[i] = label + "/" + bare[i];
            }

            return names;
        }

        /// <summary>Runs the gate over one family, over the generated/declared or the fixture catalog's world.</summary>
        public static W6GateScenarioResult Run(IW6Family family, bool fixtureRun)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family, fixtureRun, fixtureRun ? FixtureCycleCount : CycleCount).Run();
        }

        /// <summary>Identity salt of the reload route's session sequence, so the route is reproducible (P-008).</summary>
        private const ulong ReloadRouteSalt = 0x57364752454C4F41UL;

        /// <summary>
        /// The third genre's reload route, driven once per Play Mode matrix cycle: one real world of the genre created,
        /// one typed command admitted and committed as exactly one logical step, and the world stopped and disposed.
        /// The route builds the SAME world the gate's own observations build — the family's compiled schedule, lane,
        /// publisher, pipeline and stage runtime — so a {domain, scene} reload setting that broke a genre's world
        /// breaks this route too. It returns a detail string beginning with <c>pass</c> or <c>fail</c>; it never
        /// throws on a refusal, and the caller decides what a failure means (P-035, P-046, TEST-018).
        /// </summary>
        public static string RunReloadRoute(IW6Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            var sessions = new IdSequence(ReloadRouteSalt);
            Fixture? fixture = null;
            try
            {
                fixture = Fixture.Build(
                    family, sessions, TargetCapacity,
                    withRuntime: true, withProviders: true, pumpClockOrigin: true);
                if (!fixture.Ready)
                {
                    return "fail; world=" + fixture.Failure;
                }

                string session = fixture.Host.World.Session.ToString();
                int registryBefore = UnityWorldRegistry.Count;
                int committed = fixture.RunRecordedSteps(1, null);

                // The course's declared physics authority simulates exactly once per admitted step: the route
                // discharges that caller obligation itself (04 s7), so a reload setting that broke the engine stage
                // shows up here and not only in the gate's own observations.
                if (fixture.PhysicsGate != null)
                {
                    fixture.PhysicsGate.TrySimulateExactlyOnce(
                        fixture.Host.CurrentStep.Value, 0.02d, out string _);
                }

                UnityPhysicsSceneBackend? physics = fixture.Physics;
                int simulations = physics != null ? physics.SimulateCount : -1;
                int steppedSteps = fixture.Module != null ? fixture.Module.SteppedStepCount : -1;
                int published = fixture.Lane != null ? fixture.Lane.PublicationCount : -1;
                bool consistent = fixture.MatchesPublishedAssembly();

                Outcome stop = fixture.StopAndDispose();
                fixture = null;
                int registryAfter = UnityWorldRegistry.Count;
                bool stopped = stop == Outcome.Published || stop == Outcome.NoChange;
                bool physicsHeld = physics == null ? simulations == -1 : simulations == 1;
                bool pass = committed == 1
                    && physicsHeld
                    && steppedSteps == 1
                    && consistent
                    && stopped
                    && registryAfter == registryBefore - 1;

                return (pass ? "pass" : "fail")
                    + "; genre=" + family.Label
                    + "; session=" + session
                    + "; stepsCommitted=" + committed.ToString(CultureInfo.InvariantCulture)
                    + "; moduleSteppedSteps=" + steppedSteps.ToString(CultureInfo.InvariantCulture)
                    + "; engineSimulations=" + simulations.ToString(CultureInfo.InvariantCulture)
                    + "; lanePublications=" + published.ToString(CultureInfo.InvariantCulture)
                    + "; stop=" + stop
                    + "; registryBefore=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                    + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                return "fail; unhandled " + exception.GetType().FullName + ": " + exception.Message;
            }
            finally
            {
                fixture?.Dispose();
            }
        }

        private static int ResolveCycleCount()
        {
            string? text = Environment.GetEnvironmentVariable(CycleCountVariable);
            if (text != null
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed > 0)
            {
                return parsed;
            }

            return DefaultCycleCount;
        }

        private static string RequireFamily(string label)
        {
            for (int i = 0; i < FamilyLabels.Count; i++)
            {
                if (string.Equals(label, FamilyLabels[i], StringComparison.Ordinal))
                {
                    return FamilyLabels[i];
                }
            }

            throw new ArgumentException(
                "Unknown Wave 6 gate family label '" + (label ?? "<null>") + "'; the accepted labels are "
                + string.Join(", ", FamilyLabels.ToArray()) + ".", nameof(label));
        }

        /// <summary>
        /// One recorded input sample of the traversal observations. The replay observation feeds these back into a
        /// second world, so the replay is of the input that was really admitted rather than of a remembered one
        /// (P-008, TEST-022).
        /// </summary>
        private readonly struct RecordedInput
        {
            public RecordedInput(int horizontalMilli, byte jumpPressed)
            {
                HorizontalMilli = horizontalMilli;
                JumpPressed = jumpPressed;
            }

            public int HorizontalMilli { get; }

            public byte JumpPressed { get; }
        }

        /// <summary>
        /// The delivery seam's recording destination: it mutates once per external idempotency key and answers
        /// `AlreadyApplied` for every later attempt at the same key, so "asked twice, mutated once" is a property of
        /// the run rather than of the adapter alone (P-045). Its mutation table is deliberately owned by the gate and
        /// not by a world, which is what lets the claim span three world incarnations and two reinstatements.
        /// </summary>
        private sealed class W6RewardDestination : IDestinationPort
        {
            private readonly HashSet<Id128> applied = new HashSet<Id128>();
            private readonly SchemaRef commandSchema;

            public W6RewardDestination(Id128 destinationId, SchemaRef commandSchema)
            {
                DestinationId = destinationId;
                this.commandSchema = commandSchema;
            }

            public Id128 DestinationId { get; }

            public SchemaRef CommandSchema => commandSchema;

            /// <summary>Every attempt this port was handed, in order.</summary>
            public List<DeliveryAttempt> Attempts { get; } = new List<DeliveryAttempt>();

            /// <summary>Attempts that really mutated something.</summary>
            public int MutationCount { get; private set; }

            /// <summary>Attempts answered from the idempotency key, so no second mutation happened.</summary>
            public int AlreadyAppliedCount { get; private set; }

            public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
            {
                Attempts.Add(attempt);
                code = DiagnosticCode.None;
                if (!applied.Add(attempt.IdempotencyKey))
                {
                    AlreadyAppliedCount++;
                    detail = "the destination already applied attempt #"
                        + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture)
                        + ", so this redelivery mutated nothing (P-045)";
                    return DestinationOutcome.AlreadyApplied;
                }

                MutationCount++;
                detail = "the destination applied attempt #"
                    + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " under external key " + attempt.IdempotencyKey.ToString() + " (P-045)";
                return DestinationOutcome.Applied;
            }

            public override string ToString() => "w6RewardDestination(attempts="
                + Attempts.Count.ToString(CultureInfo.InvariantCulture)
                + ",mutations=" + MutationCount.ToString(CultureInfo.InvariantCulture)
                + ",alreadyApplied=" + AlreadyAppliedCount.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// The obligation source of the reward observation: it claims exactly the FIRST committed event of the
        /// receiving world and describes the delivery that event carries, then refuses every later event by count.
        /// Which committed event a recipient turns into a reward is the recipient's own content decision (P-045), and
        /// the owner only ever hands it committed events, so this source can never see staged work.
        /// </summary>
        private sealed class W6FirstCommittedEventSource : IDeliveryObligationSource
        {
            private readonly Id128 destinationId;
            private readonly SchemaRef commandSchema;
            private readonly byte[] payload;

            public W6FirstCommittedEventSource(Id128 destinationId, SchemaRef commandSchema, byte[] payload)
            {
                this.destinationId = destinationId;
                this.commandSchema = commandSchema;
                this.payload = payload ?? Array.Empty<byte>();
            }

            /// <summary>Committed events this source claimed; one at most, so a second claim is impossible.</summary>
            public int ClaimedCount { get; private set; }

            /// <summary>Committed events it did not claim, counted so "the event was really seen" is visible (P-052).</summary>
            public int UnclaimedCount { get; private set; }

            /// <summary>The schema of the committed event this source turned into a delivery.</summary>
            public SchemaRef ClaimedSchema { get; private set; }

            /// <summary>The committed sequence it turned into a delivery.</summary>
            public EventSequence ClaimedSequence { get; private set; }

            public bool TryDescribe(in CommittedEvent committed, out DeliveryObligationRequest request)
            {
                request = default(DeliveryObligationRequest);
                if (ClaimedCount > 0)
                {
                    UnclaimedCount++;
                    return false;
                }

                ClaimedCount++;
                ClaimedSchema = committed.Schema;
                ClaimedSequence = committed.Cursor.Sequence;
                request = new DeliveryObligationRequest(destinationId, commandSchema, payload, true);
                return true;
            }

            public override string ToString() => "w6FirstCommittedEventSource(claimed="
                + ClaimedCount.ToString(CultureInfo.InvariantCulture)
                + ",unclaimed=" + UnclaimedCount.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// One self-contained world of one genre, built through the same sequence every earlier gate uses: the genre's
        /// compiled ownership/schedule, its live targets, the control lane over a manifest source that resolves the
        /// genre's catalog plus this gate's cycle declarations, the composition bridge, the genre's own seeding and
        /// stage runtime, the derived-assembly pipeline and the genre's own time driver. It is deliberately not a
        /// second implementation of a genre: it uses the family's own declarations, recipes, lane seed, request,
        /// registration, provider mounts and payloads (P-001, P-002).
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            private readonly IW6Family family;
            private readonly int targetCapacity;

            private UnityWorldHost? host;
            private ulong hostTicks;
            private ulong operationSequence;
            private ulong sampleSequence;

            private Fixture(IW6Family family, int targetCapacity)
            {
                this.family = family;
                this.targetCapacity = targetCapacity;
            }

            public bool Ready { get; private set; }

            public string Failure { get; private set; } = string.Empty;

            public CompositionHost? Lane { get; private set; }

            public AssemblyPublisher? Publisher { get; private set; }

            public DerivedAssemblyPipeline? Pipeline { get; private set; }

            public WorldTimeDriver? Time { get; private set; }

            public TargetRegistry? Registry { get; private set; }

            public LiveTargetIndex? Targets { get; private set; }

            public LiveTargetSeeder? Seeder { get; private set; }

            public W6StageRuntime? Runtime { get; private set; }

            public PipelineDescriptorReport? Descriptor { get; private set; }

            public TypedInputIngress? Ingress { get; private set; }

            public UnityWorldHost Host => host!;

            public TraversalModule? Module => Runtime != null && Runtime.Traversal != null ? Runtime.Traversal.Module : null;

            public PhysicsAuthorityGate? PhysicsGate =>
                Runtime != null && Runtime.Traversal != null ? Runtime.Traversal.PhysicsGate : null;

            public UnityPhysicsSceneBackend? Physics =>
                Runtime != null && Runtime.Traversal != null ? Runtime.Traversal.Physics : null;

            public Gc020StageRuntime? TraversalRuntime => Runtime != null ? Runtime.Traversal : null;

            public TraversalTraceRecorder? Trace =>
                Runtime != null && Runtime.Traversal != null ? Runtime.Traversal.Trace : null;

            public static Fixture Build(
                IW6Family family,
                IdSequence sessions,
                int targetCapacity,
                bool withRuntime,
                bool withProviders,
                bool pumpClockOrigin)
            {
                var fixture = new Fixture(family, targetCapacity);
                try
                {
                    fixture.Create(sessions, withRuntime, withProviders, pumpClockOrigin);
                }
                catch (Exception exception)
                {
                    fixture.Failure = exception.GetType().Name + ": " + exception.Message;
                    fixture.Dispose();
                }

                return fixture;
            }

            /// <summary>One cycle's step: the genre's own command through the world's own ingress, then one pump.</summary>
            public ulong SubmitAndPumpCycle(ulong ordinal)
            {
                if (host == null || Time == null || Ingress == null)
                {
                    return 0UL;
                }

                var stamp = new InputSourceStamp(
                    host.World,
                    new Id128(family.SessionSalt, InputSourceLow),
                    ++sampleSequence,
                    host.CurrentStep,
                    host.CurrentEpoch);
                var sample = new SampledInputCommand(
                    stamp,
                    family.CycleRoute,
                    family.CycleTarget,
                    family.CycleSchema,
                    null,
                    family.CyclePayload(ordinal));
                InputAdmissionResult admission = Ingress.Submit(sample);
                if (!admission.Accepted)
                {
                    return 0UL;
                }

                hostTicks += family.CyclePumpTicks;
                return Time.PumpFrame(hostTicks).StepsCommitted;
            }

            /// <summary>
            /// Drives <paramref name="count"/> admitted steps, each with one captured movement sample the course's
            /// input stage consumes, and records the input when a list is supplied. The total committed logical steps
            /// is returned, so a caller compares the world's own answer rather than a formula (P-036).
            /// </summary>
            public int RunRecordedSteps(int count, List<RecordedInput>? record)
            {
                int committed = 0;
                for (int i = 0; i < count; i++)
                {
                    if (record != null)
                    {
                        record.Add(new RecordedInput(0, 0));
                    }

                    committed += (int)SubmitAndPumpCycle((ulong)i);
                }

                return committed;
            }

            /// <summary>Pumps the same host tick repeatedly: a fixed-step world with no elapsed time commits nothing.</summary>
            public ulong PumpIdleFrames(int frames)
            {
                if (host == null || Time == null)
                {
                    return 0UL;
                }

                ulong committed = 0UL;
                for (int i = 0; i < frames; i++)
                {
                    committed += Time.PumpFrame(hostTicks).StepsCommitted;
                }

                return committed;
            }

            public TraversalVector3i PoseOf(TargetId target)
            {
                if (host == null || Seeder == null || !Seeder.TryGetEntity(target, out Entity entity))
                {
                    return TraversalVector3i.Zero;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                return entityManager.Exists(entity) && entityManager.HasComponent<TraversalPose>(entity)
                    ? entityManager.GetComponentData<TraversalPose>(entity).Vector
                    : TraversalVector3i.Zero;
            }

            public TraversalVector3i VelocityOf(TargetId target)
            {
                if (host == null || Seeder == null || !Seeder.TryGetEntity(target, out Entity entity))
                {
                    return TraversalVector3i.Zero;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                return entityManager.Exists(entity) && entityManager.HasComponent<TraversalVelocity>(entity)
                    ? entityManager.GetComponentData<TraversalVelocity>(entity).Vector
                    : TraversalVector3i.Zero;
            }

            public bool MatchesPublishedAssembly()
            {
                if (Lane == null || Publisher == null || host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    Lane.Committed.Revision,
                    Lane.Committed.Epoch,
                    Publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            /// <summary>Stops and disposes this world, returning the world's own stop outcome (P-035).</summary>
            public Outcome StopAndDispose()
            {
                Outcome outcome = Outcome.NoChange;
                Runtime?.Dispose();
                Runtime = null;
                if (host != null)
                {
                    OperationResult stop = host.Stop(NextOperation(), "w6 gate world teardown");
                    outcome = stop.Outcome;
                    host.Dispose();
                    host = null;
                }

                return outcome;
            }

            public void Dispose()
            {
                if (host == null && Runtime == null)
                {
                    return;
                }

                StopAndDispose();
            }

            /// <summary>Applies one composition edit and publishes the world's assembly for that same publication.</summary>
            public bool PublishEdit(CompositionEditPayload payload, out string detail)
            {
                detail = string.Empty;
                if (host == null || Lane == null || Pipeline == null || Publisher == null)
                {
                    detail = "the world or its pipeline is missing";
                    return false;
                }

                OperationId operation = NextOperation();
                EditAdmission admission = Lane.SubmitEdit(payload, operation, Lane.Committed.Revision);
                if (!admission.Staged)
                {
                    detail = "the edit was refused by the lane (" + admission.Kind + "/"
                        + DiagnosticCodeText.Of(admission.Code) + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = Lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    detail = "the publication was refused";
                    return false;
                }

                DerivedAssemblyReport report = Pipeline.PublishDerived(operation);
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    detail = "the world refused the assembly: " + report.Describe();
                    return false;
                }

                if (!MatchesPublishedAssembly())
                {
                    detail = "the world's lane and assembly counters disagree (P-006)";
                    return false;
                }

                return true;
            }

            public OperationId NextOperation()
            {
                operationSequence++;
                return new OperationId(Host.World, family.Issuer, operationSequence);
            }

            private void Create(IdSequence sessions, bool withRuntime, bool withProviders, bool pumpClockOrigin)
            {
                Descriptor = family.CompilePipeline();
                if (!Descriptor.Succeeded
                    || Descriptor.Descriptor == null
                    || Descriptor.Adaptation == null
                    || Descriptor.Compilation == null)
                {
                    Failure = "the ownership and schedule pipeline refused: " + Descriptor.Describe();
                    return;
                }

                WorldId world = new WorldId(sessions.Next());
                WorldCreateRequest request = family.CreateRequest(world, NextOperationFor(world));
                UnityWorldRegistration registration = family.CreateRegistration(Descriptor.Adaptation);
                bool created = UnityWorldRegistry.TryCreate(
                    request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                host = createdHost;
                if (!created || host == null)
                {
                    Failure = "world creation failed: " + result.Code + ": " + result.Detail;
                    return;
                }

                Registry = new TargetRegistry(world, targetCapacity);
                Publisher = new AssemblyPublisher(
                    host, Registry, family.CreateRecipes(), family.CreateMigrations(), Descriptor.Descriptor);
                Targets = new LiveTargetIndex(Publisher.Recipes);
                Seeder = new LiveTargetSeeder(host, Registry, Targets);

                IDerivationValueSource valueSource = family.CreateValues();
                var manifestSource = new LifecycleStressManifestSource(
                    new CatalogManifestSource(family.Catalog, family.Declarations));
                IReadOnlyList<PluginManifest> cycleManifests = family.StressDeclarations.All;
                for (int i = 0; i < cycleManifests.Count; i++)
                {
                    manifestSource.Add(cycleManifests[i]);
                }

                Lane = new CompositionHost(
                    world,
                    family.WorldRootScope,
                    new CompositionHostSettings(
                        new ControlLaneCapacitySettings(LaneQueueCapacity, LaneRetainedResults),
                        OperationExpirySettings.Default),
                    manifestSource,
                    new LifecycleStressResourceFactory(),
                    PropagationMode.Automatic,
                    family.LaneSeed);
                _ = new WorldCompositionBridge(host, Lane, Publisher);

                if (!family.SeedTargets(new Gc013WorldContext(host, Targets, Seeder)))
                {
                    Failure = "the family refused to seed its declared targets";
                    return;
                }

                if (withRuntime)
                {
                    Runtime = family.AttachGateRuntime(host, Descriptor, Targets, Seeder);
                }

                Pipeline = new DerivedAssemblyPipeline(
                    host,
                    Lane,
                    Publisher,
                    Targets,
                    Seeder,
                    valueSource,
                    null,
                    null,
                    Publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, family.Issuer),
                    new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                Time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                Time.AdoptResourceTable(Descriptor.Adaptation.NativeTable!);

                if (withProviders)
                {
                    if (!PublishEdit(family.MountProvider(), out string providerDetail))
                    {
                        Failure = "the genre's own provider mount was refused: " + providerDetail;
                        return;
                    }

                    if (!PublishEdit(family.MountSecondProvider(), out string secondDetail))
                    {
                        Failure = "the genre's own second provider mount was refused: " + secondDetail;
                        return;
                    }
                }

                if (pumpClockOrigin)
                {
                    // The world's simulation clock starts at its first routable pump (P-036), so this zero-elapsed
                    // pump captures the origin before any host time is delivered.
                    Time.PumpFrame(hostTicks);
                }

                Ingress = new TypedInputIngress(host.World, host);
                Ready = true;
            }

            private OperationId NextOperationFor(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }
        }

        /// <summary>
        /// The gate executor: one world per observation group, the observations in order, and the teardown that
        /// returns the process-wide registries to their baseline even when a step failed before its own teardown ran.
        /// </summary>
        private sealed class Executor
        {
            /// <summary>Identity salt of the no-row carriers that consume an adopted-and-pending pair (P-006, P-024).</summary>
            private static readonly Id128 CarrierSalt = new Id128(0x5736474154454341UL, 1UL);


            /// <summary>Identity of the gate's durable outbox owner (P-004).</summary>
            private static readonly Id128 DeliveryOwnerId = new Id128(0x5736474154454F42UL, 1UL);

            /// <summary>Identity of the delivery destination the reward observation dispatches to (P-004).</summary>
            private static readonly Id128 RewardDestinationId = new Id128(0x5736474154454453UL, 2UL);

            /// <summary>Payload schema the reward destination is asked to apply (P-042).</summary>
            private static readonly SchemaRef RewardCommandSchema =
                new SchemaRef(new SchemaId(new Id128(0x5736474154454453UL, 3UL)), 1U);

            private const int DeliveryCapacity = 8;

            private const int DeliveryTerminalRetention = 4;

            private readonly IW6Family family;
            private readonly bool fixtureRun;
            private readonly int cycles;
            private readonly List<W6GateStep> steps = new List<W6GateStep>();
            private readonly IdSequence sessionSequence = new IdSequence(0x5736474153455353UL);
            private readonly IdSequence leaseSequence = new IdSequence(0x573647414C454153UL);
            private readonly List<PluginInstanceId> cycleInstances = new List<PluginInstanceId>();
            private readonly List<RecordedInput> recordedInput = new List<RecordedInput>();

            private int registryBaseline;
            private ulong operationSequence;
            private int carrierOrdinal;
            private int worldBalanceAtBuild;
            private int worldRetainedAtBuild;
            private int worldOutstandingAtBuild;
            private string teardownFailure = string.Empty;

            private Fixture? traversal;
            private Fixture? loop;
            private LifecycleController? loopController;

            public Executor(IW6Family family, bool fixtureRun, int cycles)
            {
                this.family = family;
                this.fixtureRun = fixtureRun;
                this.cycles = cycles;
            }

            public W6GateScenarioResult Run()
            {
                registryBaseline = UnityWorldRegistry.Count;
                try
                {
                    if (IsTraversal())
                    {
                        FixedStepCourseRuns();
                        TelemetryCountersRecordAdmittedSteps();
                        OneSimulationPerAdmittedStep();
                        ReplayReproducesTheRuleDigest();
                        CarriesTheOptionalEngineSurface();
                    }
                    else
                    {
                        DeclaresNoActionPhysicsOrAudioSurface();
                        AdapterAssemblyIsAbsent();
                        if (IsCards())
                        {
                            RewardDeliveryIsExactlyOnceAcrossAReload();
                        }
                    }

                    ThousandCycleTeardownIsBounded();
                    LedgerAndFenceHighWaterMarksAreBounded();
                    CountersReturnToBaseline();
                    RepeatableDigest();
                }
                finally
                {
                    TearDownSafely();
                    EnsureEveryObservationIsRecorded();
                }

                return new W6GateScenarioResult(family.Label, steps);
            }

            // ================================================================== 1. the fixed-step course

            /// <summary>
            /// Builds one real fixed-step course world and checks the reference's own declared facts: the world is a
            /// fixed-step world at the reference's configured step with the declared catch-up bound, it commits NO
            /// step while no host time elapses, and its live runners and both checkpoint volumes carry the genre's own
            /// storage. The world is kept for the counters, physics, replay and engine-surface observations, so they
            /// all describe the same world this observation qualified (P-036, P-038, TEST-011).
            /// </summary>
            private void FixedStepCourseRuns()
            {
                const string name = "w6-fixed-step-course-runs";
                try
                {
                    traversal = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!traversal.Ready)
                    {
                        Add(name, false, traversal.Failure);
                        return;
                    }

                    FixedStepSettings? fixedStep = traversal.Host.Request.FixedStep;
                    bool declaredStep = traversal.Host.TemporalModel == TemporalModel.FixedStep
                        && fixedStep != null
                        && fixedStep.IsValid
                        && fixedStep.StepDurationTicks == family.StepDurationTicks
                        && fixedStep.MaxStepsPerPump == family.MaxStepsPerPump
                        && fixedStep.TicksPerSecond == TraversalRegistration.TicksPerSecond
                        && family.StepMilliseconds > 0;

                    ulong idleSteps = traversal.PumpIdleFrames(4);

                    TraversalModule? module = traversal.Module;
                    bool runnersLive = module != null
                        && module.RunnerCount > 0
                        && module.VolumeCount == 2
                        && module.CourseEntity != Entity.Null
                        && traversal.Targets != null
                        && traversal.Targets.Contains(family.CycleTarget);

                    bool pass = declaredStep
                        && idleSteps == 0UL
                        && runnersLive
                        && traversal.Lane != null
                        && traversal.Lane.Committed.Mode == PropagationMode.Automatic
                        && traversal.Host.Lifecycle == WorldLifecycleState.Running
                        && traversal.Host.CurrentStep.Equals(LogicalStepId.Zero)
                        && traversal.MatchesPublishedAssembly()
                        && UnityWorldRegistry.Count == registryBaseline + 1;

                    Add(name, pass,
                        "session=" + traversal.Host.World.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; generatedCatalog=" + (Gc020TraversalHost.GeneratedCatalogPresent ? "present" : "absent")
                        + "; temporalModel=" + traversal.Host.TemporalModel
                        + "; stepMillis=" + family.StepMilliseconds.ToString(CultureInfo.InvariantCulture)
                        + "; stepDurationTicks=" + family.StepDurationTicks.ToString(CultureInfo.InvariantCulture)
                        + "; maxStepsPerPump=" + family.MaxStepsPerPump.ToString(CultureInfo.InvariantCulture)
                        + "; declaredStep=" + declaredStep
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; moduleRunners=" + (module != null
                            ? module.RunnerCount.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; moduleVolumes=" + (module != null
                            ? module.VolumeCount.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; declaredStages=" + (traversal.Descriptor != null
                            ? traversal.Descriptor.Stages.Count.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; physicsSceneInstalled=" + (traversal.Physics != null));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Drives the declared number of admitted steps, each with the one movement sample the course's input stage
            /// consumes, then samples the world's own owners through GC-023's fixed telemetry schema: the driver's
            /// committed-step counter, the world's own logical step, the physics authority gate's simulated-step
            /// counter and the engine's own `Simulate` count must all be the same number. That is what "counters record
            /// admitted steps == physics simulations" means for this reference: a counter that disagreed with the
            /// admitted steps would be a wrong counter rather than a detail (P-036, P-038, TEST-023, 08 counters).
            /// The observation also requires the counting to be COMPILED IN, which is exactly what the qualification
            /// build (and not the release clone) is for.
            /// </summary>
            private void TelemetryCountersRecordAdmittedSteps()
            {
                const string name = "w6-telemetry-counters-record-admitted-steps";
                try
                {
                    if (!TraversalReady(name, out string why))
                    {
                        Add(name, false, why);
                        return;
                    }

                    int committed = traversal!.RunRecordedSteps(TraversalSteps, recordedInput);
                    if (committed != TraversalSteps)
                    {
                        Add(name, false, "the course committed " + committed.ToString(CultureInfo.InvariantCulture)
                            + " of " + TraversalSteps.ToString(CultureInfo.InvariantCulture)
                            + " admitted steps, so the counters below would describe a run that did not happen");
                        return;
                    }

                    var collector = new TelemetryCollector(TelemetryRetention.Default);
                    ReplayScenario.RegisterOwners(collector, traversal.Host);
                    TelemetryFrame frame = collector.Sample(
                        0, traversal.Host.CurrentEpoch, traversal.Host.CurrentStep);
                    TelemetryCounterSet counters = frame.Aggregate();

                    long stepsAdvanced = counters.Get(TelemetryCounter.StepsAdvanced);
                    long liveLeases = counters.Get(TelemetryCounter.LiveLeases);
                    long outstandingCallbacks = counters.Get(TelemetryCounter.OutstandingCallbacks);
                    long structuralOperations = counters.Get(TelemetryCounter.StructuralOperations);
                    long appliedSamples = counters.Get(TelemetryCounter.ApplySampleCount);

                    int physicsSimulations = traversal.Physics != null ? traversal.Physics.SimulateCount : -1;
                    int gateSimulations = traversal.PhysicsGate != null ? traversal.PhysicsGate.SteppedStepCount : -1;

                    // The claim is narrow and exact: the counters the world's own owners export record the admitted
                    // steps, and the physics gate's simulated-step counter and the engine's own Simulate count are the
                    // same number. The lease, callback and structural counters are reported beside it, because they
                    // belong to GC-023's own observations rather than to this one.
                    bool pass = TelemetrySchema.IsCompiledIn
                        && collector.CountingCompiledIn
                        && collector.OwnerCount > 0
                        && collector.SampleCount == 1
                        && frame.Sections.Count > 0
                        && stepsAdvanced == TraversalSteps
                        && traversal.Host.CurrentStep.Value == (ulong)TraversalSteps
                        && gateSimulations == TraversalSteps
                        && physicsSimulations == TraversalSteps;

                    Add(name, pass,
                        "telemetryCompiledIn=" + TelemetrySchema.IsCompiledIn
                        + "; collectorCompiledIn=" + collector.CountingCompiledIn
                        + "; owners=" + collector.OwnerCount.ToString(CultureInfo.InvariantCulture)
                        + "; samples=" + collector.SampleCount.ToString(CultureInfo.InvariantCulture)
                        + "; sections=" + frame.Sections.Count.ToString(CultureInfo.InvariantCulture)
                        + "; stepsAdvanced=" + stepsAdvanced.ToString(CultureInfo.InvariantCulture)
                        + "; hostStep=" + traversal.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; gateSimulatedSteps=" + gateSimulations.ToString(CultureInfo.InvariantCulture)
                        + "; engineSimulations=" + physicsSimulations.ToString(CultureInfo.InvariantCulture)
                        + "; liveLeases=" + liveLeases.ToString(CultureInfo.InvariantCulture)
                        + "; outstandingCallbacks=" + outstandingCallbacks.ToString(CultureInfo.InvariantCulture)
                        + "; structuralOperations=" + structuralOperations.ToString(CultureInfo.InvariantCulture)
                        + "; applySamples=" + appliedSamples.ToString(CultureInfo.InvariantCulture)
                        + "; admittedInputSamples=" + recordedInput.Count.ToString(CultureInfo.InvariantCulture)
                        + "; counterSchema=" + TelemetrySchema.Name(TelemetryCounter.StepsAdvanced));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The physics half of the same claim, read from the gate that owns it, on a world of its own so the replay
            /// observation can compare two worlds that each ran exactly the recorded number of steps: one local
            /// `PhysicsScene` simulation per admitted step and no more, an intent applied before the simulation already
            /// reflected by the engine's pose, and a repeated step refused and counted rather than simulated twice
            /// (P-034, 04 s7, REF-A05).
            /// </summary>
            private void OneSimulationPerAdmittedStep()
            {
                const string name = "w6-one-physics-simulation-per-admitted-step";
                Fixture? physicsWorld = null;
                try
                {
                    physicsWorld = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!physicsWorld.Ready)
                    {
                        Add(name, false, "the physics world could not be built: " + physicsWorld.Failure);
                        return;
                    }

                    UnityPhysicsSceneBackend? physics = physicsWorld.Physics;
                    PhysicsAuthorityGate? gate = physicsWorld.PhysicsGate;
                    TraversalModule? module = physicsWorld.Module;
                    if (physics == null || gate == null || module == null)
                    {
                        Add(name, false, "the course world carries no local physics scene or no traversal module (04 s7)");
                        return;
                    }

                    IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                    if (runners.Count == 0)
                    {
                        Add(name, false, "the course owns no runner, so there is no physical domain to step");
                        return;
                    }

                    var declared = new List<string>();
                    for (int i = 0; i < runners.Count; i++)
                    {
                        var key = new PhysicsBodyKey(runners[i].Target, TraversalKeys.MotionDomain.Id.Value);
                        PhysicsPose initial = new PhysicsPose(
                            AsPhysics(physicsWorld.PoseOf(runners[i].Target)),
                            AsPhysics(physicsWorld.VelocityOf(runners[i].Target)));
                        PhysicsBodyOutcome outcome = gate.TryDeclareBody(
                            key, initial, out DiagnosticCode code, out string detail);
                        declared.Add(runners[i].Target.ToString() + "=" + outcome);
                        if (outcome != PhysicsBodyOutcome.Applied)
                        {
                            Add(name, false, "declaring " + runners[i].Target.ToString() + " was refused: "
                                + DiagnosticCodeText.Of(code) + ": " + detail);
                            return;
                        }
                    }

                    int simulationsBefore = physics.SimulateCount;
                    int gateBefore = gate.SteppedStepCount;
                    ulong stepsBefore = physicsWorld.Host.CurrentStep.Value;

                    var bodyKey = new PhysicsBodyKey(runners[0].Target, TraversalKeys.MotionDomain.Id.Value);
                    var teleport = new PhysicsVector3i(7000, 0, 0);
                    PhysicsBodyOutcome applied = gate.TryApplyIntent(
                        bodyKey,
                        AuthorityIntentKind.Teleport,
                        PhysicsIntentCodec.WriteTeleport(teleport),
                        out string intentDetail);
                    bool poseReflectsIntent = gate.TryReadPose(bodyKey, out PhysicsPose pose)
                        && pose.Position == teleport;

                    // One admitted step, then exactly one simulation for it, repeated: the caller obligation 04 s7
                    // states (a plugin that owns an engine authority steps it once per admitted step) is discharged
                    // here, so the counters below compare the world's own answers rather than a formula.
                    int simulated = 0;
                    for (int step = 0; step < TraversalSteps; step++)
                    {
                        if (physicsWorld.RunRecordedSteps(1, null) != 1)
                        {
                            break;
                        }

                        if (gate.TrySimulateExactlyOnce(physicsWorld.Host.CurrentStep.Value, 0.02d, out string _))
                        {
                            simulated++;
                        }
                    }

                    int duplicateBefore = gate.DuplicateStepRefusalCount;
                    bool duplicateRefused = !gate.TrySimulateExactlyOnce(
                        physicsWorld.Host.CurrentStep.Value, 0.02d, out string duplicateDetail);
                    bool duplicateCounted = gate.DuplicateStepRefusalCount == duplicateBefore + 1;

                    int simulationsAfter = physics.SimulateCount;
                    int gateAfter = gate.SteppedStepCount;
                    ulong stepsAfter = physicsWorld.Host.CurrentStep.Value;

                    bool pass = applied == PhysicsBodyOutcome.Applied
                        && poseReflectsIntent
                        && gate.Bodies.Count == runners.Count
                        && simulated == TraversalSteps
                        && simulationsAfter - simulationsBefore == TraversalSteps
                        && gateAfter - gateBefore == TraversalSteps
                        && (int)(stepsAfter - stepsBefore) == TraversalSteps
                        && duplicateRefused
                        && duplicateCounted
                        && physics.IsDedicatedLocalScene
                        && physics.AutomaticSimulationSuppressed;

                    Add(name, pass,
                        "bodies=" + W6CompositionAudit.Join(declared)
                        + "; intent=" + applied + "(" + Clip(intentDetail, 80) + ")"
                        + "; poseReflectsIntent=" + poseReflectsIntent
                        + "; steps=" + stepsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + stepsAfter.ToString(CultureInfo.InvariantCulture)
                        + "; engineSimulations=" + simulationsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + simulationsAfter.ToString(CultureInfo.InvariantCulture)
                        + "; gateSimulatedSteps=" + gateBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + gateAfter.ToString(CultureInfo.InvariantCulture)
                        + "; duplicateRefused=" + duplicateRefused + "(" + Clip(duplicateDetail, 80) + ")"
                        + "; duplicateRefusals=" + gate.DuplicateStepRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; dedicatedLocalScene=" + physics.IsDedicatedLocalScene
                        + "; automaticSuppressed=" + physics.AutomaticSimulationSuppressed);
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
                finally
                {
                    physicsWorld?.Dispose();
                }
            }

            /// <summary>
            /// Two independent course worlds integrate the SAME recorded input through the SAME declared step, and
            /// their recorded observations agree exactly at tolerance zero and produce the same rule digest — so a
            /// replay of the recorded input reproduces the rule digest. The same comparison against a trace whose one
            /// body is off by a single milli-unit MISMATCHES at zero and MATCHES at the package's declared comparison
            /// tolerance, which is what a recorded-observation comparison rather than a bitwise claim means for a
            /// physics-adjacent slice (P-008, TEST-022, REF-A06).
            /// </summary>
            private void ReplayReproducesTheRuleDigest()
            {
                const string name = "w6-replay-reproduces-the-rule-digest";
                Fixture? replay = null;
                try
                {
                    if (!TraversalReady(name, out string why))
                    {
                        Add(name, false, why);
                        return;
                    }

                    if (recordedInput.Count == 0)
                    {
                        Add(name, false, "the run recorded no admitted input, so there is nothing to replay");
                        return;
                    }

                    replay = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!replay.Ready)
                    {
                        Add(name, false, "the replay world could not be built: " + replay.Failure);
                        return;
                    }

                    int replayed = replay.RunRecordedSteps(recordedInput.Count, null);
                    TraversalTraceRecorder? first = traversal!.Trace;
                    TraversalTraceRecorder? second = replay.Trace;
                    if (replayed != recordedInput.Count || first == null || second == null)
                    {
                        Add(name, false, "the replay committed " + replayed.ToString(CultureInfo.InvariantCulture)
                            + " of " + recordedInput.Count.ToString(CultureInfo.InvariantCulture)
                            + " recorded samples, or a trace is missing");
                        return;
                    }

                    TraversalTraceComparison identical = first.CompareTo(second, 0);
                    string firstDigest = RuleDigest(first);
                    string secondDigest = RuleDigest(second);
                    bool digestsAgree = string.Equals(firstDigest, secondDigest, StringComparison.Ordinal);

                    TraversalTraceRecorder perturbed = Perturb(first, family.CycleTarget, 1);
                    TraversalTraceComparison bitwise = first.CompareTo(perturbed, 0);
                    TraversalTraceComparison tolerated = first.CompareTo(
                        perturbed, TraversalVocabulary.VelocityToleranceMilli);

                    int bodies = 0;
                    IReadOnlyList<TraversalStepTrace> recorded = first.Steps;
                    for (int i = 0; i < recorded.Count; i++)
                    {
                        bodies += recorded[i].Bodies.Count;
                    }

                    bool pass = identical.Matches
                        && identical.MismatchedBodies == 0
                        && identical.MismatchedSteps == 0
                        && identical.ComparedSteps == recorded.Count
                        && recorded.Count > 0
                        && bodies >= recordedInput.Count
                        && digestsAgree
                        && !bitwise.Matches
                        && bitwise.MismatchedBodies >= 1
                        && tolerated.Matches
                        && tolerated.MismatchedBodies == 0
                        && TraversalVocabulary.VelocityToleranceMilli > 0;

                    Add(name, pass,
                        "recordedSamples=" + recordedInput.Count.ToString(CultureInfo.InvariantCulture)
                        + "; replayedSamples=" + replayed.ToString(CultureInfo.InvariantCulture)
                        + "; recordedSteps=" + recorded.Count.ToString(CultureInfo.InvariantCulture)
                        + "; recordedBodies=" + bodies.ToString(CultureInfo.InvariantCulture)
                        + "; ruleDigest=" + firstDigest
                        + "; replayedDigest=" + secondDigest
                        + "; digestsAgree=" + digestsAgree
                        + "; identical=" + identical.ToString()
                        + "; perturbedBy=1"
                        + "; atTolerance0=" + bitwise.Matches
                        + "(mismatchedBodies=" + bitwise.MismatchedBodies.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; atDeclaredTolerance=" + tolerated.Matches
                        + "(tolerance=" + TraversalVocabulary.VelocityToleranceMilli.ToString(CultureInfo.InvariantCulture)
                        + ", mismatchedBodies=" + tolerated.MismatchedBodies.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; comparisonDetail=" + Clip(bitwise.Detail, 120));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
                finally
                {
                    replay?.Dispose();
                }
            }

            /// <summary>
            /// The other half of the composition audit, from the traversal side: the course really carries the
            /// optional engine surface. Its five declared stages and five dispatch keys are compiled into the world's
            /// descriptor, and its runtime holds the dedicated local physics scene, the gate that steps it exactly
            /// once per admitted step, the committed audio stage with its recording sink and the committed animation
            /// stage with its recording sink — so "optional physics/animation are absent from cards/narrative" is a
            /// contrast rather than an absence everywhere (P-034, P-059, 07 s4.1).
            /// </summary>
            private void CarriesTheOptionalEngineSurface()
            {
                const string name = "w6-carries-the-optional-engine-surface";
                try
                {
                    if (!TraversalReady(name, out string why))
                    {
                        Add(name, false, why);
                        return;
                    }

                    TraversalCourseSurface surface = family.ActionSurface();
                    Gc020StageRuntime? attached = traversal!.TraversalRuntime;

                    bool stages = surface.Stages.Count == TraversalKeys.SystemKeys.Length
                        && surface.Systems.Count == TraversalKeys.SystemKeys.Length
                        && traversal.Descriptor != null
                        && traversal.Descriptor.Stages.Count == surface.Stages.Count;

                    bool physics = attached != null
                        && attached.Physics != null
                        && attached.PhysicsGate != null
                        && attached.Physics.IsDedicatedLocalScene;

                    bool audio = attached != null && attached.Audio != null && attached.AudioSink != null;
                    bool animation = attached != null && attached.Animation != null && attached.AnimationSink != null;

                    Add(name, stages && physics && audio && animation,
                        "stages=" + surface.Stages.Count.ToString(CultureInfo.InvariantCulture)
                        + "; systems=" + surface.Systems.Count.ToString(CultureInfo.InvariantCulture)
                        + "; declaredStages=" + (traversal.Descriptor != null
                            ? traversal.Descriptor.Stages.Count.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; accelerationCapability=" + surface.AccelerationCapability
                        + "; physicsScene=" + (attached != null && attached.Physics != null
                            ? attached.Physics.SceneName : "<none>")
                        + "; physicsGate=" + (attached != null && attached.PhysicsGate != null)
                        + "; audioStage=" + (attached != null && attached.Audio != null)
                        + "; audioSink=" + (attached != null && attached.AudioSink != null)
                        + "; animationStage=" + (attached != null && attached.Animation != null)
                        + "; animationSink=" + (attached != null && attached.AnimationSink != null)
                        + "; audioSinkKind=recording-device-disabled(crash-139)"
                        + "; runtimeAttached=" + (attached != null));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 2. the composition audit

            /// <summary>
            /// Walks this genre's declarations and compiled descriptor looking for any traversal identity: a stage, a
            /// stage's factory keys, a system key, a buffer (the course's declared step buffer and its movement lane
            /// are its ports), a capability contract, a derivation rule, the course's configuration schema, its plugin
            /// factory key, a descriptor stage, a descriptor system key or an owned descriptor slot. A genre that
            /// declared nothing would be caught by the walked-entry counter being zero, so the audit can never report
            /// "clean" for a genre it read nothing of (P-001, P-059, TEST-021).
            /// </summary>
            private void DeclaresNoActionPhysicsOrAudioSurface()
            {
                const string name = "w6-declares-no-action-physics-or-audio-surface";
                try
                {
                    PipelineDescriptorReport descriptor = family.CompilePipeline();
                    W6FamilyAudit audit = W6CompositionAudit.WalkFamily(
                        family.Label, family.Declarations, descriptor, family.ActionSurface());

                    bool pass = audit.Clean
                        && audit.DeclaredStages > 0
                        && descriptor.Succeeded
                        && descriptor.Descriptor != null;

                    Add(name, pass,
                        audit.Describe()
                        + "; descriptorOutcome=" + descriptor.Outcome
                        + "; descriptorStages=" + (descriptor.Descriptor != null
                            ? descriptor.Descriptor.Stages.Count.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; descriptorSlots=" + (descriptor.Descriptor != null
                            ? descriptor.Descriptor.Slots.Count.ToString(CultureInfo.InvariantCulture) : "<none>"));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The build-time half of the same rule, read from the running process's own assembly graph: this genre's
            /// gameplay assembly must reference no `GameCore.Unity.Adapters` assembly, where the optional physics,
            /// animation and audio halves live, while the traversal course does. The reflection scan runs in the
            /// Editor and in the stripped player, so the claim survives the build the gate actually ships (P-001,
            /// 04 s2).
            /// </summary>
            private void AdapterAssemblyIsAbsent()
            {
                const string name = "w6-adapter-assembly-is-absent";
                try
                {
                    W6AdapterAssemblyFacts facts = W6CompositionAudit.AdapterAssemblies();
                    bool selfReferencesAdapters = IsNarrative()
                        ? facts.NarrativeReferencesAdapters
                        : facts.CardsReferencesAdapters;

                    bool pass = facts.AllLoaded
                        && !facts.NarrativeReferencesAdapters
                        && !facts.CardsReferencesAdapters
                        && !selfReferencesAdapters
                        && facts.TraversalReferencesAdapters;

                    Add(name, pass,
                        facts.Describe()
                        + "; selfAssembly=" + (IsNarrative()
                            ? W6CompositionAudit.NarrativeAssemblyName
                            : W6CompositionAudit.CardsAssemblyName)
                        + "; selfReferencesAdapters=" + selfReferencesAdapters);
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 3. durable reward delivery

            /// <summary>
            /// GC-021's durable reward delivery, across a GC-022 unload/reload cycle of the RECEIVING world.
            ///
            /// 1. A real card market commits one table result, and the gate's obligation source claims that committed
            ///    event, so the obligation is created from committed data rather than from staged work.
            /// 2. The obligation is persisted before it is applied: it is committed into a durable outbox whose
            ///    external idempotency key is derived from the committed event, the destination and the destination's
            ///    schema.
            /// 3. The receiving world is stopped and disposed — the GC-022 stop/recreate path — and the world is gone
            ///    from the registry.
            /// 4. A world of a NEW session is created, the same rows are reinstated into it, and the obligation is
            ///    dispatched: the destination mutates once and the owner acknowledges.
            /// 5. The acknowledgement is then lost (the at-least-once boundary P-045 states), the same rows are
            ///    reinstated into a third session, and the destination is asked a second time under the SAME external
            ///    key: it reports `AlreadyApplied` and mutates nothing.
            ///
            /// The mutation count across the whole sequence is therefore exactly one, and the evidence names every
            /// session, the reinstatement counts, the dispatch count and the destination's own two counters
            /// (P-045, P-050, P-053, TEST-016, TEST-017).
            /// </summary>
            private void RewardDeliveryIsExactlyOnceAcrossAReload()
            {
                const string name = "w6-reward-delivery-is-exactly-once-across-a-reload";
                Fixture? first = null;
                Fixture? second = null;
                Fixture? third = null;
                WorldDeliveryOwner? owner = null;
                try
                {
                    var journal = new MemoryDeliveryJournal("memory://w6-gate-reward");
                    var destination = new W6RewardDestination(RewardDestinationId, RewardCommandSchema);

                    // 1. The receiving world commits one real table result.
                    first = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!first.Ready)
                    {
                        Add(name, false, "the receiving world could not be built: " + first.Failure);
                        return;
                    }

                    ulong committed = first.SubmitAndPumpCycle(0);
                    if (committed != 1UL)
                    {
                        Add(name, false, "the receiving world committed " + committed.ToString(CultureInfo.InvariantCulture)
                            + " steps for its one command, so there is no committed event to deliver");
                        return;
                    }

                    // 2. One committed event becomes one durable obligation.
                    var source = new W6FirstCommittedEventSource(
                        RewardDestinationId, RewardCommandSchema, RewardPayloadBytes(1UL));
                    owner = new WorldDeliveryOwner(
                        first.Host,
                        DeliveryOwnerId,
                        DeliveryCapacity,
                        DeliveryTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        null);
                    if (!owner.TryRegisterDestination(destination, out string registerDetail))
                    {
                        Add(name, false, "registering the destination was refused: " + registerDetail);
                        return;
                    }

                    int observed = owner.PollCommittedEvents(source, first.NextOperation(), 8);
                    IReadOnlyList<OutboxRecordValue> rows = owner.ToRecords();
                    // The claim is about the OBLIGATION, not about how many committed events the world also carried:
                    // the source claims exactly one and refuses the rest, so a world that committed more than one
                    // event still becomes exactly one obligation, and the extra ones are counted as unclaimed.
                    bool obligated = observed >= 1
                        && source.ClaimedCount == 1
                        && owner.Outbox.OpenCount == 1
                        && owner.EnqueuedCount == 1
                        && rows.Count > 0
                        && owner.IsDurable;
                    if (!obligated)
                    {
                        Add(name, false, "the committed event did not become exactly one durable obligation"
                            + " (observed=" + observed.ToString(CultureInfo.InvariantCulture)
                            + "; claimed=" + source.ClaimedCount.ToString(CultureInfo.InvariantCulture)
                            + "; open=" + owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                            + "; enqueued=" + owner.EnqueuedCount.ToString(CultureInfo.InvariantCulture)
                            + "; rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                            + "; durable=" + owner.IsDurable + ")");
                        return;
                    }

                    WorldId firstSession = first.Host.World;
                    SchemaRef claimedSchema = source.ClaimedSchema;
                    EventSequence claimedSequence = source.ClaimedSequence;

                    // 3. Unload the receiving world: stop, dispose, unregister.
                    Outcome stopOutcome = first.StopAndDispose();
                    bool firstGone = !UnityWorldRegistry.TryGet(firstSession, out UnityWorldHost? _);
                    owner.Dispose();
                    owner = null;

                    // 4. Reload: a world of a NEW session, with the same rows reinstated and delivered.
                    second = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!second.Ready)
                    {
                        Add(name, false, "the reloaded receiving world could not be built: " + second.Failure);
                        return;
                    }

                    WorldId secondSession = second.Host.World;
                    bool sessionsDiffer = !firstSession.Session.Equals(secondSession.Session);

                    owner = new WorldDeliveryOwner(
                        second.Host,
                        DeliveryOwnerId,
                        DeliveryCapacity,
                        DeliveryTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        null);
                    if (!owner.TryRegisterDestination(destination, out string secondRegister))
                    {
                        Add(name, false, "registering the destination on the reloaded world was refused: " + secondRegister);
                        return;
                    }

                    bool reinstated = owner.TryReinstate(
                        rows, out DiagnosticCode reinstateCode, out string reinstateDetail);
                    int dispatched = reinstated ? owner.DispatchOpenObligations(DeliveryCapacity) : 0;
                    int mutationsAfterReload = destination.MutationCount;
                    int openAfterDispatch = owner.Outbox.OpenCount;
                    IReadOnlyList<Id128> openIds = owner.Outbox.OpenIds;
                    DeliveryOutcome ackOutcome = DeliveryOutcome.Delivered;
                    if (openAfterDispatch == 1 && openIds.Count == 1)
                    {
                        ackOutcome = owner.Adapter.TryAcknowledge(
                            openIds[0], out DiagnosticCode ackCode, out string ackDetail);
                        _ = ackCode;
                        _ = ackDetail;
                    }

                    bool deliveredOnce = reinstated
                        && dispatched == 1
                        && destination.MutationCount == 1
                        && destination.Attempts.Count == 1
                        && destination.AlreadyAppliedCount == 0
                        && ackOutcome == DeliveryOutcome.Acknowledged
                        && owner.Outbox.OpenCount == 0
                        && owner.AcknowledgedCount == 1;

                    // 5. The acknowledgement is lost; the same rows are reinstated once more into a third session.
                    owner.Dispose();
                    owner = null;
                    Outcome secondStop = second.StopAndDispose();
                    bool secondGone = !UnityWorldRegistry.TryGet(secondSession, out UnityWorldHost? _);

                    third = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!third.Ready)
                    {
                        Add(name, false, "the third receiving world could not be built: " + third.Failure);
                        return;
                    }

                    WorldId thirdSession = third.Host.World;
                    bool thirdDiffers = !thirdSession.Session.Equals(firstSession.Session)
                        && !thirdSession.Session.Equals(secondSession.Session);

                    owner = new WorldDeliveryOwner(
                        third.Host,
                        DeliveryOwnerId,
                        DeliveryCapacity,
                        DeliveryTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        null);
                    if (!owner.TryRegisterDestination(destination, out string thirdRegister))
                    {
                        Add(name, false, "registering the destination on the third world was refused: " + thirdRegister);
                        return;
                    }

                    bool reinstatedAgain = owner.TryReinstate(
                        rows, out DiagnosticCode secondReinstateCode, out string secondReinstateDetail);
                    int dispatchedAgain = reinstatedAgain ? owner.DispatchOpenObligations(DeliveryCapacity) : 0;
                    bool redeliveredOnce = reinstatedAgain
                        && dispatchedAgain == 1
                        && destination.MutationCount == 1
                        && destination.Attempts.Count == 2
                        && destination.AlreadyAppliedCount == 1
                        && owner.AcknowledgedCount == 1
                        && owner.Outbox.OpenCount == 0;

                    bool stopsClean = (stopOutcome == Outcome.Published || stopOutcome == Outcome.NoChange)
                        && (secondStop == Outcome.Published || secondStop == Outcome.NoChange);

                    bool pass = sessionsDiffer
                        && thirdDiffers
                        && claimedSchema.Equals(RewardCommandSchema)
                        && claimedSequence.Value > 0UL
                        && firstGone
                        && secondGone
                        && stopsClean
                        && deliveredOnce
                        && redeliveredOnce
                        && owner.IsDurable;

                    Add(name, pass,
                        "sessions=" + firstSession.Session.ToString()
                        + "->" + secondSession.Session.ToString()
                        + "->" + thirdSession.Session.ToString()
                        + "; sessionsDiffer=" + (sessionsDiffer && thirdDiffers)
                        + "; sourceStopped=" + stopOutcome
                        + "; reloadStopped=" + secondStop
                        + "; firstUnregistered=" + firstGone
                        + "; secondUnregistered=" + secondGone
                        + "; claimedSchema=" + claimedSchema
                        + "; claimedSequence=" + claimedSequence.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                        + "; reinstated=" + reinstated + "(" + DiagnosticCodeText.Of(reinstateCode)
                        + ": " + Clip(reinstateDetail, 60) + ")"
                        + "; dispatched=" + dispatched.ToString(CultureInfo.InvariantCulture)
                        + "; destinationMutationsAfterReload=" + mutationsAfterReload.ToString(CultureInfo.InvariantCulture)
                        + "; destinationMutations=" + destination.MutationCount.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAttempts=" + destination.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAlreadyApplied=" + destination.AlreadyAppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; acknowledged=" + owner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)
                        + "; reinstatedAgain=" + reinstatedAgain + "(" + DiagnosticCodeText.Of(secondReinstateCode)
                        + ": " + Clip(secondReinstateDetail, 60) + ")"
                        + "; dispatchedAgain=" + dispatchedAgain.ToString(CultureInfo.InvariantCulture)
                        + "; durable=" + owner.IsDurable
                        + "; journalFrames=" + journal.FrameCount.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
                finally
                {
                    owner?.Dispose();
                    third?.Dispose();
                    second?.Dispose();
                    first?.Dispose();
                }
            }

            // ================================================================== 4. the counted cycle loop

            /// <summary>
            /// The create/mount/step/unmount/teardown loop, for this genre, through one loop: each cycle mints a fresh
            /// installation identity, mounts it through the genre's own payload and the control lane, stages one real
            /// lease for that publication, publishes it, admits the genre's own command and commits exactly one
            /// logical step, then unmounts the installation through the genre's own payload and the lifecycle
            /// controller. The cycle is complete only when the publication reported the removal, the lease was retired
            /// and the installation reached `Disposed` (P-046, P-048, P-050, TEST-015).
            ///
            /// A cycle that does not publish is recorded as a failing step naming the cycle index, the diagnostic code
            /// and the detail, and the resolved cycle count is part of the detail, so a reduced run cannot be mistaken
            /// for the full one.
            /// </summary>
            private void ThousandCycleTeardownIsBounded()
            {
                const string name = "w6-thousand-cycle-teardown-is-bounded";
                try
                {
                    loop = Fixture.Build(
                        family, sessionSequence, TargetCapacity,
                        withRuntime: true, withProviders: true, pumpClockOrigin: true);
                    if (!loop.Ready)
                    {
                        Add(name, false, loop.Failure);
                        return;
                    }

                    loopController = new LifecycleController(loop.Host, loop.Lane!, loop.Publisher!, loop.Pipeline!);
                    worldBalanceAtBuild = loop.Host.Ledger.AcquireCount - loop.Host.Ledger.RetireCount;
                    worldRetainedAtBuild = loop.Host.Ledger.RetainedResourceCount;
                    worldOutstandingAtBuild = loop.Host.Ledger.OutstandingJobCount;

                    int completed = 0;
                    int failedCycle = -1;
                    string failedCode = "<none>";
                    string failure = string.Empty;
                    for (int index = 1; index <= cycles; index++)
                    {
                        PluginInstanceId instance = family.StressInstance((ulong)index);
                        if (!RunCycle(index, instance, out DiagnosticCode code, out string detail))
                        {
                            failedCycle = index;
                            failedCode = DiagnosticCodeText.Of(code);
                            failure = detail;
                            break;
                        }

                        cycleInstances.Add(instance);
                        completed++;
                    }

                    bool pass = failedCycle < 0 && completed == cycles && cycleInstances.Count == cycles;

                    Add(name, pass,
                        "cycles=" + cycles.ToString(CultureInfo.InvariantCulture)
                        + "; completed=" + completed.ToString(CultureInfo.InvariantCulture)
                        + "; failedCycle=" + failedCycle.ToString(CultureInfo.InvariantCulture)
                        + "; failedCode=" + failedCode
                        + "; laneRevision=" + loop.Lane!.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + loop.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; hostStep=" + loop.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneRetainedResults=" + LaneRetainedResults.ToString(CultureInfo.InvariantCulture)
                        + (failure.Length == 0 ? string.Empty : "; failure=" + Clip(failure, 200)));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The ledger and fence registries after the counted cycles: the lane owns no live lease, no retained row
            /// and no quarantine; its retired history is bounded by the acquisitions the loop really made rather than
            /// by elapsed time; and neither the composition-side nor the world-side job fence has an outstanding
            /// handle. The numbers are reported as the high-water marks they are, so a bounded registry and an
            /// unbounded one cannot look the same in the evidence (P-007, P-048, TEST-023, and GC-022's
            /// `artifacts/gc-022/resource-policy.md`).
            /// </summary>
            private void LedgerAndFenceHighWaterMarksAreBounded()
            {
                const string name = "w6-ledger-and-fence-high-water-marks-are-bounded";
                try
                {
                    if (!LoopReady(name, out string why))
                    {
                        Add(name, false, why);
                        return;
                    }

                    ResourceLedger resources = loop!.Lane!.Resources;
                    int records = resources.Records().Count;
                    int retired = resources.RetiredCount;
                    int evicted = resources.EvictedRetiredCount;
                    int liveLeases = resources.LiveLeaseCount;
                    int quarantined = resources.QuarantinedCount;
                    int failedReleases = resources.FailedReleaseCount;

                    int fenceOutstanding = loopController!.JobFence.OutstandingCount;
                    int fenceFailures = loopController.JobFence.CompletionFailureCount;
                    int trackedJobs = loop.Lane.Lifecycle.Jobs.TrackedCount;
                    int laneJobsOutstanding = loop.Lane.Lifecycle.Jobs.OutstandingCount;
                    int quarantineEntries = loop.Lane.Lifecycle.Quarantine.Count;
                    int liveActivations = loop.Lane.Callbacks.LiveActivationCount;

                    int worldAcquired = loop.Host.Ledger.AcquireCount;
                    int worldRetired = loop.Host.Ledger.RetireCount;
                    int worldRetained = loop.Host.Ledger.RetainedResourceCount;
                    ulong quarantinedBytes = loop.Host.Ledger.QuarantinedBytes;

                    bool bounded = liveLeases == 0
                        && quarantined == 0
                        && failedReleases == 0
                        && retired == cycles
                        && evicted + retired == cycles
                        && records <= cycles
                        && fenceOutstanding == 0
                        && fenceFailures == 0
                        && laneJobsOutstanding == 0
                        && quarantineEntries == 0
                        && liveActivations == 0
                        && trackedJobs >= 0
                        && worldAcquired - worldRetired == worldBalanceAtBuild
                        && worldRetained == worldRetainedAtBuild
                        && quarantinedBytes == 0UL;

                    Add(name, bounded,
                        "cycles=" + cycles.ToString(CultureInfo.InvariantCulture)
                        + "; highWaterRecords=" + records.ToString(CultureInfo.InvariantCulture)
                        + "; retired=" + retired.ToString(CultureInfo.InvariantCulture)
                        + "; evictedRetired=" + evicted.ToString(CultureInfo.InvariantCulture)
                        + "; liveLeases=" + liveLeases.ToString(CultureInfo.InvariantCulture)
                        + "; quarantined=" + quarantined.ToString(CultureInfo.InvariantCulture)
                        + "; failedReleases=" + failedReleases.ToString(CultureInfo.InvariantCulture)
                        + "; fenceOutstandingHighWater=" + fenceOutstanding.ToString(CultureInfo.InvariantCulture)
                        + "; fenceFailures=" + fenceFailures.ToString(CultureInfo.InvariantCulture)
                        + "; trackedJobsHighWater=" + trackedJobs.ToString(CultureInfo.InvariantCulture)
                        + "; laneJobsOutstanding=" + laneJobsOutstanding.ToString(CultureInfo.InvariantCulture)
                        + "; quarantineEntries=" + quarantineEntries.ToString(CultureInfo.InvariantCulture)
                        + "; liveActivations=" + liveActivations.ToString(CultureInfo.InvariantCulture)
                        + "; worldAcquired=" + worldAcquired.ToString(CultureInfo.InvariantCulture)
                        + "; worldRetired=" + worldRetired.ToString(CultureInfo.InvariantCulture)
                        + "; worldRetained=" + worldRetained.ToString(CultureInfo.InvariantCulture)
                        + "@baseline" + worldRetainedAtBuild.ToString(CultureInfo.InvariantCulture)
                        + "; worldOutstanding=" + loop.Host.Ledger.OutstandingJobCount.ToString(CultureInfo.InvariantCulture)
                        + "@baseline" + worldOutstandingAtBuild.ToString(CultureInfo.InvariantCulture)
                        + "; quarantinedBytes=" + quarantinedBytes.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The world's own counters after the counted cycles are the counters the world reported before them: the
            /// loop acquires and retires exactly what it staged, so the ledger's balance, its retained count and its
            /// outstanding job count come back to the baselines the world itself declared, and none of the loop's own
            /// installations is retained. The counters are stable, not merely non-zero (P-048, P-050, TEST-015).
            /// </summary>
            private void CountersReturnToBaseline()
            {
                const string name = "w6-counters-return-to-baseline";
                try
                {
                    if (!LoopReady(name, out string why))
                    {
                        Add(name, false, why);
                        return;
                    }

                    int missing = 0;
                    int retained = 0;
                    for (int i = 0; i < cycleInstances.Count; i++)
                    {
                        if (!TryInstallState(cycleInstances[i], out InstallationState _))
                        {
                            missing++;
                        }

                        retained += loop!.Lane!.Resources.RetainedCountFor(cycleInstances[i]);
                    }

                    int balance = loop!.Host.Ledger.AcquireCount - loop.Host.Ledger.RetireCount;
                    bool pass = retained == 0
                        && missing == 0
                        && balance == worldBalanceAtBuild
                        && loop.Host.Ledger.RetainedResourceCount == worldRetainedAtBuild
                        && loop.Host.Ledger.OutstandingJobCount == worldOutstandingAtBuild
                        && loop.Host.Ledger.QuarantinedBytes == 0UL
                        && loop.Host.Lifecycle == WorldLifecycleState.Running
                        && loop.MatchesPublishedAssembly();

                    Add(name, pass,
                        "instances=" + cycleInstances.Count.ToString(CultureInfo.InvariantCulture)
                        + "; instancesMissing=" + missing.ToString(CultureInfo.InvariantCulture)
                        + "; instancesRetained=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerBalance=" + balance.ToString(CultureInfo.InvariantCulture)
                        + "@baseline" + worldBalanceAtBuild.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerRetained=" + loop.Host.Ledger.RetainedResourceCount.ToString(CultureInfo.InvariantCulture)
                        + "@baseline" + worldRetainedAtBuild.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerOutstanding=" + loop.Host.Ledger.OutstandingJobCount.ToString(CultureInfo.InvariantCulture)
                        + "@baseline" + worldOutstandingAtBuild.ToString(CultureInfo.InvariantCulture)
                        + "; lifecycle=" + loop.Host.Lifecycle
                        + "; laneRevision=" + loop.Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + loop.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 5. repeatability

            /// <summary>
            /// The digest over this run's observation table, all passing, recomputed from the frozen name table rather
            /// than read from a run: a renamed, reordered, added or dropped observation changes the literal, so the
            /// gate cannot silently shrink, and two runs of the same fixture over the same catalog agree. That is the
            /// repeatability fixture the exit gate asks for (P-008, TEST-022).
            /// </summary>
            private void RepeatableDigest()
            {
                const string name = "w6-repeatable-digest";
                try
                {
                    string[] expected = QualifiedNames(family.Label);
                    IReadOnlyList<string> recorded = RecordedNames();

                    // Every observation but this one must already be recorded, and in the frozen order.
                    bool sameTable = recorded.Count == expected.Length - 1;
                    for (int i = 0; sameTable && i < recorded.Count; i++)
                    {
                        sameTable = string.Equals(recorded[i], expected[i], StringComparison.Ordinal);
                    }

                    var failing = new List<string>();
                    for (int i = 0; i < steps.Count; i++)
                    {
                        if (!steps[i].Passed)
                        {
                            failing.Add(steps[i].Name);
                        }
                    }

                    var table = new List<W6GateStep>(expected.Length);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        table.Add(new W6GateStep(expected[i], true, "table"));
                    }

                    // The digest this run reports is the digest of its own table once this step is added, so the value
                    // in the detail is the one the EditMode suite and the player probe compare their frozen literal
                    // against. A run that recorded a different sequence, or a failing observation, cannot produce it
                    // (P-008, TEST-022).
                    var prospective = new List<W6GateStep>(steps.Count + 1);
                    for (int i = 0; i < steps.Count; i++)
                    {
                        prospective.Add(steps[i]);
                    }

                    prospective.Add(new W6GateStep(expected[expected.Length - 1], failing.Count == 0, "self"));
                    string expectedDigest = new W6GateScenarioResult(family.Label, table).Digest;
                    string actualDigest = new W6GateScenarioResult(family.Label, prospective).Digest;
                    bool holds = failing.Count == 0
                        && sameTable
                        && string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal);

                    Add(name, holds,
                        "digest=" + actualDigest
                        + "; expectedDigest=" + expectedDigest
                        + "; observations=" + steps.Count.ToString(CultureInfo.InvariantCulture)
                        + "; tableSize=" + expected.Length.ToString(CultureInfo.InvariantCulture)
                        + "; cycleCount=" + cycles.ToString(CultureInfo.InvariantCulture)
                        + "; fixtureRun=" + fixtureRun
                        + (failing.Count == 0
                            ? string.Empty
                            : "; failedObservations=" + W6CompositionAudit.Join(failing)));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== the cycle itself

            private bool RunCycle(int index, PluginInstanceId instance, out DiagnosticCode code, out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                Fixture current = loop!;
                CompositionHost laneRef = current.Lane!;
                LifecycleController controllerRef = loopController!;

                OperationId mountOperation = current.NextOperation();
                EditAdmission admission = laneRef.SubmitEdit(
                    family.StressMount(family.StressDeclarations.Installation, instance, family.WorldRootScope),
                    mountOperation,
                    laneRef.Committed.Revision);
                if (!admission.Staged)
                {
                    code = admission.Code;
                    detail = "cycle " + Text(index) + ": the mount was refused (" + admission.Kind + "/"
                        + DiagnosticCodeText.Of(admission.Code) + ")";
                    return false;
                }

                ResourceKey leaseKey = new ResourceKey(leaseSequence.Next());
                bool staged = laneRef.StageResource(
                    mountOperation,
                    instance,
                    leaseKey,
                    new FrozenPayload(new byte[] { 1 }),
                    null,
                    out DiagnosticCode stageCode);
                IReadOnlyList<StagedLease> leases = laneRef.StagedLeases(mountOperation);
                IReadOnlyList<PublishedOperation> published = laneRef.Drain();
                DerivedAssemblyReport derived = current.Pipeline!.PublishDerived(mountOperation);
                CompleteWorldHalf(current, mountOperation, derived);

                PublishedOperation? mountPublication = PublishedOf(published, mountOperation);
                bool mounted = mountPublication != null
                    && mountPublication.Outcome == Outcome.Published
                    && TryInstallState(instance, out InstallationState mountState)
                    && mountState == InstallationState.Active;
                if (!mounted)
                {
                    code = mountPublication != null ? mountPublication.Code : derived.Code;
                    detail = "cycle " + Text(index) + ": the mount did not publish (state=" + StateOf(instance)
                        + "; outcome=" + (mountPublication != null ? mountPublication.Outcome.ToString() : "<none>")
                        + "; leases=" + Text(leases.Count)
                        + "; staged=" + staged + "/" + DiagnosticCodeText.Of(stageCode)
                        + "; derived=" + derived.Outcome + "/" + DiagnosticCodeText.Of(derived.Code) + ")";
                    return false;
                }

                // The step: the genre's own command through the world's own ingress, committing exactly one logical
                // step. A cycle whose step does not commit is a failing cycle, not a skipped one (P-036).
                ulong committed = current.SubmitAndPumpCycle((ulong)index);
                if (committed != 1UL)
                {
                    detail = "cycle " + Text(index) + ": the step committed " + Text(committed)
                        + " logical steps for one admitted command (P-036)";
                    return false;
                }

                OperationId unmountOperation = current.NextOperation();
                LifecycleRequestReport unmountReport = controllerRef.Submit(
                    family.StressUnmount(instance), unmountOperation);
                if (unmountReport.Derived != null)
                {
                    CompleteWorldHalf(current, unmountOperation, unmountReport.Derived);
                }

                TeardownReport? teardown = TeardownOf(unmountReport.Lifecycle, instance);
                bool unmounted = unmountReport.Succeeded
                    && TryInstallState(instance, out InstallationState unmountState)
                    && unmountState == InstallationState.Disposed
                    && teardown != null
                    && teardown.Cleanup.Retired.Count == 1
                    && teardown.Retraction.AttributedRows > 0
                    && laneRef.Resources.RetainedCountFor(instance) == 0
                    && laneRef.Resources.LiveLeaseCount == 0;
                if (!unmounted)
                {
                    code = unmountReport.Code;
                    detail = "cycle " + Text(index) + ": the unmount did not settle (state=" + StateOf(instance)
                        + "; succeeded=" + unmountReport.Succeeded
                        + "; retired=" + Text(teardown != null ? teardown.Cleanup.Retired.Count : -1)
                        + "; retractedRows=" + Text(teardown != null ? teardown.Retraction.AttributedRows : -1)
                        + "; retained=" + Text(laneRef.Resources.RetainedCountFor(instance))
                        + "; detail=" + Clip(unmountReport.Detail, 120) + ")";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Completes the world's half of one lifecycle publication. A publication whose derivation changed no
            /// effective binding is answered `NoTargetChange`, and P-006 still counts it as one publication of the one
            /// series, so the world must publish its assembly for that pair: a derivation that never adopted leaves
            /// the pair free and the world publishes its unchanged assembly for it; a derivation that adopted and then
            /// collapsed to a planner no-op leaves the pair adopted and pending, and the one legal consumer of a
            /// pending pair is a spawn, so a carrier is seeded from the family's declared future recipe and scope.
            /// This is the same reconciliation GC-022's stress performs, for the same reason (P-006, P-024).
            /// </summary>
            private void CompleteWorldHalf(Fixture current, OperationId operation, DerivedAssemblyReport derived)
            {
                CompositionHost laneRef = current.Lane!;
                AssemblyPublisher publisherRef = current.Publisher!;
                UnityWorldHost hostRef = current.Host;

                if (derived.Outcome != DerivedAssemblyOutcome.NoTargetChange
                    || AssemblyPublisher.MatchesPublishedAssembly(
                        laneRef.Committed.Revision,
                        laneRef.Committed.Epoch,
                        publisherRef.PublishedRevision,
                        hostRef.CurrentEpoch))
                {
                    return;
                }

                if (!publisherRef.HasAdoptedPublication)
                {
                    publisherRef.PublishUnchangedAssembly(
                        operation, laneRef.Committed.Revision, laneRef.Committed.Epoch);
                    return;
                }

                carrierOrdinal++;
                TargetId carrier = TargetId.FromRaw(CarrierSalt.High, CarrierSalt.Low + (ulong)carrierOrdinal);
                family.PrepareSpawn();
                current.Pipeline!.PublishSpawn(
                    current.NextOperation(), carrier, family.FutureRecipe, family.WorldRootScope);
            }

            // ================================================================== helpers

            private bool IsTraversal() => string.Equals(family.Label, Gc020TraversalHost.Label, StringComparison.Ordinal);

            private bool IsCards() => string.Equals(family.Label, Gc013CardsHost.Label, StringComparison.Ordinal);

            private bool IsNarrative() => string.Equals(family.Label, Gc013NarrativeHost.Label, StringComparison.Ordinal);

            private bool TraversalReady(string name, out string why)
            {
                _ = name;
                if (traversal == null || !traversal.Ready)
                {
                    why = traversal == null
                        ? "the course world was never built"
                        : "the course world is not ready: " + traversal.Failure;
                    return false;
                }

                why = string.Empty;
                return true;
            }

            private bool LoopReady(string name, out string why)
            {
                _ = name;
                if (loop == null || !loop.Ready || loopController == null)
                {
                    why = loop == null
                        ? "the loop world was never built"
                        : "the loop world is not ready: " + loop.Failure;
                    return false;
                }

                why = string.Empty;
                return true;
            }

            /// <summary>Records one observation, qualified with the family label at this single recording point.</summary>
            private void Add(string bareName, bool passed, string detail)
                => steps.Add(new W6GateStep(family.Label + "/" + bareName, passed, detail ?? string.Empty));

            private IReadOnlyList<string> RecordedNames()
            {
                var names = new string[steps.Count];
                for (int i = 0; i < steps.Count; i++)
                {
                    names[i] = steps[i].Name;
                }

                return names;
            }

            /// <summary>
            /// Turns an unrecorded observation into a failing step. A step method whose precondition failed must not
            /// leave a gap: the digest is computed over this table, so a missing name would otherwise change the
            /// digest silently rather than fail (P-008, P-060).
            /// </summary>
            private void EnsureEveryObservationIsRecorded()
            {
                string[] expected = QualifiedNames(family.Label);
                for (int i = 0; i < expected.Length; i++)
                {
                    bool found = false;
                    for (int s = 0; s < steps.Count && !found; s++)
                    {
                        found = string.Equals(steps[s].Name, expected[i], StringComparison.Ordinal);
                    }

                    if (!found)
                    {
                        steps.Add(new W6GateStep(expected[i], false, "the observation was never recorded"));
                    }
                }

                if (teardownFailure.Length != 0)
                {
                    steps.Add(new W6GateStep(family.Label + "/w6-teardown-returns-the-registry-to-its-baseline",
                        false, teardownFailure));
                }
            }

            /// <summary>
            /// Returns the process-wide registries to their baseline even when a step failed before its own teardown
            /// ran, so a leaked world is a real, named failure rather than a side effect of a failing run (P-048).
            /// </summary>
            private void TearDownSafely()
            {
                try
                {
                    if (loopController != null && loop != null && loop.Ready)
                    {
                        for (int i = 0; i < cycleInstances.Count; i++)
                        {
                            if (TryInstallState(cycleInstances[i], out InstallationState state)
                                && state != InstallationState.Disposed)
                            {
                                loopController.Unload(cycleInstances[i], loop.NextOperation());
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // A teardown that cannot settle an installation is reported by the registry check below.
                }

                loopController = null;
                loop?.Dispose();
                loop = null;
                traversal?.Dispose();
                traversal = null;

                int registryAfter = UnityWorldRegistry.Count;
                if (registryAfter != registryBaseline)
                {
                    teardownFailure = "registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; registryBaseline=" + registryBaseline.ToString(CultureInfo.InvariantCulture);
                }
            }

            private bool TryInstallState(PluginInstanceId instance, out InstallationState state)
            {
                state = default(InstallationState);
                if (loop == null || loop.Lane == null)
                {
                    return false;
                }

                if (!loop.Lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return false;
                }

                state = entry.State;
                return true;
            }

            private string StateOf(PluginInstanceId instance) =>
                TryInstallState(instance, out InstallationState state) ? state.ToString() : "<absent>";

            private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Clip(string value, int max)
                => value.Length <= max ? value : value.Substring(0, max);

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;

            private static string RuleDigest(TraversalTraceRecorder trace) => NarrativeDigest.OfLines(trace.ToLines());

            private static PhysicsVector3i AsPhysics(TraversalVector3i value) =>
                new PhysicsVector3i(value.X, value.Y, value.Z);

            /// <summary>
            /// A copy of one recorded trace with one runner's velocity moved by <paramref name="delta"/>, so the
            /// comparison's tolerance is asserted against a difference the gate made rather than against a coincidence
            /// (P-008, TEST-022).
            /// </summary>
            private static TraversalTraceRecorder Perturb(TraversalTraceRecorder source, TargetId target, int delta)
            {
                IReadOnlyList<TraversalStepTrace> recorded = source.Steps;
                var copy = new TraversalTraceRecorder(recorded.Count + 8);
                for (int s = 0; s < recorded.Count; s++)
                {
                    TraversalStepTrace step = recorded[s];
                    copy.BeginStep(step.Step, step.Epoch, step.ExternallyOwned);
                    for (int b = 0; b < step.Bodies.Count; b++)
                    {
                        TraversalBodyTrace body = step.Bodies[b];
                        TraversalVector3i velocity = body.Runner.Equals(target)
                            ? new TraversalVector3i(body.Velocity.X + delta, body.Velocity.Y, body.Velocity.Z)
                            : body.Velocity;
                        copy.RecordBody(new TraversalBodyTrace(
                            body.Runner, body.Pose, velocity, body.AppliedAcceleration, body.Jumped));
                    }

                    for (int c = 0; c < step.Crossings.Count; c++)
                    {
                        TraversalCrossingTrace crossing = step.Crossings[c];
                        copy.RecordCrossing(new TraversalCrossingTrace(
                            crossing.Runner, crossing.Checkpoint, crossing.CountAfter));
                    }

                    for (int r = 0; r < step.Refusals.Count; r++)
                    {
                        copy.RecordRefusal(step.Refusals[r]);
                    }

                    copy.CompleteStep();
                }

                return copy;
            }

            private static PublishedOperation? PublishedOf(
                IReadOnlyList<PublishedOperation> published, OperationId operation)
            {
                for (int i = 0; i < published.Count; i++)
                {
                    if (published[i].Operation.Equals(operation))
                    {
                        return published[i];
                    }
                }

                return null;
            }

            private static TeardownReport? TeardownOf(LifecycleCommitReport? report, PluginInstanceId instance)
            {
                if (report == null)
                {
                    return null;
                }

                for (int i = 0; i < report.Teardowns.Count; i++)
                {
                    if (report.Teardowns[i].Instance.Equals(instance))
                    {
                        return report.Teardowns[i];
                    }
                }

                return null;
            }

            private static byte[] RewardPayloadBytes(ulong ordinal)
            {
                var bytes = new byte[9];
                bytes[0] = 1;
                for (int i = 0; i < 8; i++)
                {
                    bytes[1 + i] = (byte)((ordinal >> ((7 - i) * 8)) & 0xFFUL);
                }

                return bytes;
            }
        }
    }
}
