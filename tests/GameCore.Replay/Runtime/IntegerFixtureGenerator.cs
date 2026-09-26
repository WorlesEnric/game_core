// GameCore.Replay — the deterministic integer-rule fixture and its generator (GC-023, TEST-022).
//
// TEST-022: "Record at least 10,000 logical steps for a pure integer-rule fixture, with fixed catalog/configuration
// versions, seeds, host-assigned admission sequences, sealed batch boundaries and composition-operation order."
//
// This file is that fixture's *description*: a composition shape (scopes, integer-rule targets, providers with
// integer payloads), a step script (which step admits a command, which step runs a composition operation, which
// steps are deliberately unchanged), and the seeds a replay derives its producer/completion order from. Everything
// is an integer and every identity derives from a stable name, so two runs of the same record cannot differ for a
// reason the record does not name.
//
// Shape of the fixture (all counts configurable, defaults as recorded):
//
//   replay-root
//   ├── replay-branch-0 .. replay-branch-3        (4 branches)
//   │   └── replay-target-<b>-<t>                 (TargetsPerBranch targets each)
//   └── replay-isolated                           (capability isolation: every branch provider is blocked)
//       └── replay-target-iso-0 .. 3
//
// Two capability contracts, because P-021's strata must be exercised:
//
//   replay.counter   stratum 0, Additive, int32 sum  — the integer value the rule stage integrates
//   replay.weight    stratum 1, Replace             — a lower-stratum-dependent binding
//
// The generator is an LCG over the record's seed, so a failing step is addressable from the seed and the step
// alone, exactly like the derivation suite's operation sequences (TEST-008's reproducibility requirement).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;

namespace GameCore.Replay
{
    /// <summary>One composition operation the step script performs, as a stable-keyed description.</summary>
    public enum ReplayOperationKind
    {
        /// <summary>Nothing changes: a step that must not do control-plane work (TEST-023's zero-work case).</summary>
        None = 0,

        /// <summary>Mount a counter provider at a branch scope.</summary>
        MountProvider = 1,

        /// <summary>Reconfigure a provider's integer payload.</summary>
        ReconfigureProvider = 2,

        /// <summary>Unmount a provider.</summary>
        UnmountProvider = 3,

        /// <summary>Spawn a target under a branch scope.</summary>
        SpawnTarget = 4,

        /// <summary>Move a target to another branch scope.</summary>
        MoveTarget = 5,

        /// <summary>Switch the world's propagation mode (the whole-world invalidation case).</summary>
        SwitchMode = 6,
    }

