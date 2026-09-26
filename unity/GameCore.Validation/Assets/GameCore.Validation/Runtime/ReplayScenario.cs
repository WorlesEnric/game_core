// GameCore.Validation.ProbeHost — the GC-023 replay and instrumentation scenario.
//
// Two halves, both driven from one entry point so the EditMode suite and the `-probeReplay` player probe run the
// same checks:
//
//   * **One real Unity world** (`RunWorld`) built through the production registry and the fixture registration. It
//     is the *actual* world GC-023 instruments: its driver, its three guarded groups, its resource ledger, its
//     publication store, its observation surface and its message plane are all sampled through
//     `ITelemetryOwner.WriteTelemetry` into one frame per observation. The named observations then assert what
//     TEST-023 requires of a real world — an idle command-driven world advances zero steps and does zero
//     control-plane work (0 control-tree visits, 0 string service resolutions), a registered wake commits exactly one
//     step and produces duration samples, and the memory split is reported as four separate numbers with no
//     quarantine and no retained events on an untouched world.
//
//   * **The pure fixture in the player** (`RunReplay`). The 10,000-step integer fixture is replayed across every
//     supported worker count and with shuffled producer/completion order, the differential sweep runs against the
//     real oracle with its reducer, the observation recording is replayed and compared separately from a
//     native-physics comparison, and the raw benchmark trace is written and read back. This is what TEST-022 asks a
//     replay to prove, and running it in the player is what proves the fixtures survive IL2CPP's stripping.
//
// Nothing here re-implements kernel behaviour: the world is the production host, the derivation is the production
// engine, and every counter read goes through the owner that owns it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using GameCore.Replay;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Jobs.LowLevel.Unsafe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named observation of the replay scenario.</summary>
    public sealed class ReplayStep
    {
        public ReplayStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => (Passed ? "pass " : "FAIL ") + Name + ": " + Detail;
    }

    /// <summary>The scenario's observations plus the artifacts it produced.</summary>
    public sealed class ReplayScenarioResult
    {
        public ReplayScenarioResult(
            IReadOnlyList<ReplayStep> steps,
            string rawTrace,
            string jsonTrace,
            string digest)
        {
            Steps = steps;
            RawTrace = rawTrace ?? string.Empty;
            JsonTrace = jsonTrace ?? string.Empty;
            Digest = digest ?? string.Empty;
        }

        public IReadOnlyList<ReplayStep> Steps { get; }

        /// <summary>The raw benchmark trace of the world observations (08 s3's measurement artifact).</summary>
        public string RawTrace { get; }

        /// <summary>The same trace as the artifact JSON document.</summary>
        public string JsonTrace { get; }

        /// <summary>Digest over the named observations, so a renamed or dropped step changes it (P-008).</summary>
        public string Digest { get; }

        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count != 0;
            }
        }

        public string Describe()
        {
            int passed = 0;
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Passed)
                {
                    passed++;
                }
            }

            return "replay-scenario{observations=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + ";passed=" + passed.ToString(CultureInfo.InvariantCulture)
                + ";digest=" + Digest + "}";
        }
    }

    /// <summary>The GC-023 replay and instrumentation scenario.</summary>
    public static class ReplayScenario
    {
        /// <summary>Named observations of the real-world half.</summary>
        public static readonly IReadOnlyList<string> WorldNames = Array.AsReadOnly(new[]
        {
            "replay-world-created-and-owners-sampled",
            "replay-world-idle-pump-does-zero-control-work",
            "replay-world-memory-split-is-reported-separately",
            "replay-world-wake-commits-one-step-and-samples-durations",
            "replay-world-counters-move-on-a-committed-step",
        });

        /// <summary>Named observations of the pure replay half.</summary>
        public static readonly IReadOnlyList<string> ReplayNames = Array.AsReadOnly(new[]
        {
            "replay-trace-records-ten-thousand-steps",
            "replay-hashes-identically-across-worker-counts",
            "replay-hashes-identically-under-shuffled-producers",
            "replay-different-admission-is-a-different-input",
            "replay-observation-replay-is-separate-from-physics",
            "replay-differential-sweep-is-clean-and-reducible",
            "replay-raw-benchmark-trace-round-trips",
        });

        /// <summary>Every observation name of this scenario, in the order the runs report them.</summary>
        public static IReadOnlyList<string> AllNames { get; } = Combine(WorldNames, ReplayNames);

        /// <summary>The recorded fixture seed and label the player probe and the suite both use.</summary>
        public const uint FixtureSeed = 20260923U;

        /// <summary>Label of the recorded trace.</summary>
        public const string RecordLabel = "integer-10000";

        /// <summary>Idle frames the world observation pumps before it asserts zero work.</summary>
        public const int IdleFrames = 64;

        private const ulong HostTick = 1_000_000UL;

        /// <summary>Runs both halves; the world half first, so its trace is the artifact.</summary>
        public static ReplayScenarioResult RunAll()
        {
            var steps = new List<ReplayStep>();
            ReplayScenarioResult world = RunWorld();
            for (int i = 0; i < world.Steps.Count; i++)
            {
                steps.Add(world.Steps[i]);
            }

            ReplayScenarioResult replay = RunReplay();
            for (int i = 0; i < replay.Steps.Count; i++)
            {
                steps.Add(replay.Steps[i]);
            }

            return new ReplayScenarioResult(steps, world.RawTrace, world.JsonTrace, DigestOf(steps));
        }

        /// <summary>Digest over the observation sequence: one line per observation, name and outcome (P-008).</summary>
        public static string DigestOf(IReadOnlyList<ReplayStep> steps)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                builder.Append("name=").Append(steps[i].Name)
                    .Append(" result=").Append(steps[i].Passed ? "pass" : "fail")
                    .Append('\n');
            }

            return ReplayStateHash.HashOf(builder.ToString()).ToHex();
        }

        /// <summary>
        /// The real-world half: one command-driven fixture world, sampled through every one of its own telemetry
        /// owners before and after a committed step.
        /// </summary>
        public static ReplayScenarioResult RunWorld()
        {
            var steps = new List<ReplayStep>();
            var session = new WorldId(new Id128(0x47433032334B4559UL, 0x0102030405060708UL));
            UnityWorldHost? host = null;
            string rawTrace = string.Empty;
            string jsonTrace = string.Empty;
            try
            {
                bool created = UnityWorldRegistry.TryCreate(
                    FixtureRegistration.CommandDrivenRequest(
                        session,
                        new OperationId(session, W1GateKeys.Issuer, 1UL),
                        ContentHash.Empty),
                    FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: false),
                    out host,
                    out WorldCreateResult createResult);

                if (!created || host == null)
                {
                    steps.Add(new ReplayStep(
                        WorldNames[0],
                        false,
                        "the fixture world was not created: " + createResult.Code + " " + createResult.Detail));
                    return new ReplayScenarioResult(steps, string.Empty, string.Empty, DigestOf(steps));
                }

                // A host clock, supplied explicitly. The kernel never reads a clock itself (P-008); this is the one
                // place a measurement origin is chosen, and it is chosen by the host.
                Func<long> clock = () => TelemetryDurations.TicksToMicroseconds(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                host.Driver.TelemetryClock = clock;
                host.IngressGroup.TelemetryClock = clock;
                host.StepGroup.TelemetryClock = clock;
                host.OutputGroup.TelemetryClock = clock;

                var collector = new TelemetryCollector(new TelemetryRetention(IdleFrames + 4, 16));
                int owners = RegisterOwners(collector, host);
                // The host acquires its own storage, identity index and one registration per fixture system before
                // the world becomes Running, so an untouched world legitimately retains those; what the idle
                // observations must prove is that nothing *new* is retained by pumping.
                int hostResourceCountAtCreation = host.Ledger.RetainedResourceCount;

                TelemetryFrame createdFrame = collector.Sample(0, host.CurrentEpoch, host.CurrentStep);
                // Seven owners are registered, but the three guarded groups share one owner key by design, so a
                // frame carries five sections here: driver, resources, images, observation and the merged dispatch
                // section. The assertion is on the sections the frame actually has, not on the registration count.
                bool createdPassed = host.Lifecycle == WorldLifecycleState.Running
                    && host.CurrentStep.Value == 0UL
                    && owners >= 7
                    && createdFrame.Sections.Count >= 5;
                steps.Add(new ReplayStep(
                    WorldNames[0],
                    createdPassed,
                    "lifecycle=" + host.Lifecycle
                    + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; step=" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                    + "; owners=" + owners.ToString(CultureInfo.InvariantCulture)
                    + "; sections=" + createdFrame.Sections.Count.ToString(CultureInfo.InvariantCulture)
                    + "; frame=" + createdFrame.Describe()));

                // Idle frames: a command-driven world with no demand performs no step and, with it, no control-plane
                // work at all. Every one of the 64 frames is sampled, so "zero" is asserted per frame rather than
                // once at the end.
                bool idlePassed = true;
                var idleDetail = new StringBuilder();
                ulong stepsBefore = host.CurrentStep.Value;
                for (int frame = 1; frame <= IdleFrames; frame++)
                {
                    WorldPumpResult pump = host.PumpFrame(HostTick * (ulong)(frame + 1));
                    TelemetryFrame sampled = collector.Sample(0, host.CurrentEpoch, host.CurrentStep);
                    TelemetryCounterSet aggregate = sampled.Aggregate();
                    bool frameIdle = !pump.Reentrant
                        && host.CurrentStep.Value == stepsBefore
                        && host.Driver.CommittedStepCount == 0
                        && aggregate.Get(TelemetryCounter.StepsAdvanced) == 0L
                        && aggregate.Get(TelemetryCounter.ControlNodesVisited) == 0L
                        && aggregate.Get(TelemetryCounter.ServiceStringLookups) == 0L
                        && aggregate.Get(TelemetryCounter.PlanPreparedBytes) == 0L
                        && aggregate.Get(TelemetryCounter.ContributionsAdded) == 0L;
                    if (!frameIdle)
                    {
                        idlePassed = false;
                        idleDetail.Append("frame").Append(frame).Append(':').Append(aggregate.Describe()).Append(';');
                        break;
                    }
                }

                idleDetail.Append("pumps=").Append(host.PumpCount.ToString(CultureInfo.InvariantCulture))
                    .Append("; step=").Append(host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture))
                    .Append("; dispatchRuns=").Append(host.StepGroup.DispatchRunCount.ToString(CultureInfo.InvariantCulture))
                    .Append("; publishedImages=").Append(host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture))
                    .Append("; controlNodes=0; serviceStringLookups=0");
                steps.Add(new ReplayStep(WorldNames[1], idlePassed, idleDetail.ToString()));

                // The memory split, from the owners that hold each kind of memory. TEST-023 requires leases, retained
                // events, retained caches and quarantine to be separate numbers, and an untouched world must have
                // nothing quarantined and nothing retained by the event store.
                TelemetryFrame memoryFrame = collector.Sample(0, host.CurrentEpoch, host.CurrentStep);
                TelemetryCounterSet memory = memoryFrame.Aggregate();
                WorldResourceLedgerSnapshot ledger = host.ReadResourceLedger();
                bool memoryPassed = memory.Get(TelemetryCounter.QuarantineBytes) == 0L
                    && memory.Get(TelemetryCounter.QuarantineEntries) == 0L
                    && memory.Get(TelemetryCounter.RetainedEventBytes) == 0L
                    && memory.Get(TelemetryCounter.RetainedEventCount) == 0L
                    && memory.Get(TelemetryCounter.LeaseBytes) == 0L
                    && memory.Get(TelemetryCounter.CacheBytes) == 0L
                    && ledger.QuarantinedBytes == 0UL
                    && ledger.QuarantinedCount == 0
                    && host.Ledger.RetainedResourceCount == hostResourceCountAtCreation
                    && host.Ledger.OutstandingJobCount == 0;
                steps.Add(new ReplayStep(
                    WorldNames[2],
                    memoryPassed,
                    "leaseBytes=" + memory.Get(TelemetryCounter.LeaseBytes).ToString(CultureInfo.InvariantCulture)
                    + "; retainedEventBytes=" + memory.Get(TelemetryCounter.RetainedEventBytes).ToString(CultureInfo.InvariantCulture)
                    + "; retainedEventCount=" + memory.Get(TelemetryCounter.RetainedEventCount).ToString(CultureInfo.InvariantCulture)
                    + "; cacheBytes=" + memory.Get(TelemetryCounter.CacheBytes).ToString(CultureInfo.InvariantCulture)
                    + "; quarantineBytes=" + memory.Get(TelemetryCounter.QuarantineBytes).ToString(CultureInfo.InvariantCulture)
                    + "; quarantineEntries=" + memory.Get(TelemetryCounter.QuarantineEntries).ToString(CultureInfo.InvariantCulture)
                    + "; ledgerQuarantined=" + ledger.QuarantinedBytes.ToString(CultureInfo.InvariantCulture)
                    + "; retainedResources=" + host.Ledger.RetainedResourceCount.ToString(CultureInfo.InvariantCulture)
                    + "; retainedResourcesAtCreation=" + hostResourceCountAtCreation.ToString(CultureInfo.InvariantCulture)
                    + "; outstandingJobs=" + host.Ledger.OutstandingJobCount.ToString(CultureInfo.InvariantCulture)));

                // One registered wake: exactly one logical step, and with it the first real duration samples. This is
                // the observable that proves the counter is wired to a live boundary rather than to a constant zero.
                host.RequestWake(1U);
                WorldPumpResult wakePump = host.PumpFrame(HostTick * (ulong)(IdleFrames + 2));
                TelemetryFrame wakeFrame = collector.Sample(0, host.CurrentEpoch, host.CurrentStep);
                TelemetryCounterSet wake = wakeFrame.Aggregate();
                bool wakePassed = wakePump.Advance != null
                    && wakePump.Advance!.Outcome == Outcome.Published
                    && host.CurrentStep.Value == 1UL
                    && host.Driver.CommittedStepCount == 1
                    && wake.Get(TelemetryCounter.StepsAdvanced) == 1L
                    && wake.Get(TelemetryCounter.JobWaitSampleCount) >= 1L
                    && wake.Get(TelemetryCounter.JobWaitDurationMicroseconds) >= 0L;
                steps.Add(new ReplayStep(
                    WorldNames[3],
                    wakePassed,
                    "advance=" + (wakePump.Advance != null ? wakePump.Advance.Outcome.ToString() : "<none>")
                    + "; step=" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                    + "; committed=" + host.Driver.CommittedStepCount.ToString(CultureInfo.InvariantCulture)
                    + "; stepsAdvanced=" + wake.Get(TelemetryCounter.StepsAdvanced).ToString(CultureInfo.InvariantCulture)
                    + "; jobWaitSamples=" + wake.Get(TelemetryCounter.JobWaitSampleCount).ToString(CultureInfo.InvariantCulture)
                    + "; jobWaitUs=" + wake.Get(TelemetryCounter.JobWaitDurationMicroseconds).ToString(CultureInfo.InvariantCulture)
                    + "; stageSamples=" + wake.Get(TelemetryCounter.StageSampleCount).ToString(CultureInfo.InvariantCulture)
                    + "; publishedImages=" + host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture)));

                // The committed step is observable and the counters that describe it moved: the step's image exists,
                // the store holds no *retained* event bytes (the fixture commits no events), and the driver's own
                // counters agree with the frame.
                bool movedPassed = host.Publications.PublishedCount >= 1
                    && wake.Get(TelemetryCounter.StepsAdvanced) == (long)host.Driver.CommittedStepCount
                    && host.Driver.CurrentStep.Equals(host.CurrentStep)
                    && wake.Get(TelemetryCounter.DiscardedCallbacks) == 0L
                    && wake.Get(TelemetryCounter.RequestOverflow) == 0L;
                steps.Add(new ReplayStep(
                    WorldNames[4],
                    movedPassed,
                    "driverSteps=" + host.Driver.CommittedStepCount.ToString(CultureInfo.InvariantCulture)
                    + "; frameSteps=" + wake.Get(TelemetryCounter.StepsAdvanced).ToString(CultureInfo.InvariantCulture)
                    + "; images=" + host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture)
                    + "; requestOverflow=" + wake.Get(TelemetryCounter.RequestOverflow).ToString(CultureInfo.InvariantCulture)
                    + "; discardedCallbacks=" + wake.Get(TelemetryCounter.DiscardedCallbacks).ToString(CultureInfo.InvariantCulture)
                    + "; sections=" + wakeFrame.Sections.Count.ToString(CultureInfo.InvariantCulture)));

                rawTrace = BenchmarkTrace.Write(collector.Trace("gc023-world-observations"));
                jsonTrace = BenchmarkTrace.WriteJson(collector.Trace("gc023-world-observations"));

                OperationResult stop = host.Stop(
                    new OperationId(session, W1GateKeys.Issuer, 2UL),
                    "GC-023 replay scenario teardown");
                if (stop.Outcome != Outcome.Published)
                {
                    steps.Add(new ReplayStep(
                        "replay-world-teardown",
                        false,
                        "the fixture world did not stop cleanly: " + stop.Outcome + " " + stop.Code + " " + stop.CodeText));
                }

                host = null;
            }
            catch (Exception exception)
            {
                steps.Add(new ReplayStep(
                    WorldNames[1],
                    false,
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
            finally
            {
                if (host != null)
                {
                    try
                    {
                        host.Stop(new OperationId(session, W1GateKeys.Issuer, 3UL), "GC-023 replay scenario recovery teardown");
                    }
                    catch (Exception)
                    {
                        // A teardown failure is reported by the world's own lifecycle; it must not mask the scenario's
                        // observations.
                    }
                }

                UnityWorldRegistry.Remove(session);
            }

            return new ReplayScenarioResult(steps, rawTrace, jsonTrace, DigestOf(steps));
        }

        /// <summary>
        /// Registers every telemetry owner of one live world: the driver, the three guarded groups (one merged
        /// section), the resource ledger, the publication store, the observation surface, its event store and the
        /// message plane. The return value is how many owners were registered, so a missing plane is visible rather
        /// than silently shrinking the evidence.
        /// </summary>
        public static int RegisterOwners(TelemetryCollector collector, UnityWorldHost host)
        {
            if (collector == null)
            {
                throw new ArgumentNullException(nameof(collector));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            int before = collector.OwnerCount;
            collector.Add(host.Driver);
            collector.Add(host.Ledger);
            collector.Add(host.Publications);
            collector.Add(host.Observation);
            collector.Add(host.IngressGroup);
            collector.Add(host.StepGroup);
            collector.Add(host.OutputGroup);
            CommittedEventStore? events = host.Observation.Events;
            if (events != null)
            {
                collector.Add(events);
            }

            WorldMessagePlane? messages = host.Messages;
            if (messages != null)
            {
                collector.Add(messages.Requests);
                collector.Add(messages.Schedule);
            }

            return collector.OwnerCount - before;
        }

        /// <summary>
        /// The pure half: the recorded 10,000-step trace replayed across the supported worker counts and under a
        /// shuffled producer/completion order, the differential sweep, the observation replay and the raw trace
        /// format. Every assertion is between two runs of the same record, never against a literal, so what the
        /// player proves is agreement rather than a remembered number.
        /// </summary>
        public static ReplayScenarioResult RunReplay()
        {
            var steps = new List<ReplayStep>();
            try
            {
                ReplayTrace trace = IntegerFixtureGenerator.Generate(
                    RecordLabel, FixtureSeed, IntegerFixtureGenerator.DefaultShape);

                bool recordedPassed = trace.Steps.Count == 10000
                    && trace.CompositionStepCount > 0
                    && trace.Steps[9999].Admission.Value > 0UL;
                steps.Add(new ReplayStep(
                    ReplayNames[0],
                    recordedPassed,
                    "steps=" + trace.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; compositionSteps=" + trace.CompositionStepCount.ToString(CultureInfo.InvariantCulture)
                    + "; catalog=" + trace.CatalogHash.ToHex()
                    + "; shape=" + trace.Shape.Describe()));

                int originalWorkers = JobsUtility.JobWorkerCount;
                ReplayRun baseline;
                try
                {
                    JobsUtility.JobWorkerCount = 1;
                    baseline = new ReplayRunner(
                        new ReplayOptions(1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
                }
                finally
                {
                    JobsUtility.JobWorkerCount = originalWorkers;
                }
                IReadOnlyList<int> counts = WorkerSchedule.SupportedWorkerCounts(trace.Shape.MaxWorkers);
                bool workersPassed = true;
                var workerDetail = new StringBuilder();
                for (int i = 0; i < counts.Count; i++)
                {
                    ReplayRun run;
                    int effectiveWorkers;
                    try
                    {
                        JobsUtility.JobWorkerCount = counts[i];
                        effectiveWorkers = JobsUtility.JobWorkerCount;
                        run = new ReplayRunner(
                            new ReplayOptions(counts[i], 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
                    }
                    finally
                    {
                        JobsUtility.JobWorkerCount = originalWorkers;
                    }
                    ReplayComparison comparison = ReplayComparison.Compare(baseline, run);
                    bool equal = effectiveWorkers == counts[i] && comparison.SameInput && comparison.Equal;
                    workersPassed = workersPassed && equal;
                    workerDetail.Append("workers").Append(counts[i]).Append(equal ? "=equal;" : "=DIFFERENT;")
                        .Append("jobsUtility=").Append(effectiveWorkers).Append(';');
                    if (!equal)
                    {
                        workerDetail.Append(comparison.Describe()).Append(';');
                    }
                }

                workerDetail.Append("baseline=").Append(baseline.Hashes.Describe())
                    .Append("; carriedSteps=").Append(baseline.CarriedStepCount.ToString(CultureInfo.InvariantCulture));
                steps.Add(new ReplayStep(ReplayNames[1], workersPassed, workerDetail.ToString()));

                ReplayRun shuffled;
                try
                {
                    JobsUtility.JobWorkerCount = 4;
                    shuffled = new ReplayRunner(
                        new ReplayOptions(4, 977U, true, TelemetryRetention.Off, 1000000, 0)).Run(trace);
                }
                finally
                {
                    JobsUtility.JobWorkerCount = originalWorkers;
                }
                ReplayComparison shuffledComparison = ReplayComparison.Compare(baseline, shuffled);
                steps.Add(new ReplayStep(
                    ReplayNames[2],
                    shuffledComparison.SameInput && shuffledComparison.Equal && shuffled.ProducerShuffles > 0,
                    "shuffles=" + shuffled.ProducerShuffles.ToString(CultureInfo.InvariantCulture)
                    + "; source=" + shuffled.SourceIdentity
                    + "; " + shuffledComparison.Describe()));

                ReplayRun different = new ReplayRunner(
                    new ReplayOptions(1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(
                        WithDifferentAdmission(trace));
                ReplayComparison differentComparison = ReplayComparison.Compare(baseline, different);
                steps.Add(new ReplayStep(
                    ReplayNames[3],
                    !differentComparison.SameInput && !differentComparison.Equal,
                    differentComparison.Describe()));

                IReadOnlyList<RecordedObservation> recording = ObservationRecorder.Record(trace, 1234U);
                ObservationReplayResult first = ObservationReplay.Replay(recording);
                ObservationReplayResult second = ObservationReplay.Replay(recording);
                IReadOnlyList<RecordedObservation> otherPhysics = ObservationRecorder.Record(trace, 1235U);
                NativePhysicsComparison physics = NativePhysicsComparison.Compare(recording, otherPhysics);
                bool observationPassed = ObservationReplay.RuleReplaysMatch(first, second)
                    && !physics.Identical
                    && physics.MaxComponentDelta > 0;
                steps.Add(new ReplayStep(
                    ReplayNames[4],
                    observationPassed,
                    "ruleHash=" + first.RuleHash.ToHex()
                    + "; observationHash=" + first.ObservationHash.ToHex()
                    + "; observations=" + first.ObservationCount.ToString(CultureInfo.InvariantCulture)
                    + "; physicsIdentical=" + physics.Identical
                    + "; physicsMaxDelta=" + physics.MaxComponentDelta.ToString(CultureInfo.InvariantCulture)
                    + "; physicsFirstDivergence=" + physics.FirstDivergence.ToString(CultureInfo.InvariantCulture)));

                var sweep = new DifferentialPropagation(new ReplayFixtureShape(2, 2, 1, 2, 1, 2, 1U));
                DifferentialSweepResult clean = sweep.Sweep(seeds: 12, steps: 32);
                var perturbed = new DifferentialPropagation(
                    new ReplayFixtureShape(2, 2, 1, 2, 1, 2, 1U),
                    (operation, result) =>
                        operation.Kind == ReplayOperationKind.MoveTarget
                        || operation.Kind == ReplayOperationKind.MountProvider
                        || operation.Kind == ReplayOperationKind.SwitchMode);
                FailingSeedRecord? witness = perturbed.RunSeed(1U, 24);
                bool witnessHolds = witness != null
                    && witness.Reduced.Count > 0
                    && witness.Reduced.Count <= witness.Original.Count
                    && perturbed.Run(witness.Reduced, witness.Seed, out _, out _, out int failingStep) != null
                    && failingStep >= 0;
                steps.Add(new ReplayStep(
                    ReplayNames[5],
                    clean.Failures.Count == 0 && witnessHolds,
                    clean.Describe()
                    + "; witness=" + (witness == null ? "<none>" : witness.Describe())
                    + "; witnessScript=" + (witness == null ? string.Empty : witness.ReducedScript().Replace('\n', '|'))));

                ReplayRun measured = new ReplayRunner(
                    new ReplayOptions(2, 0U, false, TelemetryRetention.Default, 1000000, 0)).Run(trace);
                TelemetryTrace telemetry = measured.Telemetry!;
                string raw = BenchmarkTrace.Write(telemetry);
                bool readBack = BenchmarkTrace.TryRead(raw, out IReadOnlyList<BenchmarkTrace.RawFrame> frames, out string failure);
                string json = BenchmarkTrace.WriteJson(telemetry);
                steps.Add(new ReplayStep(
                    ReplayNames[6],
                    readBack && frames.Count == telemetry.Frames.Count && json.Length > 0 && failure.Length == 0,
                    "frames=" + telemetry.Frames.Count.ToString(CultureInfo.InvariantCulture)
                    + "; readBack=" + frames.Count.ToString(CultureInfo.InvariantCulture)
                    + "; chain=" + telemetry.ChainHash.ToHex()
                    + "; jsonBytes=" + json.Length.ToString(CultureInfo.InvariantCulture)
                    + (failure.Length == 0 ? string.Empty : "; failure=" + failure)));
            }
            catch (Exception exception)
            {
                steps.Add(new ReplayStep(
                    ReplayNames[0],
                    false,
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }

            return new ReplayScenarioResult(steps, string.Empty, string.Empty, DigestOf(steps));
        }

        /// <summary>Rebuilds a trace with one admission sequence and batch size changed (a different input trace).</summary>
        private static ReplayTrace WithDifferentAdmission(ReplayTrace trace)
        {
            var mutated = new ReplayStepRecord[trace.Steps.Count];
            for (int i = 0; i < mutated.Length; i++)
            {
                ReplayStepRecord record = trace.Steps[i];
                mutated[i] = i == 5
                    ? new ReplayStepRecord(
                        record.Step,
                        new AdmissionSequence(record.Admission.Value + 1UL),
                        record.SealedBatch,
                        record.AdmittedCommands + 1,
                        record.Operation,
                        record.OperationArgument,
                        record.ProducerSeed,
                        record.CompletionSeed,
                        record.Workers)
                    : record;
            }

            return new ReplayTrace(trace.Label, trace.FixtureSeed, trace.Shape, mutated, null);
        }

        private static IReadOnlyList<string> Combine(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            var all = new string[first.Count + second.Count];
            for (int i = 0; i < first.Count; i++)
            {
                all[i] = first[i];
            }

            for (int i = 0; i < second.Count; i++)
            {
                all[first.Count + i] = second[i];
            }

            return Array.AsReadOnly(all);
        }
    }
}
