// GameCore.Benchmarks tests — the provisional budget table of 08 section 3 encoded as comparable rows (GC-026).
//
// The table in docs/game-core/08 is the only place a number is defined, so this suite pins the encoding of every row
// rather than the measurement: the row set, the join key a summary uses to find a row's number, the three-valued
// verdict, and — most importantly — that a run which produced *no* number for a row is NotMeasured rather than a
// pass. Two rows are report-only baselines: 08 asks for their number to be established, not met, and inventing a
// target for them would be exactly the unsupported claim 08 forbids.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class PerformanceBudgetTests
    {
        private static readonly string[] DeclaredRowIds =
        {
            PerformanceBudgets.ExecutionP95,
            PerformanceBudgets.ExecutionManagedBytes,
            PerformanceBudgets.UnchangedControlNodes,
            PerformanceBudgets.UnchangedServiceLookups,
            PerformanceBudgets.ApplyPauseP95,
            PerformanceBudgets.WholeWorldPreparationP95,
            PerformanceBudgets.SpawnBaseline,
            PerformanceBudgets.LifecyclePlateau,
            PerformanceBudgets.IdleSteps,
            PerformanceBudgets.IdleStageUpdates,
        };

        [Test]
        public void TheTenDeclaredRowsExistExactlyOnceEachInTableOrder()
        {
            Assert.That(PerformanceBudgets.All.Count, Is.EqualTo(10), "08's table has ten rows");
            Assert.That(DeclaredRowIds.Length, Is.EqualTo(10));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < DeclaredRowIds.Length; i++)
            {
                Assert.That(seen.Add(DeclaredRowIds[i]), Is.True, "row id " + DeclaredRowIds[i] + " is declared once");
                Assert.That(PerformanceBudgets.TryGet(DeclaredRowIds[i], out PerformanceBudget row), Is.True, "row " + DeclaredRowIds[i] + " exists");
                Assert.That(row, Is.Not.Null);
                Assert.That(row.Id, Is.EqualTo(DeclaredRowIds[i]));
            }

            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                Assert.That(
                    PerformanceBudgets.All[i].Id,
                    Is.EqualTo(DeclaredRowIds[i]),
                    "the row set follows 08's table order, so a report's section order is not incidental");
            }

            Assert.That(PerformanceBudgets.TryGet("budget.not-a-row", out PerformanceBudget missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(PerformanceBudgets.TryGet(string.Empty, out _), Is.False);
        }

        [Test]
        public void AnUnproducedMeasurementIsNotMeasuredAndNeverAPass()
        {
            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                Assert.That(
                    row.Verdict(double.NaN),
                    Is.EqualTo(BenchmarkBudgetVerdict.NotMeasured),
                    row.Id + ": a missing measurement is NotMeasured, never WithinTarget");
                Assert.That(
                    row.Verdict(double.PositiveInfinity) == BenchmarkBudgetVerdict.NotMeasured,
                    Is.False,
                    row.Id + ": only NaN means 'no number was produced'");
            }

            Assert.That(PerformanceBudgets.All.Count, Is.GreaterThan(0));
        }

        [Test]
        public void AMicrosecondRowIsWithinTargetAtTheTargetAndMissedAboveIt()
        {
            int compared = 0;
            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                if (row.ReportOnly || row.Unit != BenchmarkBudgetUnit.Microseconds)
                {
                    continue;
                }

                compared++;
                Assert.That(row.Target, Is.GreaterThan(0.0), row.Id + " declares a positive duration target");
                Assert.That(
                    row.Verdict(row.Target),
                    Is.EqualTo(BenchmarkBudgetVerdict.WithinTarget),
                    row.Id + ": exactly at the target is within it");
                Assert.That(
                    row.Verdict(row.Target - 1.0),
                    Is.EqualTo(BenchmarkBudgetVerdict.WithinTarget),
                    row.Id + ": under the target is within it");
                Assert.That(
                    row.Verdict(row.Target + 1e-12),
                    Is.EqualTo(BenchmarkBudgetVerdict.WithinTarget),
                    row.Id + ": the documented tolerance keeps an exactly-equal float within the target");
                Assert.That(
                    row.Verdict(row.Target + 1.0),
                    Is.EqualTo(BenchmarkBudgetVerdict.MissedTarget),
                    row.Id + ": one microsecond over the target is a miss");
                Assert.That(row.IsSatisfiedBy(row.Target), Is.True);
                Assert.That(row.IsSatisfiedBy(row.Target + 1.0), Is.False);
            }

            Assert.That(compared, Is.EqualTo(3), "three compared rows express a duration: execution p95, apply pause p95 and whole-world preparation p95");
        }

        [Test]
        public void AZeroTargetCountRowAcceptsOnlyZero()
        {
            int zeroRows = 0;
            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                if (row.ReportOnly || row.Target != 0.0)
                {
                    continue;
                }

                zeroRows++;
                Assert.That(
                    row.Unit,
                    Is.Not.EqualTo(BenchmarkBudgetUnit.Microseconds),
                    row.Id + ": a zero duration target would be a claim that a step takes no time at all");
                Assert.That(row.Verdict(0.0), Is.EqualTo(BenchmarkBudgetVerdict.WithinTarget), row.Id + ": zero measured is within target");
                Assert.That(row.Verdict(1.0), Is.EqualTo(BenchmarkBudgetVerdict.MissedTarget), row.Id + ": one occurrence is already over a zero quota");
                Assert.That(row.IsSatisfiedBy(0.0), Is.True);
                Assert.That(row.IsSatisfiedBy(0.0001), Is.False);
            }

            Assert.That(zeroRows, Is.EqualTo(5), "the zero-quota rows are the two steady-unchanged counters, idle steps, idle stage updates and managed bytes");
        }

        [Test]
        public void AReportOnlyRowSaysSoAndIsNeverAPassOrAMiss()
        {
            int reportOnly = 0;
            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                if (!row.ReportOnly)
                {
                    continue;
                }

                reportOnly++;
                Assert.That(row.Target, Is.EqualTo(0.0), row.Id + ": a report-only row invents no target");
                Assert.That(row.DescribeTarget(), Is.EqualTo("establish a baseline (no target)"));
                Assert.That(row.Verdict(123.0), Is.EqualTo(BenchmarkBudgetVerdict.ReportOnly), row.Id + ": a measured baseline is reported, not passed");
                Assert.That(row.Verdict(0.0), Is.EqualTo(BenchmarkBudgetVerdict.ReportOnly));
                Assert.That(row.IsSatisfiedBy(123.0), Is.False, row.Id + ": no number satisfies a row that has no target");
                Assert.That(row.IsSatisfiedBy(0.0), Is.False);
            }

            Assert.That(reportOnly, Is.EqualTo(2), "the spawn baseline and the lifecycle plateau are the report-only rows");
            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.SpawnBaseline, out PerformanceBudget spawn), Is.True);
            Assert.That(spawn.ReportOnly, Is.True);
            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.LifecyclePlateau, out PerformanceBudget lifecycle), Is.True);
            Assert.That(lifecycle.ReportOnly, Is.True);
        }

        [Test]
        public void EveryRowsJoinKeyNamesItsWorkloadPhaseAndMetric()
        {
            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                Assert.That(row.Id.Length, Is.GreaterThan(0));
                Assert.That(row.Metric.Length, Is.GreaterThan(0), row.Id + " names its measured quantity");
                Assert.That(row.Reference.Length, Is.GreaterThan(0), row.Id + " quotes the sentence of 08 it encodes");
                Assert.That(row.Included.Length, Is.GreaterThan(0), row.Id + " says what must be reported separately");
                Assert.That(Enum.IsDefined(typeof(BenchmarkPhase), row.Phase), Is.True, row.Id + " names a real phase");
                Assert.That(
                    row.MetricKey,
                    Is.EqualTo(row.WorkloadId + "/" + row.Phase.ToString() + "/" + row.Metric),
                    row.Id + ": the join key is workload/phase/metric, so a summary cannot match by position");
                Assert.That(
                    row.Describe().Contains(row.Id + ": " + row.WorkloadId + "/" + row.Phase.ToString(), StringComparison.Ordinal),
                    Is.True);
            }

            Assert.That(
                PerformanceBudgets.TryGet(PerformanceBudgets.ExecutionP95, out PerformanceBudget execution),
                Is.True);
            Assert.That(execution.MetricKey, Is.EqualTo("steady-execution-10000-targets/Step/p95"));
            Assert.That(execution.WorkloadId, Is.EqualTo(BenchmarkWorkloads.SteadyExecution));
            Assert.That(execution.Phase, Is.EqualTo(BenchmarkPhase.Step));
            Assert.That(execution.Metric, Is.EqualTo(BenchmarkMetrics.P95));
            Assert.That(execution.Target, Is.EqualTo(4000.0));
            Assert.That(execution.Unit, Is.EqualTo(BenchmarkBudgetUnit.Microseconds));
            Assert.That(execution.DescribeTarget(), Is.EqualTo("at most 4000 us"));

            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.WholeWorldPreparationP95, out PerformanceBudget whole), Is.True);
            Assert.That(whole.Phase, Is.EqualTo(BenchmarkPhase.Prepare));
            Assert.That(whole.Target, Is.EqualTo(100000.0));
            Assert.That(whole.DescribeTarget(), Is.EqualTo("at most 100000 us"));

            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.ApplyPauseP95, out PerformanceBudget apply), Is.True);
            Assert.That(apply.Phase, Is.EqualTo(BenchmarkPhase.Apply));
            Assert.That(apply.WorkloadId, Is.EqualTo(BenchmarkWorkloads.UpdateSizeHundred));
            Assert.That(apply.Target, Is.EqualTo(2000.0));

            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.LifecyclePlateau, out PerformanceBudget plateau), Is.True);
            Assert.That(plateau.Phase, Is.EqualTo(BenchmarkPhase.Change));
            Assert.That(plateau.Metric, Is.EqualTo(BenchmarkMetrics.P99));

            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.ExecutionManagedBytes, out PerformanceBudget bytes), Is.True);
            Assert.That(bytes.Unit, Is.EqualTo(BenchmarkBudgetUnit.Bytes));
            Assert.That(bytes.Metric, Is.EqualTo(BenchmarkMetrics.ManagedBytesPerStep));
            Assert.That(bytes.WorkloadId, Is.EqualTo(BenchmarkWorkloads.SteadyExecution));
            Assert.That(bytes.DescribeTarget(), Is.EqualTo("at most 0 bytes"));

            Assert.That(PerformanceBudgets.TryGet(PerformanceBudgets.IdleSteps, out PerformanceBudget idle), Is.True);
            Assert.That(idle.Unit, Is.EqualTo(BenchmarkBudgetUnit.Count));
            Assert.That(idle.DescribeTarget(), Is.EqualTo("exactly 0"));
            Assert.That(idle.WorkloadId, Is.EqualTo(BenchmarkWorkloads.IdleCommandWorld));
        }

        [Test]
        public void TheIdleRowsAreTheOnlyRowsOfTheIdleWorkloadAndEveryWorkloadIdIsReal()
        {
            IReadOnlyList<PerformanceBudget> idle = PerformanceBudgets.ForWorkload(BenchmarkWorkloads.IdleCommandWorld);
            Assert.That(idle.Count, Is.EqualTo(2), "08 asks for zero steps and zero simulation-stage updates");
            Assert.That(idle[0].Id, Is.EqualTo(PerformanceBudgets.IdleSteps), "and in that order");
            Assert.That(idle[1].Id, Is.EqualTo(PerformanceBudgets.IdleStageUpdates));
            Assert.That(idle[0].Metric, Is.EqualTo(BenchmarkMetrics.StepsAdvanced));
            Assert.That(idle[1].Metric, Is.EqualTo(BenchmarkMetrics.StageSampleCount));

            Assert.That(PerformanceBudgets.ForWorkload("not-a-workload").Count, Is.EqualTo(0));
            Assert.That(
                PerformanceBudgets.ForWorkload(BenchmarkWorkloads.SteadyUnchanged).Count,
                Is.EqualTo(2),
                "the two unchanged-composition counters are the rows of that workload");

            for (int i = 0; i < PerformanceBudgets.All.Count; i++)
            {
                PerformanceBudget row = PerformanceBudgets.All[i];
                Assert.That(
                    BenchmarkWorkloads.TryGet(row.WorkloadId, out BenchmarkWorkload workload),
                    Is.True,
                    row.Id + " addresses a workload that exists in the catalogue");
                Assert.That(workload, Is.Not.Null);
                Assert.That(
                    workload.Dimension.Length,
                    Is.GreaterThan(0),
                    row.Id + " addresses a workload with a declared dimension");
            }
        }

        [Test]
        public void JoinIdsNamesEveryRowInTableOrder()
        {
            string joined = PerformanceBudgets.JoinIds();

            for (int i = 0; i < DeclaredRowIds.Length; i++)
            {
                Assert.That(joined.Contains(DeclaredRowIds[i], StringComparison.Ordinal), Is.True, "JoinIds names " + DeclaredRowIds[i]);
            }

            Assert.That(joined, Is.EqualTo(string.Join(", ", DeclaredRowIds)), "JoinIds is the table order with a comma-space separator");
            Assert.That(CountOccurrences(joined, PerformanceBudgets.IdleSteps), Is.EqualTo(1), "no row id appears twice");
        }

        [Test]
        public void ARowRejectsAnEmptyIdAnEmptyMetricOrANegativeComparableTarget()
        {
            Assert.Throws<ArgumentException>(
                () => new PerformanceBudget(string.Empty, BenchmarkWorkloads.IdleCommandWorld, BenchmarkPhase.Step, BenchmarkMetrics.StepsAdvanced, 0.0, BenchmarkBudgetUnit.Count, "r", "i", false));
            Assert.Throws<ArgumentException>(
                () => new PerformanceBudget("budget.x", BenchmarkWorkloads.IdleCommandWorld, BenchmarkPhase.Step, string.Empty, 0.0, BenchmarkBudgetUnit.Count, "r", "i", false));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PerformanceBudget("budget.x", BenchmarkWorkloads.IdleCommandWorld, BenchmarkPhase.Step, BenchmarkMetrics.StepsAdvanced, -1.0, BenchmarkBudgetUnit.Count, "r", "i", false));

            var reportOnly = new PerformanceBudget(
                "budget.x",
                BenchmarkWorkloads.IdleCommandWorld,
                BenchmarkPhase.Step,
                BenchmarkMetrics.StepsAdvanced,
                0.0,
                BenchmarkBudgetUnit.Count,
                "r",
                "i",
                true);
            Assert.That(reportOnly.ReportOnly, Is.True);
            Assert.That(reportOnly.WorkloadId, Is.EqualTo(BenchmarkWorkloads.IdleCommandWorld));
            Assert.That(reportOnly.Describe().Contains("budget.x", StringComparison.Ordinal), Is.True);

            var unnamedWorkload = new PerformanceBudget(
                "budget.y",
                null!,
                BenchmarkPhase.Step,
                BenchmarkMetrics.StepsAdvanced,
                0.0,
                BenchmarkBudgetUnit.Count,
                null!,
                null!,
                false);
            Assert.That(unnamedWorkload.WorkloadId, Is.EqualTo(string.Empty), "a null workload id becomes empty text rather than a crash");
            Assert.That(unnamedWorkload.Reference, Is.EqualTo(string.Empty));
            Assert.That(unnamedWorkload.Included, Is.EqualTo(string.Empty));
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                int found = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }

                count++;
                index = found + needle.Length;
            }
        }
    }
}
