// GameCore.Replay — the replay runner: one recorded input trace driven over the real kernel (GC-023, TEST-022).
//
// What a run does per recorded step, in this order and no other:
//
//   1. **Apply the step's composition operation**, if the record names one, through the fixture builder. The
//      operation is applied before the step so the step sees the composition the record describes.
//   2. **Seal the step's input batch** with the record's host-assigned admission sequence. Commands are admitted in
//      the record's order; a step whose batch is empty performs no rule work (the idle case).
//   3. **Derive** with the real incremental engine against the previous accepted result. When the composition did
//      not change, the engine carries the previous result and examines no candidate — the path TEST-023 measures.
//   4. **Run the integer rule stage** over the derived assemblies, with the independent producers scheduled across
//      the record's worker count. The producers are enumerated in the *completion order* the record's seed
//      produces, which may be any permutation of the start order; only the canonical composition order of the
//      result may reach the state hash.
//   5. **Commit**: advance the logical step, build the committed event identities for the targets whose integer
//      state changed, and record the step's canonical hashes.
//
// The observable output of a run is a `ReplayRun`: the per-step hashes, the final hashes and the counters. Two runs
// of the same trace with different producer/completion orders and worker counts must produce equal hashes; a run
// against a *different* admitted order is a different input trace and is expected to differ (TEST-022 says so
// explicitly, and `ReplayComparison.CompareInputs` refuses to compare two different records as if they were one).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;

namespace GameCore.Replay
{
    /// <summary>One step's canonical result: the hashes, the state table and the committed event identities.</summary>
    public sealed class ReplayStepResult
    {
        public ReplayStepResult(
            int step,
            LogicalStepId logicalStep,
            AssemblyEpoch epoch,
            bool derivedWork,
            ContentHash stateHash,
            ContentHash decisionHash,
            ContentHash provenanceHash,
            IReadOnlyList<ReplayTargetState>? states,
            IReadOnlyList<string>? eventIdentities,
            InvalidationClosureResult? invalidation,
            DerivationResult? derivation)
        {
            Step = step;
            LogicalStep = logicalStep;
            Epoch = epoch;
            DerivedWork = derivedWork;
            StateHash = stateHash;
            DecisionHash = decisionHash;
            ProvenanceHash = provenanceHash;
            States = Freeze(states);
            EventIdentities = Freeze(eventIdentities);
            Invalidation = invalidation;
            Derivation = derivation;
        }

        /// <summary>Zero-based index in the trace.</summary>
        public int Step { get; }

        /// <summary>The logical step this result committed (P-006: one increment per committed step).</summary>
        public LogicalStepId LogicalStep { get; }

        /// <summary>Assembly epoch in force for this step.</summary>
        public AssemblyEpoch Epoch { get; }

        /// <summary>True when this step's composition change reached the derivation engine at all (P-023).</summary>
        public bool DerivedWork { get; }

        public ContentHash StateHash { get; }

        public ContentHash DecisionHash { get; }

        public ContentHash ProvenanceHash { get; }

        /// <summary>Per-target integer state after this step, in canonical target order.</summary>
        public IReadOnlyList<ReplayTargetState> States { get; }

        /// <summary>Committed event identities of the targets whose state changed in this step, in commit order.</summary>
        public IReadOnlyList<string> EventIdentities { get; }

        /// <summary>The invalidation of this step's change, when the engine ran.</summary>
        public InvalidationClosureResult? Invalidation { get; }

        /// <summary>The derivation this step committed, when the engine ran.</summary>
        public DerivationResult? Derivation { get; }

