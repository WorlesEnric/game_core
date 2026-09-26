// GameCore.Benchmarks tests — the sample record, the allocation tracker and the memory categories (GC-026).
//
// The allocation side is the one place where a number can be *structurally* unable to prove its claim: a counter read
// on one thread cannot prove a worker allocated nothing. 08 s3 says "a main-thread-only reading cannot prove worker
// allocation is zero", so this suite pins that the tracker reports the coverage it actually observed rather than the
// coverage the workload requested, and that it keeps counting a declared thread as unattributed until that thread
// reports a reading of its own.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkSampleTests
    {
        [Test]
        public void AOneThreadTrackerIsIncompleteUntilThatThreadReports()
        {
            BenchmarkAllocationTracker tracker =
                BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.AllThreadsSampled);

            Assert.That(tracker.DeclaredThreads, Is.EqualTo(1));
            Assert.That(tracker.SampledThreads, Is.EqualTo(0));
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(1));
            Assert.That(tracker.IsThreadComplete, Is.False, "nothing was observed yet, so no coverage can be claimed");
            Assert.That(tracker.Coverage, Is.EqualTo(BenchmarkThreadCoverage.CallingThreadOnly));
            Assert.That(tracker.RequestedCoverage, Is.EqualTo(BenchmarkThreadCoverage.AllThreadsSampled), "the request is the workload's claim");

            tracker.SampleCallingThread("main");

            Assert.That(tracker.SampledThreads, Is.EqualTo(1));
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(0));
            Assert.That(tracker.IsThreadComplete, Is.True);
            Assert.That(tracker.Coverage, Is.EqualTo(BenchmarkThreadCoverage.AllThreadsSampled));
            Assert.That(tracker.TotalBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.That(tracker.PerStep(1000L), Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        public void ADeclaredWorkerThatNeverReportedKeepsTheReadingPartial()
        {
            BenchmarkAllocationTracker tracker =
                BenchmarkAllocationTracker.Start(2, BenchmarkThreadCoverage.AllThreadsSampled);
            tracker.SampleCallingThread("main");

            Assert.That(tracker.DeclaredThreads, Is.EqualTo(2));
            Assert.That(tracker.SampledThreads, Is.EqualTo(1));
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(1), "the second declared thread never reported");
            Assert.That(tracker.IsThreadComplete, Is.False, "a one-thread reading must not claim a thread-complete zero");
            Assert.That(tracker.Coverage, Is.EqualTo(BenchmarkThreadCoverage.CallingThreadOnly));
            Assert.That(
                tracker.RequestedCoverage,
                Is.EqualTo(BenchmarkThreadCoverage.AllThreadsSampled),
                "the request stays the workload's declared need while Coverage reports the observation");

            Assert.That(tracker.Describe().Contains("declaredThreads=2", StringComparison.Ordinal), Is.True);
            Assert.That(tracker.Describe().Contains("sampledThreads=1", StringComparison.Ordinal), Is.True);
            Assert.That(tracker.Describe().Contains("threadComplete=false", StringComparison.Ordinal), Is.True);
            Assert.That(tracker.Describe().Contains("coverage=CallingThreadOnly", StringComparison.Ordinal), Is.True);

            tracker.SampleReportedThread("worker-1", 4096L);

            Assert.That(tracker.SampledThreads, Is.EqualTo(2));
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(0));
            Assert.That(tracker.IsThreadComplete, Is.True, "only a worker's own reading can complete the coverage");
            Assert.That(tracker.Coverage, Is.EqualTo(BenchmarkThreadCoverage.AllThreadsSampled));
            Assert.That(tracker.TotalBytes, Is.GreaterThanOrEqualTo(4096L), "the worker's reported bytes are counted");
            Assert.That(tracker.PerStep(4096L), Is.GreaterThan(0.0));
        }

        [Test]
        public void SamplingTheSameThreadTwiceKeepsOneReadingPerLabel()
        {
            BenchmarkAllocationTracker tracker =
                BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.AllThreadsSampled);
            tracker.SampleCallingThread("main");
            long first = tracker.TotalBytes;

            tracker.SampleCallingThread("main");

            Assert.That(tracker.SampledThreads, Is.EqualTo(1), "a label is one thread, not one sample");
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(0));
            Assert.That(tracker.IsThreadComplete, Is.True);
            Assert.That(tracker.TotalBytes, Is.GreaterThanOrEqualTo(first), "the later reading supersedes the earlier one");

            BenchmarkAllocationTracker reported =
                BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.CallingThreadOnly);
            reported.SampleReportedThread("worker-1", 10L);
            reported.SampleReportedThread("worker-1", 20L);
            Assert.That(reported.SampledThreads, Is.EqualTo(1), "a reported label is also idempotent");
            Assert.That(reported.TotalBytes, Is.EqualTo(20L), "the last reported reading is the one kept");
            Assert.That(reported.IsThreadComplete, Is.True);

            BenchmarkAllocationTracker over = BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.AllThreadsSampled);
            over.SampleReportedThread("a", 1L);
            over.SampleReportedThread("b", 2L);
            Assert.That(over.SampledThreads, Is.EqualTo(2), "a second label is a second reading");
            Assert.That(over.UnattributedThreads, Is.EqualTo(0), "unattributed threads never count below zero");
            Assert.That(over.IsThreadComplete, Is.True);
        }

        [Test]
        public void BytesPerStepIsNaNWithoutACommittedStep()
        {
            BenchmarkAllocationTracker tracker =
                BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.CallingThreadOnly);
            tracker.SampleReportedThread("worker-1", 100L);

            Assert.That(double.IsNaN(tracker.PerStep(0L)), Is.True, "no step means no per-step number, never a division by zero");
            Assert.That(double.IsNaN(tracker.PerStep(-4L)), Is.True);
            Assert.That(tracker.PerStep(100L), Is.EqualTo(1.0));
            Assert.That(tracker.PerStep(50L), Is.EqualTo(2.0));
            Assert.That(tracker.TotalBytes, Is.EqualTo(100L));
            Assert.That(tracker.Coverage, Is.EqualTo(BenchmarkThreadCoverage.AllThreadsSampled), "one declared thread reported");
        }

        [Test]
        public void TheTrackerRejectsNonsenseInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BenchmarkAllocationTracker.Start(0, BenchmarkThreadCoverage.AllThreadsSampled));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BenchmarkAllocationTracker.Start(-2, BenchmarkThreadCoverage.CallingThreadOnly));

            BenchmarkAllocationTracker tracker =
                BenchmarkAllocationTracker.Start(1, BenchmarkThreadCoverage.CallingThreadOnly);
            Assert.Throws<ArgumentException>(() => tracker.SampleCallingThread(string.Empty));
            Assert.Throws<ArgumentException>(() => tracker.SampleCallingThread(null!));
            Assert.Throws<ArgumentException>(() => tracker.SampleReportedThread(string.Empty, 1L));
            Assert.Throws<ArgumentException>(() => tracker.SampleReportedThread(null!, 1L));
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.SampleReportedThread("worker-1", -1L));

            tracker.SampleReportedThread("worker-1", 0L);
            Assert.That(tracker.IsThreadComplete, Is.True, "an honest zero from every declared thread is a complete reading");
            Assert.That(tracker.TotalBytes, Is.EqualTo(0L));
            Assert.That(tracker.UnattributedThreads, Is.EqualTo(0));
        }

        [Test]
        public void BenchmarksSampleKeepsItsDeclaredPhaseOrdinalDurationAndCounterDelta()
        {
            var counters = new TelemetryCounterSet();
            counters.Set(TelemetryCounter.ControlNodesVisited, 12L);
            var sample = new BenchmarkSample(3, BenchmarkPhase.Apply, 250L, counters);

            Assert.That(sample.Ordinal, Is.EqualTo(3));
            Assert.That(sample.Phase, Is.EqualTo(BenchmarkPhase.Apply));
            Assert.That(sample.Microseconds, Is.EqualTo(250L));
            Assert.That(sample.Counters, Is.SameAs(counters), "a sample keeps the exact counter delta it observed");
            Assert.That(sample.ToString().Contains("Apply#3=250us", StringComparison.Ordinal), Is.True);

            var timeOnly = new BenchmarkSample(0, BenchmarkPhase.Step, 0L, null);
            Assert.That(timeOnly.Counters, Is.Null, "null means the sample measured time only, not a zero delta");

            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkSample(-1, BenchmarkPhase.Step, 1L, null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BenchmarkSample(0, BenchmarkPhase.Step, -1L, null),
                "a negative duration is a clock or accounting defect");
        }

        [Test]
        public void MemoryCategoriesCopyEveryOwnerReportedCounterAndTheCallerSuppliedReadings()
        {
            var counters = new TelemetryCounterSet();
            counters.Set(TelemetryCounter.LeaseBytes, 11L);
            counters.Set(TelemetryCounter.RetainedEventBytes, 12L);
            counters.Set(TelemetryCounter.RetainedEventCount, 13L);
            counters.Set(TelemetryCounter.CacheBytes, 14L);
            counters.Set(TelemetryCounter.CacheEntries, 15L);
            counters.Set(TelemetryCounter.QuarantineBytes, 16L);
            counters.Set(TelemetryCounter.QuarantineEntries, 17L);
            counters.Set(TelemetryCounter.LiveLeases, 18L);
            counters.Set(TelemetryCounter.OutstandingCallbacks, 19L);

            BenchmarkMemoryCategories memory = BenchmarkMemoryCategories.FromCounters(
                counters,
                nativeContainerBytes: 21L,
                managedAllocatedBytes: 22L,
                managedThreadComplete: true,
                managedHeapStartBytes: 23L,
                managedHeapEndBytes: 24L);

            Assert.That(memory.NativeContainerBytes, Is.EqualTo(21L), "the world's own ledger number is the one passed in");
            Assert.That(memory.ManagedAllocatedBytes, Is.EqualTo(22L));
            Assert.That(memory.ManagedAllocationIsThreadComplete, Is.True);
            Assert.That(memory.ManagedHeapStartBytes, Is.EqualTo(23L));
            Assert.That(memory.ManagedHeapBytes, Is.EqualTo(24L));
            Assert.That(memory.ManagedHeapGrowth, Is.EqualTo(1L), "growth is the end reading minus the start reading");

            Assert.That(memory.LeaseBytes, Is.EqualTo(11L));
            Assert.That(memory.RetainedEventBytes, Is.EqualTo(12L));
            Assert.That(memory.RetainedEventCount, Is.EqualTo(13L));
            Assert.That(memory.CacheBytes, Is.EqualTo(14L));
            Assert.That(memory.CacheEntries, Is.EqualTo(15L));
            Assert.That(memory.QuarantineBytes, Is.EqualTo(16L));
            Assert.That(memory.QuarantineEntries, Is.EqualTo(17L));
            Assert.That(memory.LiveLeases, Is.EqualTo(18L));
            Assert.That(memory.OutstandingCallbacks, Is.EqualTo(19L));

            Assert.That(memory.Describe().Contains("managedHeapGrowth=1", StringComparison.Ordinal), Is.True);
            Assert.That(memory.DescribeSplit().Contains("quarantine=", StringComparison.Ordinal), Is.True);
            Assert.That(memory.DescribeSplit().Contains("retained events=", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void AnEmptyCounterSetReportsEveryCategoryAsZeroRatherThanGuessing()
        {
            BenchmarkMemoryCategories memory = BenchmarkMemoryCategories.FromCounters(
                new TelemetryCounterSet(),
                nativeContainerBytes: 0L,
                managedAllocatedBytes: 0L,
                managedThreadComplete: false,
                managedHeapStartBytes: 5L,
                managedHeapEndBytes: 5L);

            Assert.That(memory.LeaseBytes, Is.EqualTo(0L));
            Assert.That(memory.RetainedEventBytes, Is.EqualTo(0L));
            Assert.That(memory.RetainedEventCount, Is.EqualTo(0L));
            Assert.That(memory.CacheBytes, Is.EqualTo(0L));
            Assert.That(memory.CacheEntries, Is.EqualTo(0L));
            Assert.That(memory.QuarantineBytes, Is.EqualTo(0L));
            Assert.That(memory.QuarantineEntries, Is.EqualTo(0L));
            Assert.That(memory.LiveLeases, Is.EqualTo(0L));
            Assert.That(memory.OutstandingCallbacks, Is.EqualTo(0L));
            Assert.That(memory.NativeContainerBytes, Is.EqualTo(0L));
            Assert.That(memory.ManagedHeapGrowth, Is.EqualTo(0L));
            Assert.That(
                memory.ManagedAllocationIsThreadComplete,
                Is.False,
                "an incomplete allocation reading is reported as incomplete, not as a zero allocation");

            Assert.Throws<ArgumentNullException>(
                () => BenchmarkMemoryCategories.FromCounters(null!, 0L, 0L, false, 0L, 0L));
        }

        [Test]
        public void TheRunDocumentCannotPassWithoutGatesAndWithoutRunningWhatItDeclared()
        {
            BenchmarkRunDocument document = Document(BenchmarkWorkloadKind.Change, repetitionsRequested: 5);
            Assert.That(document.Passed, Is.False, "a document with no gates is not a pass");

            document.Add(new BenchmarkGateResult("budget.apply-pause-p95", true, "p95=100us"));
            Assert.That(
                document.Passed,
                Is.False,
                "every gate passed but the run executed 0 of the 5 repetitions it declared, so it is not a pass");

            document.RepetitionsExecuted = 5;
            Assert.That(document.Passed, Is.True, "every gate passed and the run executed what it declared");

            document.Add(new BenchmarkGateResult("gate.affected-targets", false, "affected=2; expected=1"));
            Assert.That(document.Passed, Is.False, "one failed gate fails the workload");

            BenchmarkRunDocument shortRun = Document(BenchmarkWorkloadKind.Change, repetitionsRequested: 5);
            shortRun.Add(new BenchmarkGateResult("budget.apply-pause-p95", true, "p95=100us"));
            shortRun.RepetitionsExecuted = 4;
            Assert.That(shortRun.Passed, Is.False, "executing fewer repetitions than declared is not a pass");
            shortRun.RepetitionsExecuted = 5;
            Assert.That(shortRun.Passed, Is.True);
            shortRun.RepetitionsExecuted = 6;
            Assert.That(shortRun.Passed, Is.True, "running more than declared does not make the declaration false");
        }

        [Test]
        public void TheRunDocumentCountsOnePhaseAndKeepsEverySample()
        {
            BenchmarkRunDocument document = Document(BenchmarkWorkloadKind.Change, repetitionsRequested: 3);
            document.Add(new BenchmarkSample(0, BenchmarkPhase.Step, 100L, null));
            document.Add(new BenchmarkSample(1, BenchmarkPhase.Step, 200L, null));
            document.Add(new BenchmarkSample(0, BenchmarkPhase.Apply, 300L, null));
            document.Add(new BenchmarkSample(2, BenchmarkPhase.Step, 400L, null));

            Assert.That(document.Samples.Count, Is.EqualTo(4));
            Assert.That(document.DurationsOf(BenchmarkPhase.Step), Is.EqualTo(new List<long> { 100L, 200L, 400L }));
            Assert.That(document.DurationsOf(BenchmarkPhase.Apply), Is.EqualTo(new List<long> { 300L }));
            Assert.That(document.DurationsOf(BenchmarkPhase.Wait).Count, Is.EqualTo(0));
            Assert.That(document.DurationsOf(BenchmarkPhase.EndToEnd).Count, Is.EqualTo(0));
            Assert.That(document.Samples[0].Phase, Is.EqualTo(BenchmarkPhase.Step));
            Assert.That(document.Samples[0].Microseconds, Is.EqualTo(100L));

            Assert.Throws<ArgumentNullException>(() => document.Add((BenchmarkSample)null!));
            Assert.Throws<ArgumentNullException>(() => document.Add((BenchmarkGateResult)null!));
        }

        [Test]
        public void TheRunDocumentRecordsItsConfigurationNotesGatesAndTotals()
        {
            BenchmarkRunDocument document = Document(BenchmarkWorkloadKind.Change, repetitionsRequested: 3);

            Assert.That(document.WorkloadId, Is.EqualTo("update-size-1"));
            Assert.That(document.Kind, Is.EqualTo(BenchmarkWorkloadKind.Change));
            Assert.That(document.Dimension, Is.EqualTo("update-size"));
            Assert.That(document.Scopes, Is.EqualTo(1000));
            Assert.That(document.Targets, Is.EqualTo(10000));
            Assert.That(document.WarmupSeconds, Is.EqualTo(30));
            Assert.That(document.Seed, Is.EqualTo(BenchmarkWorkloads.DefaultSeed));
            Assert.That(document.RunOrdinal, Is.EqualTo(1));
            Assert.That(document.RepetitionsRequested, Is.EqualTo(3));
            Assert.That(document.RepetitionsExecuted, Is.EqualTo(0));
            Assert.That(document.Totals.IsZero, Is.True, "a fresh document reports no counters rather than a fabricated total");
            Assert.That(document.Memory.ManagedAllocationIsThreadComplete, Is.False, "a fresh document claims no coverage");
            Assert.That(document.Memory.ManagedHeapGrowth, Is.EqualTo(0L));

            document.Note("updateSize=1;repetition=0");
            document.Note(string.Empty);
            document.Note(null!);
            Assert.That(document.Notes.Count, Is.EqualTo(1), "an empty note is dropped rather than written as a blank line");
            Assert.That(document.Notes[0], Is.EqualTo("updateSize=1;repetition=0"));

            document.Add(new BenchmarkGateResult("gate.affected-targets", true, "affected=1"));
            Assert.That(document.Gates.Count, Is.EqualTo(1));
            Assert.That(document.Gates[0].Name, Is.EqualTo("gate.affected-targets"));
            Assert.That(document.Gates[0].Passed, Is.True);
            Assert.That(document.Gates[0].Detail, Is.EqualTo("affected=1"));
            Assert.That(document.Gates[0].ToString().StartsWith("pass ", StringComparison.Ordinal), Is.True);
            Assert.That(
                new BenchmarkGateResult("g", false, string.Empty).ToString().StartsWith("FAIL ", StringComparison.Ordinal),
                Is.True);

            var totals = new TelemetryCounterSet();
            totals.Set(TelemetryCounter.ControlNodesVisited, 7L);
            document.SetTotals(totals);
            Assert.That(document.Totals.Get(TelemetryCounter.ControlNodesVisited), Is.EqualTo(7L));
            Assert.Throws<ArgumentNullException>(() => document.SetTotals(null!));
            Assert.Throws<ArgumentNullException>(() => new BenchmarkGateResult(null!, true, "x"));

            document.StepsAdvanced = 1234L;
            document.WindowMicroseconds = 120000000L;
            Assert.That(document.Describe().Contains("update-size-1", StringComparison.Ordinal), Is.True);
            Assert.That(document.Describe().Contains("passed=false", StringComparison.Ordinal), Is.True);
        }

        private static BenchmarkRunDocument Document(BenchmarkWorkloadKind kind, int repetitionsRequested) =>
            new BenchmarkRunDocument(
                "update-size-1",
                kind,
                "update-size",
                1000,
                10000,
                0,
                30,
                repetitionsRequested,
                BenchmarkWorkloads.DefaultSeed,
                1);
    }
}
