// GameCore.Derivation — the P-022 propagation budget and its cost counters (GC-006).
//
// `PropagationBudget` limits examined candidates, emitted contributions, affected targets, temporary bytes,
// wall-clock preparation deadline and the safe-point apply cost estimate. The default reference limits are the
// provisional guardrails named by P-022, never product capacity claims. Exceeding any hard limit yields
// `BudgetExceeded` with the top fan-out causes and no partial propagation publishes; a caller may configure a
// larger limit, but elapsed time never approves one (P-014, P-022).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Which hard limit of P-022 a derivation exceeded.</summary>
    public enum BudgetDimension
    {
        None = 0,
        ExaminedCandidates = 1,
        EmittedContributions = 2,
        AffectedTargets = 3,
        TemporaryBytes = 4,
        PreparationDeadline = 5,
        ApplyCostEstimate = 6,
    }

    /// <summary>
    /// Hard limits of one derivation (P-022). Defaults are the reference guardrails; every value must be
    /// positive because a zero limit would silently forbid a legal plan instead of reporting a budget.
    /// </summary>
    public sealed class PropagationBudget
    {
        public const long DefaultMaxExaminedCandidates = 1000000L;
        public const long DefaultMaxEmittedContributions = 250000L;
        public const long DefaultMaxAffectedTargets = 100000L;
        public const long DefaultMaxTemporaryBytes = 128L * 1024L * 1024L;
        public const long DefaultPreparationDeadlineMilliseconds = 2000L;
        public const long DefaultMaxApplyCostEstimateMicroseconds = 8000L;

        public PropagationBudget(
            long maxExaminedCandidates,
            long maxEmittedContributions,
            long maxAffectedTargets,
            long maxTemporaryBytes,
            long preparationDeadlineMilliseconds,
            long maxApplyCostEstimateMicroseconds)
        {
            RequirePositive(maxExaminedCandidates, nameof(maxExaminedCandidates));
            RequirePositive(maxEmittedContributions, nameof(maxEmittedContributions));
            RequirePositive(maxAffectedTargets, nameof(maxAffectedTargets));
            RequirePositive(maxTemporaryBytes, nameof(maxTemporaryBytes));
            RequirePositive(preparationDeadlineMilliseconds, nameof(preparationDeadlineMilliseconds));
            RequirePositive(maxApplyCostEstimateMicroseconds, nameof(maxApplyCostEstimateMicroseconds));

            MaxExaminedCandidates = maxExaminedCandidates;
            MaxEmittedContributions = maxEmittedContributions;
            MaxAffectedTargets = maxAffectedTargets;
            MaxTemporaryBytes = maxTemporaryBytes;
            PreparationDeadlineMilliseconds = preparationDeadlineMilliseconds;
            MaxApplyCostEstimateMicroseconds = maxApplyCostEstimateMicroseconds;
        }

        public static PropagationBudget Reference { get; } =
            new PropagationBudget(
                DefaultMaxExaminedCandidates,
                DefaultMaxEmittedContributions,
                DefaultMaxAffectedTargets,
                DefaultMaxTemporaryBytes,
                DefaultPreparationDeadlineMilliseconds,
                DefaultMaxApplyCostEstimateMicroseconds);

        public long MaxExaminedCandidates { get; }

        public long MaxEmittedContributions { get; }

        public long MaxAffectedTargets { get; }

        public long MaxTemporaryBytes { get; }

        public long PreparationDeadlineMilliseconds { get; }

        public long MaxApplyCostEstimateMicroseconds { get; }

        /// <summary>One byte-cost estimate for a candidate record; the temporary-storage accounting unit (P-022).</summary>
        public const long CandidateByteCost = 128L;

        /// <summary>One byte-cost estimate for an emitted contribution; the temporary-storage accounting unit (P-022).</summary>
        public const long ContributionByteCost = 256L;

        private static void RequirePositive(long value, string name)
        {
            if (value <= 0L)
            {
                throw new ArgumentOutOfRangeException(name, "A budget limit is positive (P-022).");
            }
        }

        public override string ToString() =>
            "PropagationBudget(candidates<=" + MaxExaminedCandidates
            + ", contributions<=" + MaxEmittedContributions
            + ", targets<=" + MaxAffectedTargets
            + ", bytes<=" + MaxTemporaryBytes
            + ", prepareMs<=" + PreparationDeadlineMilliseconds
            + ", applyUs<=" + MaxApplyCostEstimateMicroseconds + ")";
    }

    /// <summary>
    /// Live cost counters of one derivation: the P-022 accounting plus the P-023 index-visit evidence. Not sealed:
    /// `InvalidationCounters` extends it with the invalidation report so one derivation reports one counter object
    /// (GC-013).
    /// </summary>
    public class CostCounters
    {
        public int ExaminedCandidates { get; internal set; }

        public int EmittedContributions { get; internal set; }

        public int AffectedTargets { get; internal set; }

        public long TemporaryBytes { get; internal set; }

        public int RulesEvaluated { get; internal set; }

        /// <summary>
        /// Telemetry-only counters of this derivation (GC-023): the schema counters that are neither budget
        /// accounting nor provenance, so they must not change a decision or a diagnostic. They live in one
        /// <see cref="TelemetryCounterSet"/>, and every increment site is a
        /// <see cref="TelemetryCounting.Count(TelemetryCounterSet?, TelemetryCounter)"/> call, which the compiler
        /// removes — argument evaluation included — when `GAMECORE_TELEMETRY` is undefined (08 s3, TEST-023).
        /// </summary>
        public TelemetryCounterSet Telemetry { get; } = new TelemetryCounterSet();

        /// <summary>08 `StrataEvaluated`: capability strata this derivation iterated.</summary>
        public long StrataEvaluated => Telemetry.Get(TelemetryCounter.StrataEvaluated);

        /// <summary>
        /// 08 `ControlNodesVisited`: control-plane nodes this derivation examined. Zero in a build without
        /// `GAMECORE_TELEMETRY`, because the counting call sites do not survive compilation.
        /// </summary>
        public long ControlNodesVisited => Telemetry.Get(TelemetryCounter.ControlNodesVisited);

        /// <summary>Index buckets visited; the P-023 evidence that a local edit does not rescan untouched scopes.</summary>
        public int IndexBucketsVisited { get; internal set; }

        /// <summary>Targets visited through an index, including candidates rejected before emission.</summary>
        public int IndexTargetsVisited { get; internal set; }

        /// <summary>Contributions removed because another contribution lost or was excluded (provenance only).</summary>
        public int ShadowedCandidates { get; internal set; }

        /// <summary>False when the derivation stopped early on a hard limit; the result is then a rejection.</summary>
        public bool WithinBudget { get; internal set; } = true;

        /// <summary>Which limit stopped the derivation; <see cref="BudgetDimension.None"/> when it completed.</summary>
        public BudgetDimension ExceededDimension { get; internal set; }

        public long ExceededCount { get; internal set; }

        public long ExceededLimit { get; internal set; }

        /// <summary>Canonical, stable text used in diagnostics and evidence files.</summary>
        public virtual string Describe() =>
            "candidates=" + ExaminedCandidates
            + ";contributions=" + EmittedContributions
            + ";targets=" + AffectedTargets
            + ";bytes=" + TemporaryBytes
            + ";rules=" + RulesEvaluated
            + ";buckets=" + IndexBucketsVisited
            + ";visitedTargets=" + IndexTargetsVisited
            + ";shadowed=" + ShadowedCandidates;

        public IReadOnlyList<Id128> TopFanOutCauses { get; internal set; } = Array.Empty<Id128>();

        /// <summary>
        /// Writes this derivation's protocol counters and its telemetry-only counters into
        /// <paramref name="into"/> through the fixed compact schema (GC-023). Contribution add/retract counts are
        /// added by the result, which owns the delta.
        /// </summary>
        public virtual void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Merge(Telemetry);
            into.Add(TelemetryCounter.CandidatesMatched, ExaminedCandidates);
        }
    }
}