        public string Describe() =>
            "step " + Step.ToString(CultureInfo.InvariantCulture)
            + "(@" + LogicalStep.Value.ToString(CultureInfo.InvariantCulture)
            + ",e" + Epoch.Value.ToString(CultureInfo.InvariantCulture) + ")"
            + " derived=" + (DerivedWork ? "1" : "0")
            + " state=" + StateHash.ToHex()
            + " decisions=" + DecisionHash.ToHex()
            + " provenance=" + ProvenanceHash.ToHex()
            + " events=" + EventIdentities.Count.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => Describe();

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>The whole observable output of one run: per-step results, final hashes, counters and telemetry.</summary>
    public sealed class ReplayRun
    {
        public ReplayRun(
            ReplayTrace trace,
            IReadOnlyList<ReplayStepResult>? steps,
            ReplayTraceHashes hashes,
            TelemetryTrace? telemetry,
            int workers,
            int producerShuffles,
            string sourceIdentity)
        {
            Trace = trace;
            Steps = steps == null ? Array.Empty<ReplayStepResult>() : Freeze(steps);
            Hashes = hashes;
            Telemetry = telemetry;
            Workers = workers;
            ProducerShuffles = producerShuffles;
            SourceIdentity = sourceIdentity ?? string.Empty;
        }

        /// <summary>The recorded record this run drove.</summary>
        public ReplayTrace Trace { get; }

        public IReadOnlyList<ReplayStepResult> Steps { get; }

        /// <summary>The run's committed canonical hashes; two equal runs have equal values here.</summary>
        public ReplayTraceHashes Hashes { get; }

        /// <summary>The retained telemetry trace, or null when this run did not collect any.</summary>
        public TelemetryTrace? Telemetry { get; }

        /// <summary>Worker counts this run used, in step order, so a sweep can prove the range it covered.</summary>
        public int Workers { get; }

        /// <summary>Producer-order permutations this run observed, so a sweep can prove they were shuffled.</summary>
        public int ProducerShuffles { get; }

        /// <summary>
        /// A short identity of what produced the run — normally <c>workers=4;producers=shuffled</c>. It is never
        /// part of a hash: two runs with different identities must compare equal, which is the whole claim.
        /// </summary>
        public string SourceIdentity { get; }

        /// <summary>Committed logical steps; equal to the trace's step count for a complete run (P-006).</summary>
        public int CommittedStepCount => Steps.Count;

        /// <summary>Steps that examined no candidate because the composition did not change (P-023).</summary>
        public int CarriedStepCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].DerivedWork)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Compact audit text of the run (P-052, P-060).</summary>
        public string Describe() =>
            "replay-run{" + Trace.Describe()
            + ";source=" + SourceIdentity
            + ";steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
            + ";carried=" + CarriedStepCount.ToString(CultureInfo.InvariantCulture)
            + ";" + Hashes.Describe() + "}";

        public override string ToString() => Describe();

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
        {
            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>How a run was configured: the producer/completion seeds, worker count and telemetry retention.</summary>
    public sealed class ReplayOptions
    {
        public ReplayOptions(
            int workers,
            uint seedOffset,
            bool shuffleProducers,
            TelemetryRetention telemetry,
            int stateBound,
            int worldOrdinal)
        {
            Workers = workers < 1 ? 1 : workers;
            SeedOffset = seedOffset;
            ShuffleProducers = shuffleProducers;
            Telemetry = telemetry;
            StateBound = stateBound;
            WorldOrdinal = worldOrdinal;
        }

        /// <summary>The 10,000-step reference configuration: 1 worker, no shuffle, bounded retention.</summary>
        public static ReplayOptions Reference { get; } =
            new ReplayOptions(1, 0U, false, TelemetryRetention.Default, 1000000, 0);

        /// <summary>Worker count this run schedules independent producers across (TEST-022's 1/2/4/max).</summary>
        public int Workers { get; }

        /// <summary>Offset added to every step's producer/completion seed; the only scheduling freedom.</summary>
        public uint SeedOffset { get; }

        /// <summary>True to observe producers in their seeded completion order instead of their start order.</summary>
        public bool ShuffleProducers { get; }

        /// <summary>Explicit retention policy of this run's telemetry collector.</summary>
        public TelemetryRetention Telemetry { get; }

        /// <summary>Saturation bound of the integer rule stage; a positive bound keeps the state finite.</summary>
        public int StateBound { get; }

        /// <summary>Fixture world ordinal this run reports in comparison output (never a runtime identity).</summary>
        public int WorldOrdinal { get; }
    }

    /// <summary>
    /// Drives one recorded trace over the real derivation engine. The runner owns no policy of its own: the
    /// composition, the derivation and the invalidation are the kernel's, and this class only supplies the recorded
    /// input order and the recorded scheduling.
    /// </summary>
    public sealed class ReplayRunner
    {
        private readonly ReplayOptions options;
        private readonly FixtureValueSource values;
        private readonly TelemetryCollector telemetry;

        public ReplayRunner(ReplayOptions? options = null)
        {
            this.options = options ?? ReplayOptions.Reference;
            values = IntegerFixtureGenerator.ValueSource();
            telemetry = new TelemetryCollector(this.options.Telemetry);
        }

        /// <summary>The telemetry collector of this runner, so a caller can register owners before a run.</summary>
        public TelemetryCollector Telemetry => telemetry;

        /// <summary>Drives the trace and returns the run's observable output.</summary>
        public ReplayRun Run(ReplayTrace trace, IReadOnlyList<ITelemetryOwner>? owners = null)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            if (owners != null)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    telemetry.Add(owners[i]);
                }
            }