    /// <summary>One step of a recorded input trace: the admitted command, the composition operation and the seeds.</summary>
    public readonly struct ReplayStepRecord
    {
        public ReplayStepRecord(
            int step,
            AdmissionSequence admission,
            bool sealedBatch,
            int admittedCommands,
            ReplayOperationKind operation,
            string operationArgument,
            uint producerSeed,
            uint completionSeed,
            int workers)
        {
            Step = step;
            Admission = admission;
            SealedBatch = sealedBatch;
            AdmittedCommands = admittedCommands;
            Operation = operation;
            OperationArgument = operationArgument ?? string.Empty;
            ProducerSeed = producerSeed;
            CompletionSeed = completionSeed;
            Workers = workers;
        }

        /// <summary>Zero-based logical step index; the recorded step is `Step + 1` (P-006).</summary>
        public int Step { get; }

        /// <summary>Host-assigned admission sequence of the step's sealed batch (P-037).</summary>
        public AdmissionSequence Admission { get; }

        /// <summary>True when this step sealed a batch (a step always does; recorded because it is normative).</summary>
        public bool SealedBatch { get; }

        /// <summary>Commands admitted into this step's sealed batch.</summary>
        public int AdmittedCommands { get; }

        /// <summary>The composition operation this step applies before its step, or <see cref="ReplayOperationKind.None"/>.</summary>
        public ReplayOperationKind Operation { get; }

        /// <summary>The operation's stable argument key (provider or target name); empty for a mode switch.</summary>
        public string OperationArgument { get; }

        /// <summary>Seed of the independent producers' start order for this step.</summary>
        public uint ProducerSeed { get; }

        /// <summary>Seed of the producers' completion order for this step.</summary>
        public uint CompletionSeed { get; }

        /// <summary>Worker count the step's producers are scheduled across (1, 2, 4 or the fixture maximum).</summary>
        public int Workers { get; }

        /// <summary>One-line description of the step, used in a failing assertion (TEST-022's addressability).</summary>
        public string Describe() =>
            "step " + Step.ToString(CultureInfo.InvariantCulture)
            + "(op=" + Operation + ":" + (OperationArgument.Length == 0 ? "-" : OperationArgument)
            + ", admitted=" + AdmittedCommands.ToString(CultureInfo.InvariantCulture)
            + ", admission=" + Admission.Value.ToString(CultureInfo.InvariantCulture)
            + ", workers=" + Workers.ToString(CultureInfo.InvariantCulture)
            + ", seeds=" + ProducerSeed.ToString(CultureInfo.InvariantCulture)
            + "/" + CompletionSeed.ToString(CultureInfo.InvariantCulture) + ")";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// A recorded run: the fixture shape, the step script and the expected canonical hashes per step. The trace is
    /// the *input* record; a run of the fixture against it produces a <see cref="ReplayRun"/> to compare with.
    /// </summary>
    public sealed class ReplayTrace
    {
        public ReplayTrace(
            string label,
            uint fixtureSeed,
            ReplayFixtureShape shape,
            IReadOnlyList<ReplayStepRecord>? steps,
            ReplayTraceHashes? expected)
        {
            Label = label ?? string.Empty;
            FixtureSeed = fixtureSeed;
            Shape = shape;
            Steps = steps == null ? Array.Empty<ReplayStepRecord>() : Freeze(steps);
            Expected = expected;
        }

        /// <summary>Diagnostic label, e.g. <c>integer-10000</c>. Never part of a hash.</summary>
        public string Label { get; }

        /// <summary>Seed the fixture composition and step script were generated from (TEST-008's record).</summary>
        public uint FixtureSeed { get; }

        /// <summary>The fixture shape this trace was recorded against; a shape change is a different trace.</summary>
        public ReplayFixtureShape Shape { get; }

        /// <summary>Recorded steps in admission order; the index is the logical step.</summary>
        public IReadOnlyList<ReplayStepRecord> Steps { get; }

        /// <summary>Committed expectations, or null when this record is the one being recorded.</summary>
        public ReplayTraceHashes? Expected { get; }

        /// <summary>Catalog/configuration identity of the record: a different one is a different input trace.</summary>
        public ContentHash CatalogHash => Shape.CatalogHash;

        /// <summary>Steps that change the composition; the rest must do no control-plane work (TEST-023).</summary>
        public int CompositionStepCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i].Operation != ReplayOperationKind.None)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Compact one-line audit text of the record (P-052).</summary>
        public string Describe() =>
            "replay-trace{" + (Label.Length == 0 ? "unlabelled" : Label)
            + ";seed=" + FixtureSeed.ToString(CultureInfo.InvariantCulture)
            + ";steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
            + ";compositionSteps=" + CompositionStepCount.ToString(CultureInfo.InvariantCulture)
            + ";shape=" + Shape.Describe()
            + ";catalog=" + CatalogHash.ToHex() + "}";

        public override string ToString() => Describe();

