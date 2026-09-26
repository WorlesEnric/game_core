// GameCore.Benchmarks — the provisional budgets of 08 section 3 as comparable rows.
//
// Normative source: docs/game-core/08-validation-and-performance.md, "Provisional budgets and failure decisions".
// The table there is the only place a number is defined, and its own preface is part of the contract:
//
//   "The following numbers are initial engineering targets, not measured results or universal shipping requirements.
//    Apply them on the recorded baseline machine; retain the measurements if later product evidence justifies revising
//    a target. A quota in the protocol is a correctness bound and remains mandatory even when a performance target
//    changes."
//
// So this file encodes the table verbatim as *rows*, and a row's verdict has three possible values rather than two:
// a row whose metric the run did not produce is NotMeasured, never "within". Two rows are report-only baselines: 08
// asks them to "establish a baseline" or to show a plateau, and there is no number to compare against, so inventing
// one here would be exactly the kind of unsupported claim 08 forbids.
//
// A quota in 00 (P-022's candidate/contribution/target/byte counts, the apply-cost estimate) is NOT a row here: it is
// a correctness bound enforced by the derivation engine itself and reported with the fixture configuration, and a
// performance revision must never move it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Benchmarks
{
    /// <summary>The unit a budget target is expressed in; the metric name always says which quantity it is.</summary>
    public enum BenchmarkBudgetUnit
    {
        Microseconds = 0,
        Bytes = 1,
        Count = 2,
    }

    /// <summary>How one measured number stands against one provisional target.</summary>
    public enum BenchmarkBudgetVerdict
    {
        /// <summary>The run produced no number for this row, so the row is not a pass.</summary>
        NotMeasured = 0,

        /// <summary>A measurement was produced and it is at or under the target (or exactly the required count).</summary>
        WithinTarget = 1,

        /// <summary>A measurement was produced and it exceeds the target; a budget decision record must explain it.</summary>
        MissedTarget = 2,

        /// <summary>A report-only row: 08 asks for the number to be established, not for it to meet a target.</summary>
        ReportOnly = 3,
    }

    /// <summary>
    /// One row of the 08 budget table, addressed by the workload, the phase and the metric the runner reports. The
    /// join key is <see cref="MetricKey"/>, built from those three, so a summary can never compare a number to the
    /// wrong row by position.
    /// </summary>
    public sealed class PerformanceBudget
    {
        public PerformanceBudget(
            string id,
            string workloadId,
            BenchmarkPhase phase,
            string metric,
            double target,
            BenchmarkBudgetUnit unit,
            string reference,
            string included,
            bool reportOnly)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A budget row needs a stable id.", nameof(id));
            }

            if (string.IsNullOrEmpty(metric))
            {
                throw new ArgumentException("A budget row needs a metric name.", nameof(metric));
            }

            if (!reportOnly && target < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(target),
                    "A comparable budget target is never negative; a report-only row says so explicitly.");
            }

            Id = id;
            WorkloadId = workloadId ?? string.Empty;
            Phase = phase;
            Metric = metric;
            Target = target;
            Unit = unit;
            Reference = reference ?? string.Empty;
            Included = included ?? string.Empty;
            ReportOnly = reportOnly;
        }

        /// <summary>Stable row id, quoted by the budget decision record.</summary>
        public string Id { get; }

        public string WorkloadId { get; }

        public BenchmarkPhase Phase { get; }

        /// <summary>Name of the measured quantity, e.g. <c>p95</c> or <c>control-nodes-visited</c>.</summary>
        public string Metric { get; }

        /// <summary>Provisional target; zero for a report-only row.</summary>
        public double Target { get; }

        public BenchmarkBudgetUnit Unit { get; }

        /// <summary>The sentence of 08 that defines this row, so a reader can check the encoding.</summary>
        public string Reference { get; }

        /// <summary>What 08 requires to be reported separately from this number.</summary>
        public string Included { get; }

        /// <summary>True for a row 08 asks to be established rather than met.</summary>
        public bool ReportOnly { get; }

        /// <summary>The machine-readable join key a summary uses to find this row's measurement.</summary>
        public string MetricKey => WorkloadId + "/" + Phase.ToString() + "/" + Metric;

        /// <summary>True when <paramref name="measured"/> satisfies this row.</summary>
        public bool IsSatisfiedBy(double measured) =>
            !ReportOnly && measured <= Target + 1e-9;

        /// <summary>The verdict of one measurement; NaN means the run produced no number for this row.</summary>
        public BenchmarkBudgetVerdict Verdict(double measured)
        {
            if (double.IsNaN(measured))
            {
                return BenchmarkBudgetVerdict.NotMeasured;
            }

            if (ReportOnly)
            {
                return BenchmarkBudgetVerdict.ReportOnly;
            }

            return IsSatisfiedBy(measured)
                ? BenchmarkBudgetVerdict.WithinTarget
                : BenchmarkBudgetVerdict.MissedTarget;
        }

        /// <summary>The target as text, with its unit and its provisional status spelled out.</summary>
        public string DescribeTarget()
        {
            if (ReportOnly)
            {
                return "establish a baseline (no target)";
            }

            switch (Unit)
            {
                case BenchmarkBudgetUnit.Microseconds:
                    return "at most " + Target.ToString("0.###", CultureInfo.InvariantCulture) + " us";

                case BenchmarkBudgetUnit.Bytes:
                    return "at most " + Target.ToString("0.###", CultureInfo.InvariantCulture) + " bytes";

                default:
                    return "exactly " + Target.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        public string Describe() =>
            Id + ": " + WorkloadId + "/" + Phase.ToString() + "/" + Metric + " " + DescribeTarget();

        public override string ToString() => Describe();
    }

    /// <summary>Metrics the runner reports and the budget rows compare against, as constants.</summary>
    public static class BenchmarkMetrics
    {
        /// <summary>p50 of the phase's per-sample durations.</summary>
        public const string P50 = "p50";

        /// <summary>p95 of the phase's per-sample durations.</summary>
        public const string P95 = "p95";

        /// <summary>p99 of the phase's per-sample durations.</summary>
        public const string P99 = "p99";

        /// <summary>Largest sample of the phase.</summary>
        public const string Max = "max";

        /// <summary>Mean of the phase's samples.</summary>
        public const string Mean = "mean";

        /// <summary>Number of samples behind the phase's distribution.</summary>
        public const string Count = "count";

        /// <summary>Managed bytes allocated per committed logical step, measured across every thread.</summary>
        public const string ManagedBytesPerStep = "managed-bytes-per-step";

        /// <summary>08 `ControlNodesVisited` delta over the workload's window.</summary>
        public const string ControlNodesVisited = "control-nodes-visited";

        /// <summary>08 `ServiceStringLookups` delta over the workload's window.</summary>
        public const string ServiceStringLookups = "service-string-lookups";

        /// <summary>08 `StepsAdvanced` delta over the workload's window.</summary>
        public const string StepsAdvanced = "steps-advanced";

        /// <summary>
        /// Simulation-stage updates observed over the workload's window. The value is the schema's own wire name for
        /// `TelemetryCounter.StageSampleCount`, so a budget row keyed on it joins straight to the counter the raw
        /// document carries rather than to a second spelling of the same quantity.
        /// </summary>
        public const string StageSampleCount = "stage-samples";

        /// <summary>08 `ContributionsAdded` delta over the workload's window.</summary>
        public const string ContributionsAdded = "contributions-added";

        /// <summary>08 `ContributionsRetracted` delta over the workload's window.</summary>
        public const string ContributionsRetracted = "contributions-retracted";

        /// <summary>08 `CandidatesMatched` delta over the workload's window.</summary>
        public const string CandidatesMatched = "candidates-matched";

        /// <summary>08 `PlanPreparedBytes` delta over the workload's window.</summary>
        public const string PlanPreparedBytes = "plan-prepared-bytes";

        /// <summary>Targets whose effective assembly changed, as reported by the derivation delta.</summary>
        public const string AffectedTargets = "affected-targets";

        /// <summary>True when the last derivation of the workload published no change (P-006 `NoChange`).</summary>
        public const string NoChange = "no-change";

        /// <summary><c>0</c> when the incremental path ran, <c>1</c> when the world was legitimately recomputed.</summary>
        public const string WholeWorldRecompute = "whole-world-recompute";
    }

    /// <summary>The budget table of 08 section 3, row for row.</summary>
    public static class PerformanceBudgets
    {
        /// <summary>Row ids, quoted by the budget decision record.</summary>
        public const string ExecutionP95 = "budget.execution-p95";

        public const string ExecutionManagedBytes = "budget.execution-managed-bytes";

        public const string UnchangedControlNodes = "budget.unchanged-control-nodes";

        public const string UnchangedServiceLookups = "budget.unchanged-service-lookups";

        public const string ApplyPauseP95 = "budget.apply-pause-p95";
        public const string WholeWorldPreparationP95 = "budget.whole-world-preparation-p95";


        public const string SpawnBaseline = "budget.spawn-baseline";

        public const string LifecyclePlateau = "budget.lifecycle-plateau";

        public const string IdleSteps = "budget.idle-steps";

        public const string IdleStageUpdates = "budget.idle-stage-updates";

        /// <summary>Every row, in the order 08's table lists them.</summary>
        public static readonly IReadOnlyList<PerformanceBudget> All = Array.AsReadOnly(new[]
        {
            new PerformanceBudget(
                ExecutionP95,
                BenchmarkWorkloads.SteadyExecution,
                BenchmarkPhase.Step,
                BenchmarkMetrics.P95,
                4000.0,
                BenchmarkBudgetUnit.Microseconds,
                "10,000 integer-rule targets, 1,000 active commands per fixed step: Core execution p95 at most 4 ms.",
                "Scheduling, queries, request arbitration, commit; report native physics/render/audio separately.",
                false),
            new PerformanceBudget(
                ExecutionManagedBytes,
                BenchmarkWorkloads.SteadyExecution,
                BenchmarkPhase.Step,
                BenchmarkMetrics.ManagedBytesPerStep,
                0.0,
                BenchmarkBudgetUnit.Bytes,
                "Stable execution after warmup: 0 managed bytes per logical step in the kernel hot path.",
                "Include worker work; report explicitly enabled diagnostics or plugin allocations separately.",
                false),
            new PerformanceBudget(
                UnchangedControlNodes,
                BenchmarkWorkloads.SteadyUnchanged,
                BenchmarkPhase.Step,
                BenchmarkMetrics.ControlNodesVisited,
                0.0,
                BenchmarkBudgetUnit.Count,
                "No composition changes over 10,000 steps: 0 control-tree visits.",
                "Counters count control work even if a traversal is cached or returns no matches.",
                false),
            new PerformanceBudget(
                UnchangedServiceLookups,
                BenchmarkWorkloads.SteadyUnchanged,
                BenchmarkPhase.Step,
                BenchmarkMetrics.ServiceStringLookups,
                0.0,
                BenchmarkBudgetUnit.Count,
                "No composition changes over 10,000 steps: 0 string service resolutions.",
                "Counters count control work even if a traversal is cached or returns no matches.",
                false),
            new PerformanceBudget(
                ApplyPauseP95,
                BenchmarkWorkloads.UpdateSizeHundred,
                BenchmarkPhase.Apply,
                BenchmarkMetrics.P95,
                2000.0,
                BenchmarkBudgetUnit.Microseconds,
                "Valid plan affecting 100 existing targets: Apply pause p95 at most 2 ms.",
                "Include fencing time in a separate mandatory wait metric; report preparation and end-to-end latency.",
                false),
            new PerformanceBudget(
                WholeWorldPreparationP95,
                BenchmarkWorkloads.UpdateSizeWhole,
                BenchmarkPhase.Prepare,
                BenchmarkMetrics.P95,
                100000.0,
                BenchmarkBudgetUnit.Microseconds,
                "Whole-world derivation for 10,000 targets: Preparation p95 at most 100 ms.",
                "Full closure/index work; no partial publication to meet the number.",
                false),
            new PerformanceBudget(
                SpawnBaseline,
                BenchmarkWorkloads.SpawnThousand,
                BenchmarkPhase.Prepare,
                BenchmarkMetrics.P95,
                0.0,
                BenchmarkBudgetUnit.Microseconds,
                "1,000-target spawn under active capabilities: report prepare/apply p95, native/managed bytes and "
                + "recipe reuse; establish a baseline.",
                "No throughput claim until the actual archetype/state footprint is measured.",
                true),
            new PerformanceBudget(
                LifecyclePlateau,
                BenchmarkWorkloads.LifecycleCycles,
                BenchmarkPhase.Change,
                BenchmarkMetrics.P99,
                0.0,
                BenchmarkBudgetUnit.Microseconds,
                "1,000 lifecycle cycles with fixed retained data: active counts return to baseline; bounded "
                + "cache/native/managed growth plateaus.",
                "Report asset policy, retained events and quarantine separately.",
                true),
            new PerformanceBudget(
                IdleSteps,
                BenchmarkWorkloads.IdleCommandWorld,
                BenchmarkPhase.Step,
                BenchmarkMetrics.StepsAdvanced,
                0.0,
                BenchmarkBudgetUnit.Count,
                "Idle command-driven world: 0 simulation steps.",
                "Host presentation and pending control-plane operations may still run.",
                false),
            new PerformanceBudget(
                IdleStageUpdates,
                BenchmarkWorkloads.IdleCommandWorld,
                BenchmarkPhase.Step,
                BenchmarkMetrics.StageSampleCount,
                0.0,
                BenchmarkBudgetUnit.Count,
                "Idle command-driven world: 0 simulation-stage updates.",
                "Host presentation and pending control-plane operations may still run.",
                false),
        });

        /// <summary>Finds a row by its id.</summary>
        public static bool TryGet(string id, out PerformanceBudget budget)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal))
                {
                    budget = All[i];
                    return true;
                }
            }

            budget = null!;
            return false;
        }

        /// <summary>The rows that address one workload, in table order.</summary>
        public static IReadOnlyList<PerformanceBudget> ForWorkload(string workloadId)
        {
            var rows = new List<PerformanceBudget>();
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].WorkloadId, workloadId, StringComparison.Ordinal))
                {
                    rows.Add(All[i]);
                }
            }

            return rows;
        }

        /// <summary>Every row id, comma separated; the budget decision record's section order.</summary>
        public static string JoinIds()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < All.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(", ");
                }

                builder.Append(All[i].Id);
            }

            return builder.ToString();
        }
    }
}