            FixtureBuilder builder = IntegerFixtureGenerator.Builder(trace.Shape);
            // The initial composition: one provider per branch, so the first step derives real work.
            for (int b = 0; b < trace.Shape.Branches; b++)
            {
                builder.Install(
                    IntegerFixtureGenerator.ProviderName(b, 0),
                    IntegerFixtureGenerator.Branch(b),
                    0,
                    IntegerFixtureGenerator.ProviderRules(IntegerFixtureGenerator.ProviderName(b, 0), b + 1),
                    InstallationState.Active);
            }

            var states = new List<ReplayTargetState>();
            var stateByTarget = new Dictionary<Id128, int>();
            var appliedByTarget = new Dictionary<Id128, int>();
            var results = new List<ReplayStepResult>(trace.Steps.Count);
            var eventIdentities = new List<string>();
            var stateHashes = new List<byte[]>();
            var stepCounterHashes = new List<byte[]>();
            var stepOwners = new List<ITelemetryOwner>(2);
            int producerShuffles = 0;

            PropagationMode mode = PropagationMode.Automatic;
            CompositionRevision revision = CompositionRevision.First;
            AssemblyEpoch epoch = AssemblyEpoch.First;
            DerivationResult? previous = null;
            DerivationChangeSet? pendingChange = null;
            int spawnCounter = 0;
            int moveCounter = 0;
            int payloadCounter = 0;
            int providersMounted = 1;

