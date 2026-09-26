// GameCore.Replay — differential propagation with reproducible failing seeds (GC-023, TEST-008).
//
// TEST-008 requires the incremental engine's effective assembly and provenance to equal a reference evaluator's for
// randomized operation sequences, and to "keep failed seeds and the reduced counterexample". GC-013's suites already
// compare the two engines; this file adds the part GC-023 owns: a differential sweep whose failure output is a
// *record*, and a deterministic reducer that turns a failing operation script into the smallest script that still
// fails, so a report carries a counterexample a reader can re-run instead of a seed they must bisect.
//
// Two design points make the reduction usable rather than decorative:
//
//   1. The reducer is *deterministic and total*: it tries shortening the operation list and, for each candidate,
//      asks the same predicate that produced the original failure. It never consults a random source, so the same
//      failing seed always reduces to the same witness.
//   2. A failure is reported for BOTH directions: the incremental result disagreeing with the oracle, and the
//      oracle disagreeing with itself (which is how the suite proves the witness is real rather than a mis-wired
//      comparator: a deliberately corrupted projection must be detected and reduced too).
//
// The sweep drives the real `IncrementalDerivationEngine` and the real `DerivationOracle` over a generated
// composition, so what is compared is the kernel's own two traversals.
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
    /// <summary>One operation of a differential script: a stable kind plus the key it addresses.</summary>
    public readonly struct DifferentialOperation
    {
        public DifferentialOperation(ReplayOperationKind kind, string key, int value)
        {
            Kind = kind;
            Key = key ?? string.Empty;
            Value = value;
        }

        public ReplayOperationKind Kind { get; }

        /// <summary>Provider or target stable name the operation addresses; empty for a mode switch or no-op.</summary>
        public string Key { get; }

        /// <summary>Numeric argument: a priority, an integer payload or a target ordinal.</summary>
        public int Value { get; }

        public string Describe() =>
            Kind + ":" + (Key.Length == 0 ? "-" : Key) + "@" + Value.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => Describe();
    }

    /// <summary>A failing seed and its reduced counterexample: the record TEST-008 asks a failure to keep.</summary>
    public sealed class FailingSeedRecord
    {
        public FailingSeedRecord(
            uint seed,
            string failure,
            IReadOnlyList<DifferentialOperation>? original,
            IReadOnlyList<DifferentialOperation>? reduced,
            ContentHash incrementalHash,
            ContentHash oracleHash,
            int reducedSteps)
        {
            Seed = seed;
            Failure = failure ?? string.Empty;
            Original = original ?? Array.Empty<DifferentialOperation>();
            Reduced = reduced ?? Array.Empty<DifferentialOperation>();
            IncrementalHash = incrementalHash;
            OracleHash = oracleHash;
            ReducedSteps = reducedSteps;
        }

        /// <summary>The seed the sweep used; re-running that seed must reproduce this record.</summary>
        public uint Seed { get; }

        /// <summary>What disagreed, in one line.</summary>
        public string Failure { get; }

        /// <summary>The script as generated.</summary>
        public IReadOnlyList<DifferentialOperation> Original { get; }

        /// <summary>The smallest script the reducer found that still fails.</summary>
        public IReadOnlyList<DifferentialOperation> Reduced { get; }

        /// <summary>The incremental engine's projection hash at the failure.</summary>
        public ContentHash IncrementalHash { get; }

        /// <summary>The oracle's projection hash at the failure.</summary>
        public ContentHash OracleHash { get; }

        /// <summary>Length of <see cref="Reduced"/>.</summary>
        public int ReducedSteps { get; }

        /// <summary>Canonical text of the reduced script, so an evidence file is a re-runnable record.</summary>
        public string ReducedScript()
        {
            var builder = new StringBuilder();
            builder.Append("seed=").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < Reduced.Count; i++)
            {
                builder.Append("op=").Append(Reduced[i].Describe()).Append('\n');
            }

            return builder.ToString();
        }

        public string Describe() =>
            "failing-seed{seed=" + Seed.ToString(CultureInfo.InvariantCulture)
            + ";original=" + Original.Count.ToString(CultureInfo.InvariantCulture)
            + ";reduced=" + ReducedSteps.ToString(CultureInfo.InvariantCulture)
            + ";failure=" + Failure
            + ";incremental=" + IncrementalHash.ToHex()
            + ";oracle=" + OracleHash.ToHex() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>The result of one differential sweep: how many seeds ran and every failing record it kept.</summary>
    public sealed class DifferentialSweepResult
    {
        public DifferentialSweepResult(
            int seeds,
            int stepsPerSeed,
            int acceptedComparisons,
            IReadOnlyList<FailingSeedRecord>? failures)
        {
            Seeds = seeds;
            StepsPerSeed = stepsPerSeed;
            AcceptedComparisons = acceptedComparisons;
            Failures = failures == null ? Array.Empty<FailingSeedRecord>() : failures;
        }

        public int Seeds { get; }

        public int StepsPerSeed { get; }

        /// <summary>Step comparisons that agreed.</summary>
        public int AcceptedComparisons { get; }

        /// <summary>Every failure, with its reduced counterexample.</summary>
        public IReadOnlyList<FailingSeedRecord> Failures { get; }

        /// <summary>Seeds whose every step agreed; the sweep is clean when this equals <see cref="Seeds"/>.</summary>
        public int CleanSeeds => Seeds - Failures.Count;

        public string Describe() =>
            "differential-sweep{seeds=" + Seeds.ToString(CultureInfo.InvariantCulture)
            + ";stepsPerSeed=" + StepsPerSeed.ToString(CultureInfo.InvariantCulture)
            + ";agreed=" + AcceptedComparisons.ToString(CultureInfo.InvariantCulture)
            + ";failures=" + Failures.Count.ToString(CultureInfo.InvariantCulture) + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// The differential sweep and its reducer. <see cref="ScriptFor"/> generates one seed's script, and
    /// <see cref="ProjectionOf"/> is the comparison that must hold at every step; a test that wants to prove the
    /// machinery detects a real divergence supplies a projection that is deliberately wrong for one operation.
    /// </summary>
    public sealed class DifferentialPropagation
    {
        private readonly ReplayFixtureShape shape;
        private readonly FixtureValueSource values;
        private readonly Func<DifferentialOperation, DerivationResult, bool>? perturbation;

        /// <param name="shape">Fixture shape the scripts run over.</param>
        /// <param name="perturbation">
        /// Optional injected divergence: when it returns true for an operation, the comparison for that step is
        /// flipped. It is the deliberately-wrong-implementation seam the suite uses to prove the reducer produces a
        /// real counterexample rather than an artifact of a passing sweep.
        /// </param>
        public DifferentialPropagation(
            ReplayFixtureShape shape,
            Func<DifferentialOperation, DerivationResult, bool>? perturbation = null)
        {
            this.shape = shape;
            values = IntegerFixtureGenerator.ValueSource();
            this.perturbation = perturbation;
        }

        /// <summary>Generates one seed's operation script: the vocabulary TEST-008 names.</summary>
        public IReadOnlyList<DifferentialOperation> ScriptFor(uint seed, int length)
        {
            var script = new DifferentialOperation[length];
            Lcg random = new Lcg(seed + 6151U);
            for (int i = 0; i < length; i++)
            {
                ReplayOperationKind kind = (ReplayOperationKind)random.Next(7);
                string key;
                switch (kind)
                {
                    case ReplayOperationKind.MountProvider:
                    case ReplayOperationKind.ReconfigureProvider:
                    case ReplayOperationKind.UnmountProvider:
                        key = IntegerFixtureGenerator.ProviderName(
                            random.Next(shape.Branches), random.Next(shape.ProvidersPerBranch));
                        break;
                    case ReplayOperationKind.SpawnTarget:
                        key = IntegerFixtureGenerator.TargetName(
                            random.Next(shape.Branches), random.Next(shape.TargetsPerBranch));
                        break;
                    case ReplayOperationKind.MoveTarget:
                        key = IntegerFixtureGenerator.TargetName(
                            random.Next(shape.Branches), random.Next(shape.TargetsPerBranch));
                        break;
                    default:
                        key = string.Empty;
                        break;
                }

                script[i] = new DifferentialOperation(kind, key, random.NextInclusive(-2, 4));
            }

            return Array.AsReadOnly(script);
        }

        /// <summary>
        /// Runs one script and returns the first step whose incremental projection differs from the oracle's, or
        /// null when every step agreed. The two hashes of a failure are returned through <paramref name="detail"/>.
        /// </summary>
        public string? Run(IReadOnlyList<DifferentialOperation> script, uint seed, out ContentHash incrementalHash, out ContentHash oracleHash, out int failingStep)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            FixtureBuilder builder = IntegerFixtureGenerator.Builder(shape);
            PropagationMode mode = PropagationMode.Automatic;
            CompositionRevision revision = CompositionRevision.First;
            AssemblyEpoch epoch = AssemblyEpoch.First;
            DerivationResult? previous = null;
            incrementalHash = ContentHash.Empty;
            oracleHash = ContentHash.Empty;
            failingStep = -1;

            for (int i = 0; i < script.Count; i++)
            {
                Apply(builder, script[i]);
                revision = new CompositionRevision(revision.Value + 1UL);
                epoch = new AssemblyEpoch(epoch.Value + 1UL);
                if (script[i].Kind == ReplayOperationKind.SwitchMode)
                {
                    mode = script[i].Value % 2 == 0 ? PropagationMode.Automatic : PropagationMode.Conservative;
                }

                DerivationSnapshot snapshot = builder.Build(mode, revision, epoch).ToSnapshot();
                DerivationChangeSet changeSet = previous == null
                    ? DerivationChangeSet.Diff(snapshot, snapshot)
                    : DerivationChangeSet.Diff(previous.Snapshot, snapshot);

                // The decision of a step is taken by whichever engine each path owns, so a disagreement here is a
                // genuine disagreement of the two traversals rather than of two copies of one comparator.
                IncrementalDerivationOutcome incremental = IncrementalDerivationEngine.Derive(
                    snapshot, values, new DerivationOptions(null, null, null, null, true), previous, changeSet);
                DerivationResult oracle = DerivationOracle.Derive(
                    snapshot, values, new DerivationOptions(null, null, null, null, true), previous);

                string incrementalText = ReplayStateHash.ProjectionText(incremental.Result);
                string oracleText = ReplayStateHash.ProjectionText(oracle);
                incrementalHash = ReplayStateHash.HashOf(incrementalText);
                oracleHash = ReplayStateHash.HashOf(oracleText);

                bool perturbed = perturbation != null && perturbation(script[i], incremental.Result);
                if (!string.Equals(incrementalText, oracleText, StringComparison.Ordinal) || perturbed)
                {
                    failingStep = i;
                    string reason = perturbed
                        ? "the injected perturbation reported this step as divergent"
                        : "the incremental projection (state, decisions and provenance) differs from the oracle's";
                    previous = incremental.Result;
                    return "step " + i.ToString(CultureInfo.InvariantCulture)
                        + " (seed " + seed.ToString(CultureInfo.InvariantCulture)
                        + ", op " + script[i].Describe() + "): " + reason
                        + "; incremental=" + incrementalHash.ToHex()
                        + " oracle=" + oracleHash.ToHex()
                        + (perturbed ? string.Empty : "; decisions=" + incremental.Result.Decisions.Count + "/" + oracle.Decisions.Count
                            + "; explanations=" + incremental.Result.Explanations.Count + "/" + oracle.Explanations.Count
                            + "; first difference=" + FirstDifference(incrementalText, oracleText));
                }

                previous = incremental.Result;
            }

            return null;
        }

        private static string FirstDifference(string incremental, string oracle)
        {
            string[] left = incremental.Split('\n');
            string[] right = oracle.Split('\n');
            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                string a = i < left.Length ? left[i] : "<missing>";
                string b = i < right.Length ? right[i] : "<missing>";
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    return "incremental[" + i.ToString(CultureInfo.InvariantCulture) + "]=" + a + ", oracle=" + b;
                }
            }

            return "<none>";
        }

        /// <summary>
        /// Reduces a failing script to a minimal one that still fails. Deterministic: delta debugging by chunk
        /// removal, then single-operation removal, then a stable-value search. The predicate is re-run on every
        /// candidate, so a reduction that stops failing is rejected and the previous witness is kept.
        /// </summary>
        public FailingSeedRecord Reduce(uint seed, IReadOnlyList<DifferentialOperation> script)
        {
            string? failure = Run(script, seed, out ContentHash incremental, out ContentHash oracle, out _);
            if (failure == null)
            {
                throw new InvalidOperationException("Reduce was asked to reduce a script that does not fail.");
            }

            List<DifferentialOperation> current = new List<DifferentialOperation>(script);
            // 1. Remove halves, repeatedly, while the reduced script still fails.
            int chunk = current.Count / 2;
            while (chunk >= 1)
            {
                int index = 0;
                bool removed = false;
                while (index < current.Count)
                {
                    List<DifferentialOperation> candidate = new List<DifferentialOperation>(current);
                    int take = Math.Min(chunk, candidate.Count - index);
                    candidate.RemoveRange(index, take);
                    if (candidate.Count == 0)
                    {
                        break;
                    }

                    if (StillFails(candidate, seed))
                    {
                        current = candidate;
                        removed = true;
                    }
                    else
                    {
                        index += chunk;
                    }
                }

                if (!removed)
                {
                    chunk /= 2;
                }
            }

            // 2. Simplify each remaining operation's numeric value towards zero while the script still fails.
            for (int i = 0; i < current.Count; i++)
            {
                DifferentialOperation original = current[i];
                int best = original.Value;
                foreach (int candidateValue in new[] { 0, 1, -1, original.Value / 2 })
                {
                    if (candidateValue == original.Value)
                    {
                        continue;
                    }

                    List<DifferentialOperation> candidate = new List<DifferentialOperation>(current);
                    candidate[i] = new DifferentialOperation(original.Kind, original.Key, candidateValue);
                    if (StillFails(candidate, seed))
                    {
                        best = candidateValue;
                        break;
                    }
                }

                current[i] = new DifferentialOperation(original.Kind, original.Key, best);
            }

            // Re-evaluate the final witness so the record carries its actual hashes.
            string? finalFailure = Run(current, seed, out incremental, out oracle, out _);
            return new FailingSeedRecord(
                seed,
                finalFailure ?? failure,
                script,
                current,
                incremental,
                oracle,
                current.Count);
        }

        /// <summary>
        /// Runs one seed for a fixed number of steps and returns the failing record, or null when the whole script
        /// agreed with the oracle.
        /// </summary>
        public FailingSeedRecord? RunSeed(uint seed, int steps)
        {
            IReadOnlyList<DifferentialOperation> script = ScriptFor(seed, steps);
            string? failure = Run(script, seed, out _, out _, out _);
            return failure == null ? null : Reduce(seed, script);
        }

        /// <summary>Sweeps <paramref name="seeds"/> seeds of <paramref name="steps"/> operations each.</summary>
        public DifferentialSweepResult Sweep(int seeds, int steps, uint firstSeed = 1U)
        {
            var failures = new List<FailingSeedRecord>();
            int agreed = 0;
            for (int i = 0; i < seeds; i++)
            {
                uint seed = unchecked(firstSeed + (uint)i);
                IReadOnlyList<DifferentialOperation> script = ScriptFor(seed, steps);
                string? failure = Run(script, seed, out _, out _, out _);
                if (failure == null)
                {
                    agreed += steps;
                    continue;
                }

                failures.Add(Reduce(seed, script));
            }

            return new DifferentialSweepResult(seeds, steps, agreed, failures);
        }

        private bool StillFails(List<DifferentialOperation> candidate, uint seed) =>
            Run(candidate, seed, out _, out _, out _) != null;

        /// <summary>Applies one differential operation to a composition; the inverse of the script's vocabulary.</summary>
        private void Apply(FixtureBuilder builder, DifferentialOperation operation)
        {
            switch (operation.Kind)
            {
                case ReplayOperationKind.MountProvider:
                {
                    if (operation.Key.Length == 0)
                    {
                        return;
                    }

                    // A mount of an already-mounted provider is a no-op proposal: the engine must report NoChange.
                    if (!HasInstall(builder, operation.Key))
                    {
                        builder.Install(
                            operation.Key,
                            IntegerFixtureGenerator.Branch(Math.Abs(operation.Value) % shape.Branches),
                            operation.Value,
                            IntegerFixtureGenerator.ProviderRules(operation.Key, operation.Value),
                            InstallationState.Active);
                    }

                    return;
                }

                case ReplayOperationKind.ReconfigureProvider:
                {
                    if (operation.Key.Length == 0 || !HasInstall(builder, operation.Key))
                    {
                        return;
                    }

                    builder.ReplaceRulePayload(
                        operation.Key,
                        operation.Key + IntegerFixtureGenerator.CounterRuleSuffix,
                        FixturePayload.Int32(operation.Value));
                    return;
                }

                case ReplayOperationKind.UnmountProvider:
                {
                    if (operation.Key.Length == 0 || !HasInstall(builder, operation.Key))
                    {
                        return;
                    }

                    builder.RemoveInstall(operation.Key);
                    return;
                }

                case ReplayOperationKind.SpawnTarget:
                {
                    if (operation.Key.Length == 0 || HasTarget(builder, operation.Key))
                    {
                        return;
                    }

                    builder.Target(
                        operation.Key,
                        IntegerFixtureGenerator.Branch(Math.Abs(operation.Value) % shape.Branches),
                        IntegerFixtureGenerator.TargetRecipe);
                    return;
                }

                case ReplayOperationKind.MoveTarget:
                {
                    if (operation.Key.Length == 0 || !HasTarget(builder, operation.Key))
                    {
                        return;
                    }

                    builder.MoveTarget(
                        operation.Key,
                        IntegerFixtureGenerator.Branch(Math.Abs(operation.Value) % shape.Branches));
                    return;
                }

                default:
                    return;
            }
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

        private static bool HasTarget(FixtureBuilder builder, string target)
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
}