        private static IReadOnlyList<ReplayStepRecord> Freeze(IReadOnlyList<ReplayStepRecord> steps)
        {
            var copy = new ReplayStepRecord[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                copy[i] = steps[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>The committed canonical hashes of a whole run: one chain per observable family (TEST-022).</summary>
    public sealed class ReplayTraceHashes
    {
        public ReplayTraceHashes(
            ContentHash finalState,
            ContentHash finalDecisions,
            ContentHash finalProvenance,
            ContentHash eventIdentities,
            ContentHash stateChain,
            ContentHash counterChain)
        {
            FinalState = finalState;
            FinalDecisions = finalDecisions;
            FinalProvenance = finalProvenance;
            EventIdentities = eventIdentities;
            StateChain = stateChain;
            CounterChain = counterChain;
        }

        /// <summary>Canonical hash of the final effective state.</summary>
        public ContentHash FinalState { get; }

        /// <summary>Canonical hash of the final decision set.</summary>
        public ContentHash FinalDecisions { get; }

        /// <summary>Canonical hash of the final capability provenance.</summary>
        public ContentHash FinalProvenance { get; }

        /// <summary>Canonical hash of every committed event identity, in commit order.</summary>
        public ContentHash EventIdentities { get; }

        /// <summary>Chain hash over per-step state hashes: any divergence changes every later link.</summary>
        public ContentHash StateChain { get; }

        /// <summary>Chain hash over per-step telemetry counter frames (a cost trace, never a correctness input).</summary>
        public ContentHash CounterChain { get; }

        public string Describe() =>
            "hashes{state=" + FinalState.ToHex()
            + ";decisions=" + FinalDecisions.ToHex()
            + ";provenance=" + FinalProvenance.ToHex()
            + ";events=" + EventIdentities.ToHex()
            + ";stateChain=" + StateChain.ToHex()
            + ";counterChain=" + CounterChain.ToHex() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>The fixture's shape: every count and the catalog revision the record was generated under.</summary>
    public readonly struct ReplayFixtureShape
    {
        public ReplayFixtureShape(
            int branches,
            int targetsPerBranch,
            int isolatedTargets,
            int providersPerBranch,
            int steps,
            int maxWorkers,
            uint catalogRevision)
        {
            Branches = branches;
            TargetsPerBranch = targetsPerBranch;
            IsolatedTargets = isolatedTargets;
            ProvidersPerBranch = providersPerBranch;
            Steps = steps;
            MaxWorkers = maxWorkers;
            CatalogRevision = catalogRevision;
        }

        public int Branches { get; }

        public int TargetsPerBranch { get; }

        public int IsolatedTargets { get; }

        public int ProvidersPerBranch { get; }

        public int Steps { get; }

        /// <summary>The fixture's maximum worker count; TEST-022's "supported worker counts" upper bound.</summary>
        public int MaxWorkers { get; }

        /// <summary>Catalog/configuration revision of the fixture; changing it is a different input trace.</summary>
        public uint CatalogRevision { get; }

        /// <summary>Total targets the shape declares, isolated branch included.</summary>
        public int TargetCount => (Branches * TargetsPerBranch) + IsolatedTargets;

        /// <summary>Content hash of the shape: the "fixed catalog/configuration versions" of TEST-022.</summary>
        public ContentHash CatalogHash => ReplayStateHash.HashOf(Describe());

        public string Describe() =>
            "shape{branches=" + Branches.ToString(CultureInfo.InvariantCulture)
            + ";targetsPerBranch=" + TargetsPerBranch.ToString(CultureInfo.InvariantCulture)
            + ";isolated=" + IsolatedTargets.ToString(CultureInfo.InvariantCulture)
            + ";providersPerBranch=" + ProvidersPerBranch.ToString(CultureInfo.InvariantCulture)
            + ";steps=" + Steps.ToString(CultureInfo.InvariantCulture)
            + ";maxWorkers=" + MaxWorkers.ToString(CultureInfo.InvariantCulture)
            + ";catalogRevision=" + CatalogRevision.ToString(CultureInfo.InvariantCulture) + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Generates the integer fixture and its recorded step script. The generator is pure: the same
    /// <see cref="ReplayFixtureShape"/>, seed and worker schedule produce the same trace, byte for byte.
    /// </summary>
    public static class IntegerFixtureGenerator
    {
        /// <summary>The stable names the generator derives every identity from (never a numeric literal).</summary>
        public const string RootScope = "replay-root";
        public const string IsolatedScope = "replay-isolated";
        public const string BranchPrefix = "replay-branch-";
        public const string TargetPrefix = "replay-target-";
        public const string IsolatedTargetPrefix = "replay-target-iso-";
        public const string ProviderPrefix = "replay-provider-";
        public const string CounterCapability = "replay.counter";
        public const string WeightCapability = "replay.weight";
        public const string CounterSchema = "replay.counter-value";
        public const string WeightSchema = "replay.weight-value";
        public const string TargetRecipe = "replay.integer-target-recipe";
        public const string CounterReducer = "replay.reducer.int32-sum";
        public const string AlwaysPredicate = "replay.predicate.always";
        public const string CounterRuleSuffix = ".counter";
        public const string WeightRuleSuffix = ".weight";

        /// <summary>Counter contract stratum (0): the integer the rule stage integrates.</summary>
        public const int CounterStratum = 0;

        /// <summary>Weight contract stratum (1): derived from the finalized stratum-0 capability (P-021).</summary>
        public const int WeightStratum = 1;

        /// <summary>The fixture world identity; every run of the generator uses the same one (P-004 keeps sessions apart).</summary>
        public static WorldId FixtureWorld { get; } = new WorldId(FixtureIds.Id("gamecore.replay.world"));

        /// <summary>
        /// The 10,000-step shape TEST-022 names. The target counts are deliberately small: the sweep re-derives
        /// every one of those steps under four worker counts and a shuffled completion order, so the cost that
        /// matters is the per-step comparison count, not the size of one step's world.
        /// </summary>
        public static ReplayFixtureShape DefaultShape =>
            new ReplayFixtureShape(
                branches: 4,
                targetsPerBranch: 2,
                isolatedTargets: 2,
                providersPerBranch: 2,
                steps: 10000,
                maxWorkers: 8,
                catalogRevision: 1U);

        /// <summary>Builds the composition of one shape. Nothing here depends on the seed.</summary>
        public static FixtureBuilder Builder(ReplayFixtureShape shape)
        {
            FixtureBuilder builder = new FixtureBuilder(FixtureWorld).Scope(RootScope, null);
            for (int b = 0; b < shape.Branches; b++)
            {
                builder.Scope(Branch(b), RootScope);
            }

            builder.Scope(IsolatedScope, RootScope, isolateAllCapabilities: true);

            // Two capabilities, one per stratum: stratum 1's rule reads the finalized stratum-0 capability, which is
            // exactly the lower-stratum input P-021 allows and makes `StrataEvaluated` observably non-trivial.
            builder
                .Contract(CounterCapability, CounterStratum, new[]
                {
                    new FixtureSlot(CounterSchema, CompositionPolicy.Additive, reducer: FixtureIds.Key(CounterReducer)),
                })
                .Contract(WeightCapability, WeightStratum, new[]
                {
                    new FixtureSlot(WeightSchema, CompositionPolicy.Replace),
                });

            for (int b = 0; b < shape.Branches; b++)
            {
                for (int t = 0; t < shape.TargetsPerBranch; t++)
                {
                    builder.Target(TargetName(b, t), Branch(b), TargetRecipe);
                }
            }

            for (int i = 0; i < shape.IsolatedTargets; i++)
            {
                builder.Target(IsolatedTargetName(i), IsolatedScope, TargetRecipe);
            }

            return builder;
        }

        /// <summary>The value source one shape needs: the int32 sum reducer and the always predicate.</summary>
        public static FixtureValueSource ValueSource() =>
            new FixtureValueSource()
                .RegisterInt32Sum(CounterReducer)
                .RegisterAlwaysPredicate(AlwaysPredicate);

        /// <summary>
        /// The rules of one provider at a given integer payload: a stratum-0 additive counter and a stratum-1
        /// weight that is only eligible once that counter is final.
        /// </summary>
        public static IReadOnlyList<DerivationRule> ProviderRules(string providerName, int value) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    providerName + CounterRuleSuffix,
                    CounterCapability,
                    CounterStratum,
                    1U,
                    FixtureBuilder.Selector(TargetRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Additive,
                    FixturePayload.Int32(value)),
                FixtureBuilder.Rule(
                    providerName + WeightRuleSuffix,
                    WeightCapability,
                    WeightStratum,
                    1U,
                    FixtureBuilder.Selector(TargetRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    FixtureBuilder.Inputs(CounterCapability),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Int32(value % 7)),
            };

        /// <summary>Stable target name of one branch target.</summary>
        public static string TargetName(int branch, int index) =>
            TargetPrefix + branch.ToString(CultureInfo.InvariantCulture)
            + "-" + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>Stable name of one isolated-branch target.</summary>
        public static string IsolatedTargetName(int index) =>
            IsolatedTargetPrefix + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>Stable name of one branch scope.</summary>
        public static string Branch(int index) =>
            BranchPrefix + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>Stable name of one provider.</summary>
        public static string ProviderName(int branch, int index) =>
            ProviderPrefix + branch.ToString(CultureInfo.InvariantCulture)
            + "-" + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Generates the recorded step script of one trace: which step admits a command, which step performs a
        /// composition operation, and the producer/completion seeds and worker count of every step.
        /// </summary>
        public static ReplayTrace Generate(
            string label,
            uint fixtureSeed,
            ReplayFixtureShape shape,
            int providerStride = 997)
        {
            var steps = new ReplayStepRecord[shape.Steps];
            ulong admission = 0UL;
            for (int step = 0; step < shape.Steps; step++)
            {
                // Every step seals a batch (P-037): command-driven worlds advance only for admitted demand, and a
                // batch of zero is the "no demand" case the fixture keeps as an explicit step for the replay sweep.
                Lcg stepRandom = new Lcg(unchecked((fixtureSeed * 7919U) + (uint)step + 1U));
                int admitted = stepRandom.NextInclusive(0, 2);
                bool sealedBatch = true;
                admission += (ulong)admitted;
                ReplayOperationKind operation = ReplayOperationKind.None;
                string argument = string.Empty;
                if (step > 0 && step % providerStride == 0)
                {
                    int ordinal = step / providerStride;
                    int branch = ordinal % shape.Branches;
                    switch (ordinal % 4)
                    {
                        case 0:
                            operation = ReplayOperationKind.MountProvider;
                            argument = ProviderName(branch, ordinal % shape.ProvidersPerBranch);
                            break;
                        case 1:
                            operation = ReplayOperationKind.ReconfigureProvider;
                            argument = ProviderName(branch, ordinal % shape.ProvidersPerBranch);
                            break;
                        case 2:
                            operation = ReplayOperationKind.MoveTarget;
                            argument = TargetName(branch, ordinal % shape.TargetsPerBranch);
                            break;
                        default:
                            operation = ReplayOperationKind.SwitchMode;
                            break;
                    }
                }

                steps[step] = new ReplayStepRecord(
                    step,
                    new AdmissionSequence(admission),
                    sealedBatch,
                    admitted,
                    operation,
                    argument,
                    producerSeed: unchecked((fixtureSeed * 31U) + (uint)step),
                    completionSeed: unchecked((fixtureSeed * 131U) + (uint)step * 17U),
                    workers: WorkerSchedule.WorkersForStep(step, shape.MaxWorkers));
            }

            return new ReplayTrace(label, fixtureSeed, shape, steps, null);
        }
    }

    /// <summary>
    /// The fixture's integer rule stage: the pure function a step applies to the effective integer values. It is
    /// deliberately tiny and integral — the point of the fixture is that replay is compared, not that the rule is
    /// interesting (TEST-022: "a pure integer-rule fixture").
    /// </summary>
    public static class IntegerRuleStage
    {
        /// <summary>
        /// Applies one step of the integer rule to every target assembly, in canonical target order. The rule is:
        /// every active contribution's integer payload is added to the target's state, and a target whose capacity
        /// value would exceed the bound saturates rather than wrapping (an integer-only, exactly reproducible rule).
        /// </summary>
        public static int Apply(TargetAssembly assembly, int previousState, int bound)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            int state = previousState;
            for (int s = 0; s < assembly.Slots.Count; s++)
            {
                EffectiveSlot slot = assembly.Slots[s];
                for (int v = 0; v < slot.Values.Count; v++)
                {
                    if (FixturePayload.TryReadInt32(slot.Values[v], out int value))
                    {
                        state += value;
                    }
                }
            }

            if (bound > 0 && state > bound)
            {
                state = bound;
            }

            return state;
        }

        /// <summary>How many integer payloads one assembly contributes, so a silent skip is visible in a trace.</summary>
        public static int AppliedCount(TargetAssembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            int applied = 0;
            for (int s = 0; s < assembly.Slots.Count; s++)
            {
                applied += assembly.Slots[s].Values.Count;
            }

            return applied;
        }
    }

    /// <summary>
    /// The seeded worker schedule of a step: how many workers run the independent producers and the order their
    /// completions are observed in. TEST-022 requires the *same admitted input* replayed with a shuffled internal
    /// producer/completion order to produce identical policy results, so the shuffle may only reorder independent
    /// work, never the admitted order of the input itself.
    /// </summary>
    public static class WorkerSchedule
    {
        /// <summary>Worker counts TEST-022 names, plus the fixture maximum.</summary>
        public static IReadOnlyList<int> SupportedWorkerCounts(int maxWorkers)
        {
            List<int> counts = new List<int> { 1, 2, 4 };
            if (maxWorkers > 4)
            {
                counts.Add(maxWorkers);
            }

            return counts.AsReadOnly();
        }

        /// <summary>
        /// Worker count of one step: 1, 2, 4 or the fixture maximum, chosen deterministically from the step index so
        /// the recorded trace covers every supported count.
        /// </summary>
        public static int WorkersForStep(int step, int maxWorkers)
        {
            IReadOnlyList<int> counts = SupportedWorkerCounts(maxWorkers);
            return counts[step % counts.Count];
        }

        /// <summary>
        /// The start order of <paramref name="count"/> independent producers across <paramref name="workers"/>
        /// workers: a round-robin assignment whose per-worker lists are produced in order, flattened in the order
        /// the assigned workers were picked. It is a permutation of the producers, and it is the only thing a
        /// producer seed changes.
        /// </summary>
        public static int[] StartOrder(int count, int workers, uint seed)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var order = new int[count];
            if (count == 0)
            {
                return order;
            }

            int effectiveWorkers = workers < 1 ? 1 : workers;
            Lcg random = new Lcg(seed + 1U);
            var lanes = new List<int>[effectiveWorkers];
            for (int i = 0; i < effectiveWorkers; i++)
            {
                lanes[i] = new List<int>();
            }

            for (int i = 0; i < count; i++)
            {
                lanes[i % effectiveWorkers].Add(i);
            }

            int lane = random.Next(effectiveWorkers);
            int cursor = 0;
            for (int i = 0; i < effectiveWorkers; i++)
            {
                List<int> assigned = lanes[(lane + i) % effectiveWorkers];
                for (int j = 0; j < assigned.Count; j++)
                {
                    order[cursor] = assigned[j];
                    cursor++;
                }
            }

            return order;
        }

        /// <summary>
        /// The order independent producers' *completions* are observed in: a seeded permutation of
        /// <paramref name="count"/> items. Completion order may differ from start order, which is the scheduling
        /// freedom TEST-022 exercises.
        /// </summary>
        public static int[] CompletionOrder(int count, uint seed)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            Lcg random = new Lcg(seed + 7919U);
            for (int i = count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            return order;
        }
    }

    /// <summary>
    /// The fixture's linear congruential generator. Deliberately the same shape the derivation suite uses: a
    /// reproducible step is addressable from (seed, step) alone, with no dependency on the platform's RNG (P-008).
    /// </summary>
    public sealed class Lcg
    {
        private const uint Multiplier = 1664525U;
        private const uint Increment = 1013904223U;

        private uint state;

        public Lcg(uint seed)
        {
            state = seed == 0U ? 1U : seed;
        }

        /// <summary>Next raw 32-bit value.</summary>
        public uint NextUInt32()
        {
            state = unchecked((state * Multiplier) + Increment);
            return state;
        }

        /// <summary>Next value in <c>[0, bound)</c>; a non-positive bound yields zero.</summary>
        public int Next(int bound) => bound <= 1 ? 0 : (int)(NextUInt32() % (uint)bound);

        /// <summary>Next value in <c>[min, max]</c> inclusive.</summary>
        public int NextInclusive(int min, int max) => max <= min ? min : min + Next(max - min + 1);

        public bool NextBool() => (NextUInt32() & 1U) == 1U;
    }
}