            for (int s = 0; s < trace.Steps.Count; s++)
            {
                ReplayStepRecord record = trace.Steps[s];
                bool derivedWork = false;

                // 1. The recorded composition operation, if any. It is what makes the following derivation dirty.
                switch (record.Operation)
                {
                    case ReplayOperationKind.MountProvider:
                    {
                        string provider = record.OperationArgument;
                        if (!HasInstall(builder, provider))
                        {
                            providersMounted++;
                            builder.Install(
                                provider,
                                IntegerFixtureGenerator.Branch(providersMounted % trace.Shape.Branches),
                                0,
                                IntegerFixtureGenerator.ProviderRules(provider, providersMounted),
                                InstallationState.Active);
                            revision = new CompositionRevision(revision.Value + 1UL);
                            epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        }

                        break;
                    }

                    case ReplayOperationKind.ReconfigureProvider:
                    {
                        string provider = record.OperationArgument;
                        if (HasInstall(builder, provider))
                        {
                            payloadCounter++;
                            builder.ReplaceRulePayload(
                                provider,
                                provider + IntegerFixtureGenerator.CounterRuleSuffix,
                                FixturePayload.Int32(payloadCounter));
                            revision = new CompositionRevision(revision.Value + 1UL);
                            epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        }

                        break;
                    }

                    case ReplayOperationKind.UnmountProvider:
                    {
                        string provider = record.OperationArgument;
                        if (HasInstall(builder, provider) && providersMounted > 1)
                        {
                            providersMounted--;
                            builder.RemoveInstall(provider);
                            revision = new CompositionRevision(revision.Value + 1UL);
                            epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        }

                        break;
                    }

                    case ReplayOperationKind.SpawnTarget:
                    {
                        string target = IntegerFixtureGenerator.TargetName(
                            spawnCounter % trace.Shape.Branches, spawnCounter / trace.Shape.Branches);
                        spawnCounter++;
                        if (!TargetExists(builder, target))
                        {
                            builder.Target(target, IntegerFixtureGenerator.Branch(spawnCounter % trace.Shape.Branches),
                                IntegerFixtureGenerator.TargetRecipe);
                            revision = new CompositionRevision(revision.Value + 1UL);
                            epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        }

                        break;
                    }

                    case ReplayOperationKind.MoveTarget:
                    {
                        string target = record.OperationArgument;
                        if (TargetExists(builder, target))
                        {
                            moveCounter++;
                            builder.MoveTarget(target, IntegerFixtureGenerator.Branch(moveCounter % trace.Shape.Branches));
                            revision = new CompositionRevision(revision.Value + 1UL);
                            epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        }

                        break;
                    }

                    case ReplayOperationKind.SwitchMode:
                    {
                        mode = mode == PropagationMode.Automatic
                            ? PropagationMode.Conservative
                            : PropagationMode.Automatic;
                        revision = new CompositionRevision(revision.Value + 1UL);
                        epoch = new AssemblyEpoch(epoch.Value + 1UL);
                        break;
                    }

                    default:
                        break;
                }

                // 2. Seal the recorded batch. A step with no admitted command still commits (a CommandDriven world
                //    would advance only for demand; the fixture keeps every recorded step, which is what makes the
                //    per-step hash chain complete).
                DerivationSnapshot snapshot = builder.Build(mode, revision, epoch).ToSnapshot();
                pendingChange = previous == null
                    ? DerivationChangeSet.Diff(snapshot, snapshot)
                    : DerivationChangeSet.Diff(previous.Snapshot, snapshot);
                if (!pendingChange.IsEmpty || previous == null)
                {
                    derivedWork = true;
                }

                // 3. Derive. The incremental engine carries everything when nothing changed, and then no candidate
                //    is examined at all (P-023, TEST-023).
                DerivationOptions derivationOptions = new DerivationOptions(null, null, null, null, true);
                IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                    snapshot, values, derivationOptions, previous, pendingChange);
                if (!outcome.Result.Accepted)
                {
                    throw new InvalidOperationException(
                        "the replay fixture's derivation was rejected at step "
                        + s.ToString(CultureInfo.InvariantCulture) + ": "
                        + outcome.Result.DiagnosticCode + " (" + outcome.Result.Snapshot.CanonicalText() + ")");
                }

                previous = outcome.Result;

                // 4. The integer rule stage, over the derived assemblies. Producers are enumerated in the recorded
                //    worker/completion order; the assemblies themselves are in canonical order, so the scheduling
                //    cannot reach the result.
                int targetCount = outcome.Result.Assemblies.Count;
                int[] order = options.ShuffleProducers
                    ? WorkerSchedule.CompletionOrder(targetCount, record.CompletionSeed + options.SeedOffset)
                    : WorkerSchedule.StartOrder(targetCount, options.Workers, record.ProducerSeed + options.SeedOffset);
                if (options.ShuffleProducers && targetCount > 1)
                {
                    producerShuffles++;
                }

                states.Clear();
                var stepEvents = new List<string>();
                for (int i = 0; i < order.Length; i++)
                {
                    TargetAssembly assembly = outcome.Result.Assemblies[order[i]];
                    stateByTarget.TryGetValue(assembly.Target.Value, out int current);
                    appliedByTarget.TryGetValue(assembly.Target.Value, out int applied);
                    int next = IntegerRuleStage.Apply(assembly, current, options.StateBound);
                    int appliedNow = IntegerRuleStage.AppliedCount(assembly);
                    stateByTarget[assembly.Target.Value] = next;
                    appliedByTarget[assembly.Target.Value] = applied + appliedNow;
                    states.Add(new ReplayTargetState(assembly.Target, next, applied + appliedNow));
                    if (next != current)
                    {
                        stepEvents.Add(ReplayStateHash.EventIdentityText(
                            assembly.Target,
                            new SchemaRef(FixtureIds.Schema(IntegerFixtureGenerator.CounterSchema), 1U),
                            new LogicalStepId((ulong)s + 1UL),
                            epoch,
                            next));
                    }
                }

                // The scheduling order visits every target exactly once, but it is *not* canonical: with two
                // workers the start order interleaves lanes, and a shuffled completion order is an arbitrary
                // permutation. Only canonical order may reach a hash (P-008), so the state table is sorted by
                // target and the event identities are sorted ordinally before either is hashed or accumulated.
                states.Sort(static (left, right) => left.Target.CompareTo(right.Target));
                stepEvents.Sort(StringComparer.Ordinal);
                for (int e = 0; e < stepEvents.Count; e++)
                {
                    eventIdentities.Add(stepEvents[e]);
                }

                // 5. Commit: the step id advances by exactly one per committed step (P-006).
                LogicalStepId committed = new LogicalStepId((ulong)s + 1UL);
                ContentHash stateHash = ReplayStateHash.IntegerStateHash(states);
                ContentHash decisionHash = ReplayStateHash.HashOf(
                    ReplayStateHash.DecisionText(outcome.Result) + ReplayStateHash.ExplanationText(outcome.Result));
                ContentHash provenanceHash = ReplayStateHash.HashOf(ReplayStateHash.ProvenanceText(outcome.Result));
                stateHashes.Add(stateHash.ToArray());

                // The telemetry sample is a *cost* observation: it is retained in the trace but never folded into a
                // correctness hash, which is what keeps instrumentation from changing semantics (GC-023).
                // One owner for the derivation family: on the incremental path the outcome, its closure and its
                // result share one counter object, so registering two of them would report the same cumulative
                // counters twice in one frame. The result is the superset - it also reports the delta's contribution
                // counts - and the closure's own counters stay readable on the closure object.
                stepOwners.Clear();
                stepOwners.Add(outcome.Result);
                TelemetryFrame frame = telemetry.Sample(options.WorldOrdinal, epoch, committed, stepOwners);
                stepCounterHashes.Add(frame.Hash().ToArray());

                results.Add(new ReplayStepResult(
                    s,
                    committed,
                    epoch,
                    derivedWork,
                    stateHash,
                    decisionHash,
                    provenanceHash,
                    states,
                    stepEvents,
                    outcome.Invalidation,
                    outcome.Result));

            }

            ReplayTraceHashes hashes = BuildHashes(results, stateHashes, stepCounterHashes, eventIdentities);
            return new ReplayRun(
                trace,
                results,
                hashes,
                telemetry.Trace(trace.Label),
                options.Workers,
                producerShuffles,
                DescribeSource());
        }

        private string DescribeSource() =>
            "workers=" + options.Workers.ToString(CultureInfo.InvariantCulture)
            + ";producers=" + (options.ShuffleProducers ? "shuffled" : "start-order")
            + ";seedOffset=" + options.SeedOffset.ToString(CultureInfo.InvariantCulture);

        private static ReplayTraceHashes BuildHashes(
            List<ReplayStepResult> results,
            List<byte[]> stateHashes,
            List<byte[]> stepCounterHashes,
            List<string> eventIdentities)
        {
            if (results.Count == 0)
            {
                return new ReplayTraceHashes(
                    ContentHash.Empty, ContentHash.Empty, ContentHash.Empty,
                    ContentHash.Empty, ContentHash.Empty, ContentHash.Empty);
            }

            ReplayStepResult last = results[results.Count - 1];
            return new ReplayTraceHashes(
                last.StateHash,
                last.DecisionHash,
                last.ProvenanceHash,
                ReplayStateHash.EventHash(eventIdentities),
                Chain(stateHashes),
                Chain(stepCounterHashes));
        }

        /// <summary>
        /// The chain over per-step hashes. It is `ReplayStateHash.Chain` - one definition shared with the
        /// real-Unity-jobs runner - so both comparers mean the same thing by "the same replay" (TEST-022).
        /// </summary>
        private static ContentHash Chain(List<byte[]> perStep)
        {
            var hashes = new ContentHash[perStep.Count];
            for (int i = 0; i < perStep.Count; i++)
            {
                hashes[i] = new ContentHash(perStep[i]);
            }

            return ReplayStateHash.Chain(hashes);
        }

        private static bool HasInstall(FixtureBuilder builder, string provider)
        {
            FixtureComposition composition = builder.Build(
                PropagationMode.Automatic, CompositionRevision.First, AssemblyEpoch.First);
            for (int i = 0; i < composition.Installs.Count; i++)
            {
                if (composition.Installs[i].Record.Instance.Equals(FixtureIds.Instance(provider)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TargetExists(FixtureBuilder builder, string target)
        {
            FixtureComposition composition = builder.Build(
                PropagationMode.Automatic, CompositionRevision.First, AssemblyEpoch.First);
            for (int i = 0; i < composition.Targets.Count; i++)
            {
                if (composition.Targets[i].Target.Equals(FixtureIds.Target(target)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Compares two runs of the same recorded trace. Equality of every committed hash is the acceptance criterion;
    /// the source identities are printed so a failure shows what differed (TEST-022).
    /// </summary>
    public sealed class ReplayComparison
    {
        private ReplayComparison(
            bool sameInput,
            bool equal,
            int firstDivergence,
            string detail,
            string firstIdentity,
            string secondIdentity)
        {
            SameInput = sameInput;
            Equal = equal;
            FirstDivergence = firstDivergence;
            Detail = detail ?? string.Empty;
            FirstIdentity = firstIdentity ?? string.Empty;
            SecondIdentity = secondIdentity ?? string.Empty;
        }

        /// <summary>True when both runs drove the same recorded input (same steps, order, batches and seeds).</summary>
        public bool SameInput { get; }

        /// <summary>True when every committed hash is equal.</summary>
        public bool Equal { get; }

        /// <summary>Index of the first step whose hash differed, or -1.</summary>
        public int FirstDivergence { get; }

        /// <summary>Human-readable difference, including both source identities.</summary>
        public string Detail { get; }

        public string FirstIdentity { get; }

        public string SecondIdentity { get; }

        /// <summary>Compares two runs; a different recorded input is reported and never reported as equal.</summary>
        public static ReplayComparison Compare(ReplayRun first, ReplayRun second)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }

            if (second == null)
            {
                throw new ArgumentNullException(nameof(second));
            }

            bool sameInput = SameRecordedInput(first.Trace, second.Trace);
            if (!sameInput)
            {
                return new ReplayComparison(
                    false,
                    false,
                    -1,
                    "the two runs drove different recorded inputs, which TEST-022 treats as different traces rather "
                    + "than a replay failure: first=" + first.Trace.Describe() + "; second=" + second.Trace.Describe(),
                    first.SourceIdentity,
                    second.SourceIdentity);
            }

            if (first.Steps.Count != second.Steps.Count)
            {
                return new ReplayComparison(
                    true,
                    false,
                    0,
                    "the two runs committed a different number of steps: "
                    + first.Steps.Count.ToString(CultureInfo.InvariantCulture) + " against "
                    + second.Steps.Count.ToString(CultureInfo.InvariantCulture),
                    first.SourceIdentity,
                    second.SourceIdentity);
            }

            for (int i = 0; i < first.Steps.Count; i++)
            {
                ReplayStepResult left = first.Steps[i];
                ReplayStepResult right = second.Steps[i];
                if (!left.StateHash.Equals(right.StateHash))
                {
                    return new ReplayComparison(true, false, i, "state diverged: " + left.Describe() + " against " + right.Describe(), first.SourceIdentity, second.SourceIdentity);
                }

                if (!left.DecisionHash.Equals(right.DecisionHash))
                {
                    return new ReplayComparison(true, false, i, "decisions diverged: " + left.Describe() + " against " + right.Describe(), first.SourceIdentity, second.SourceIdentity);
                }

                if (!left.ProvenanceHash.Equals(right.ProvenanceHash))
                {
                    return new ReplayComparison(true, false, i, "provenance diverged: " + left.Describe() + " against " + right.Describe(), first.SourceIdentity, second.SourceIdentity);
                }

                if (left.EventIdentities.Count != right.EventIdentities.Count)
                {
                    return new ReplayComparison(true, false, i, "the committed event count diverged at step " + i.ToString(CultureInfo.InvariantCulture), first.SourceIdentity, second.SourceIdentity);
                }

                for (int e = 0; e < left.EventIdentities.Count; e++)
                {
                    if (!string.Equals(left.EventIdentities[e], right.EventIdentities[e], StringComparison.Ordinal))
                    {
                        return new ReplayComparison(true, false, i, "a committed event identity diverged at step " + i.ToString(CultureInfo.InvariantCulture), first.SourceIdentity, second.SourceIdentity);
                    }
                }
            }

            bool equal = first.Hashes.FinalState.Equals(second.Hashes.FinalState)
                && first.Hashes.FinalDecisions.Equals(second.Hashes.FinalDecisions)
                && first.Hashes.FinalProvenance.Equals(second.Hashes.FinalProvenance)
                && first.Hashes.EventIdentities.Equals(second.Hashes.EventIdentities)
                && first.Hashes.StateChain.Equals(second.Hashes.StateChain);
            return new ReplayComparison(
                true,
                equal,
                equal ? -1 : 0,
                equal ? "identical" : "the per-step hashes matched but a final hash differed: " + first.Hashes.Describe() + " against " + second.Hashes.Describe(),
                first.SourceIdentity,
                second.SourceIdentity);
        }

        /// <summary>Compares the recorded inputs themselves: steps, batches, operations and seeds.</summary>
        public static bool SameRecordedInput(ReplayTrace first, ReplayTrace second)
        {
            if (!ReferenceEquals(first, second) && first.Steps.Count != second.Steps.Count)
            {
                return false;
            }

            if (!first.CatalogHash.Equals(second.CatalogHash) || first.FixtureSeed != second.FixtureSeed)
            {
                return false;
            }

            for (int i = 0; i < first.Steps.Count; i++)
            {
                ReplayStepRecord left = first.Steps[i];
                ReplayStepRecord right = second.Steps[i];
                if (left.Admission.Value != right.Admission.Value
                    || left.AdmittedCommands != right.AdmittedCommands
                    || left.SealedBatch != right.SealedBatch
                    || left.Operation != right.Operation
                    || !string.Equals(left.OperationArgument, right.OperationArgument, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public string Describe() =>
            "comparison{equal=" + (Equal ? "1" : "0")
            + ";sameInput=" + (SameInput ? "1" : "0")
            + ";firstDivergence=" + FirstDivergence.ToString(CultureInfo.InvariantCulture)
            + ";first=" + FirstIdentity
            + ";second=" + SecondIdentity
            + ";" + Detail + "}";

        public override string ToString() => Describe();
    }
}
