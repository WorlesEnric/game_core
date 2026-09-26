// GameCore.Benchmarks — the sample taxonomy and the raw per-sample record of the 08 measurement method.
//
// Normative source: docs/game-core/08-validation-and-performance.md section 3. Three rules from that section shape
// this file, and each one is a deliberate refusal:
//
//   * "Collect per-sample p50/p95/p99/max and total counts, not only averages or FPS." A run therefore keeps every
//     sample, and the distribution is computed from the samples rather than from a running average that cannot be
//     re-analysed.
//   * "Time end-to-end changes separately from apply time so off-thread preparation cannot hide an excessive
//     user-visible delay." Preparation, the mandatory safety-boundary wait, the apply pause and the end-to-end
//     latency are four separate phases of one change, never one number.
//   * "Memory reports distinguish managed heap, native containers, retained catalogs/caches, asset leases, events and
//     quarantined work." The memory categories are read from the owners that hold the bytes (GC-023's counters)
//     instead of from a process total, because 08 says an aggregate process number cannot attribute a leak.
//
// The allocation side is the one place where a number can be *structurally* unable to prove its claim: a counter read
// on one thread cannot prove a worker allocated nothing. `BenchmarkAllocationTracker` therefore records how many
// threads it actually sampled and refuses to report a thread-complete zero it did not observe.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Benchmarks
{
    /// <summary>
    /// Which part of a workload's work one sample describes. 08 separates these so off-thread preparation cannot hide
    /// a user-visible delay; the enum is the seam that keeps them apart.
    /// </summary>
    public enum BenchmarkPhase
    {
        /// <summary>Initialization/compilation/load work before the measured window (never a budget row).</summary>
        Warmup = 0,

        /// <summary>Off-thread or caller-side preparation of one change (derivation, closure, plan build).</summary>
        Prepare = 1,

        /// <summary>The mandatory safety-boundary wait of one change: fencing, drain and dependency waits.</summary>
        Wait = 2,

        /// <summary>The fenced apply pause of one change: the interval the world is not simulating.</summary>
        Apply = 3,

        /// <summary>The whole change from proposal to published assembly, measured by the caller.</summary>
        EndToEnd = 4,

        /// <summary>One simulation step or one idle pump of a steady workload.</summary>
        Step = 5,

        /// <summary>One whole repetition of a repeated change, excluding the callers' own fixture construction.</summary>
        Change = 6,
    }

    /// <summary>How many threads a workload's allocation reading actually covers.</summary>
    public enum BenchmarkThreadCoverage
    {
        /// <summary>Every thread that executed any of the workload's work was sampled by this tracker.</summary>
        AllThreadsSampled = 0,

        /// <summary>Only the calling thread was sampled; a worker-thread allocation would be invisible.</summary>
        CallingThreadOnly = 1,
    }

    /// <summary>
    /// One measurement sample: an ordinal, the phase it belongs to, its duration in microseconds and the counter
    /// delta the sample observed (null when the sample measured time only).
    /// </summary>
    public sealed class BenchmarkSample
    {
        public BenchmarkSample(int ordinal, BenchmarkPhase phase, long microseconds, TelemetryCounterSet? counters)
        {
            if (ordinal < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            }

            if (microseconds < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(microseconds),
                    "A duration sample is never negative; a negative duration is a clock or accounting defect.");
            }

            Ordinal = ordinal;
            Phase = phase;
            Microseconds = microseconds;
            Counters = counters;
        }

        public int Ordinal { get; }

        public BenchmarkPhase Phase { get; }

        /// <summary>Observed duration in microseconds, from the host clock the runner supplied.</summary>
        public long Microseconds { get; }

        /// <summary>Counter delta of this sample, or null when the sample measured time only.</summary>
        public TelemetryCounterSet? Counters { get; }

        public override string ToString() =>
            Phase.ToString() + "#" + Ordinal.ToString(CultureInfo.InvariantCulture) + "="
            + Microseconds.ToString(CultureInfo.InvariantCulture) + "us";
    }

    /// <summary>
    /// Counts managed bytes allocated by the threads it was told to cover. It is not a profiler: it reads
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> from the threads that report to it, and it records how many
    /// threads did so, because a reading that covers one thread of two proves nothing about the other (08 s3:
    /// "a main-thread-only reading cannot prove worker allocation is zero").
    /// </summary>
    public sealed class BenchmarkAllocationTracker
    {
        private readonly Dictionary<string, long> reported = new Dictionary<string, long>(StringComparer.Ordinal);
        private long callingThreadStart;
        private int declaredThreads;
        private int unattributedThreads;

        private BenchmarkAllocationTracker()
        {
        }

        /// <summary>
        /// Coverage the tracker can honestly claim: <see cref="BenchmarkThreadCoverage.AllThreadsSampled"/> only when
        /// every declared thread reported, and <see cref="BenchmarkThreadCoverage.CallingThreadOnly"/> otherwise.
        /// </summary>
        public BenchmarkThreadCoverage Coverage => IsThreadComplete
            ? BenchmarkThreadCoverage.AllThreadsSampled
            : BenchmarkThreadCoverage.CallingThreadOnly;

        /// <summary>Coverage the workload declared it would need; the observed value is <see cref="Coverage"/>.</summary>
        public BenchmarkThreadCoverage RequestedCoverage { get; private set; }

        /// <summary>Threads that were expected to execute workload work.</summary>
        public int DeclaredThreads => declaredThreads;

        /// <summary>Threads that actually reported a reading; fewer than <see cref="DeclaredThreads"/> means partial.</summary>
        public int SampledThreads => reported.Count;

        /// <summary>
        /// Starts a tracker for a workload whose work runs on <paramref name="declaredThreads"/> threads. The
        /// requested coverage is recorded as the workload's claim; <see cref="Coverage"/> reports what the tracker
        /// actually observed, so the document can never state a coverage the run did not have.
        /// </summary>
        public static BenchmarkAllocationTracker Start(int declaredThreads, BenchmarkThreadCoverage requested)
        {
            if (declaredThreads <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(declaredThreads));
            }

            return new BenchmarkAllocationTracker
            {
                declaredThreads = declaredThreads,
                unattributedThreads = declaredThreads,
                RequestedCoverage = requested,
                callingThreadStart = GC.GetAllocatedBytesForCurrentThread(),
            };
        }

        /// <summary>
        /// Records the calling thread's allocation since the tracker started under <paramref name="label"/>.
        /// Idempotent per label, so a caller may sample the same thread at several points and keep the last reading.
        /// </summary>
        public void SampleCallingThread(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                throw new ArgumentException("A sampled thread needs a label.", nameof(label));
            }

            long delta = GC.GetAllocatedBytesForCurrentThread() - callingThreadStart;
            if (delta < 0L)
            {
                delta = 0L;
            }

            if (reported.ContainsKey(label))
            {
                reported[label] = delta;
            }
            else
            {
                reported.Add(label, delta);
                unattributedThreads--;
            }
        }

        /// <summary>
        /// Records a reading a worker thread took of itself. This is the only way a worker's allocation can enter the
        /// total: <see cref="GC.GetAllocatedBytesForCurrentThread"/> cannot be read from another thread, so a runner
        /// that does not receive a worker's own reading must not report a thread-complete zero.
        /// </summary>
        public void SampleReportedThread(string label, long allocatedBytes)
        {
            if (string.IsNullOrEmpty(label))
            {
                throw new ArgumentException("A sampled thread needs a label.", nameof(label));
            }

            if (allocatedBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
            }

            if (reported.ContainsKey(label))
            {
                reported[label] = allocatedBytes;
            }
            else
            {
                reported.Add(label, allocatedBytes);
                unattributedThreads--;
            }
        }

        /// <summary>Threads this tracker was told about but never received a reading from.</summary>
        public int UnattributedThreads => unattributedThreads < 0 ? 0 : unattributedThreads;

        /// <summary>Total managed bytes reported by every sampled thread.</summary>
        public long TotalBytes
        {
            get
            {
                long total = 0L;
                foreach (KeyValuePair<string, long> entry in reported)
                {
                    total += entry.Value;
                }

                return total;
            }
        }

        /// <summary>True when the reading covers every declared thread; only then can a zero be asserted.</summary>
        public bool IsThreadComplete => UnattributedThreads == 0;

        /// <summary>Managed bytes per committed step, or NaN when no step was committed.</summary>
        public double PerStep(long committedSteps) =>
            committedSteps <= 0L ? double.NaN : (double)TotalBytes / committedSteps;

        public string Describe() =>
            "managedBytes=" + TotalBytes.ToString(CultureInfo.InvariantCulture)
            + ";declaredThreads=" + DeclaredThreads.ToString(CultureInfo.InvariantCulture)
            + ";sampledThreads=" + SampledThreads.ToString(CultureInfo.InvariantCulture)
            + ";threadComplete=" + (IsThreadComplete ? "true" : "false")
            + ";coverage=" + Coverage.ToString();
    }

    /// <summary>
    /// The memory categories 08 requires to be reported separately. Every byte count comes from an owner that holds
    /// the bytes (GC-023's counters), never from a process total: 08 says an aggregate process number cannot attribute
    /// a leak, and a category that is inferred from a total is not evidence.
    /// </summary>
    public sealed class BenchmarkMemoryCategories
    {
        private BenchmarkMemoryCategories()
        {
        }

        /// <summary>Managed heap size observed at the end of the workload's window, in bytes.</summary>
        public long ManagedHeapBytes { get; private set; }

        /// <summary>Managed heap size observed before the workload's window, in bytes.</summary>
        public long ManagedHeapStartBytes { get; private set; }

        /// <summary>Managed bytes allocated by the covered threads over the window (see the allocation tracker).</summary>
        public long ManagedAllocatedBytes { get; private set; }

        /// <summary>True when the managed allocation reading covers every declared thread.</summary>
        public bool ManagedAllocationIsThreadComplete { get; private set; }

        /// <summary>Native storage the world's own resource ledger holds (its declared containers and buffers).</summary>
        public long NativeContainerBytes { get; private set; }

        /// <summary>Bytes held by live staged/asset leases.</summary>
        public long LeaseBytes { get; private set; }

        /// <summary>Bytes held by retained committed events, and how many events that is.</summary>
        public long RetainedEventBytes { get; private set; }

        public long RetainedEventCount { get; private set; }

        /// <summary>Bytes held by retained derived caches, and how many entries that is.</summary>
        public long CacheBytes { get; private set; }

        public long CacheEntries { get; private set; }

        /// <summary>Bytes quarantined because unfinished work still reaches them (P-048).</summary>
        public long QuarantineBytes { get; private set; }

        public long QuarantineEntries { get; private set; }

        public long LiveLeases { get; private set; }

        public long OutstandingCallbacks { get; private set; }

        /// <summary>
        /// Reads the categories out of one aggregate counter set. The native-container number and the two heap
        /// readings are supplied by the caller: they come from the world's own resource ledger and from the runtime
        /// rather than from the compact schema, and 08 wants them next to the owner-reported categories rather than
        /// merged into one process total.
        /// </summary>
        public static BenchmarkMemoryCategories FromCounters(
            TelemetryCounterSet counters,
            long nativeContainerBytes,
            long managedAllocatedBytes,
            bool managedThreadComplete,
            long managedHeapStartBytes,
            long managedHeapEndBytes)
        {
            if (counters == null)
            {
                throw new ArgumentNullException(nameof(counters));
            }

            return new BenchmarkMemoryCategories
            {
                ManagedAllocatedBytes = managedAllocatedBytes,
                ManagedAllocationIsThreadComplete = managedThreadComplete,
                ManagedHeapStartBytes = managedHeapStartBytes,
                ManagedHeapBytes = managedHeapEndBytes,
                NativeContainerBytes = nativeContainerBytes,
                LeaseBytes = counters.Get(TelemetryCounter.LeaseBytes),
                RetainedEventBytes = counters.Get(TelemetryCounter.RetainedEventBytes),
                RetainedEventCount = counters.Get(TelemetryCounter.RetainedEventCount),
                CacheBytes = counters.Get(TelemetryCounter.CacheBytes),
                CacheEntries = counters.Get(TelemetryCounter.CacheEntries),
                QuarantineBytes = counters.Get(TelemetryCounter.QuarantineBytes),
                QuarantineEntries = counters.Get(TelemetryCounter.QuarantineEntries),
                LiveLeases = counters.Get(TelemetryCounter.LiveLeases),
                OutstandingCallbacks = counters.Get(TelemetryCounter.OutstandingCallbacks),
            };
        }

        /// <summary>Observed heap growth over the window; a process total, reported as an observation only.</summary>
        public long ManagedHeapGrowth => ManagedHeapBytes - ManagedHeapStartBytes;

        public string Describe() =>
            "managedHeapStart=" + ManagedHeapStartBytes.ToString(CultureInfo.InvariantCulture)
            + ";managedHeapEnd=" + ManagedHeapBytes.ToString(CultureInfo.InvariantCulture)
            + ";managedHeapGrowth=" + ManagedHeapGrowth.ToString(CultureInfo.InvariantCulture)
            + ";managedAllocated=" + ManagedAllocatedBytes.ToString(CultureInfo.InvariantCulture)
            + ";managedThreadComplete=" + (ManagedAllocationIsThreadComplete ? "true" : "false")
            + ";nativeContainers=" + NativeContainerBytes.ToString(CultureInfo.InvariantCulture)
            + ";leases=" + LeaseBytes.ToString(CultureInfo.InvariantCulture)
            + ";events=" + RetainedEventBytes.ToString(CultureInfo.InvariantCulture)
            + ";events_n=" + RetainedEventCount.ToString(CultureInfo.InvariantCulture)
            + ";cache=" + CacheBytes.ToString(CultureInfo.InvariantCulture)
            + ";cache_n=" + CacheEntries.ToString(CultureInfo.InvariantCulture)
            + ";quarantine=" + QuarantineBytes.ToString(CultureInfo.InvariantCulture)
            + ";quarantine_n=" + QuarantineEntries.ToString(CultureInfo.InvariantCulture)
            + ";liveLeases=" + LiveLeases.ToString(CultureInfo.InvariantCulture)
            + ";outstandingCallbacks=" + OutstandingCallbacks.ToString(CultureInfo.InvariantCulture);

        /// <summary>The split as one line per category, for the summary's memory section.</summary>
        public string DescribeSplit()
        {
            var text = new StringBuilder();
            text.Append("managed heap (process total, observation only) start=").Append(ManagedHeapStartBytes.ToString(CultureInfo.InvariantCulture)).Append(" B end=").Append(ManagedHeapBytes.ToString(CultureInfo.InvariantCulture)).Append(" B");
            text.Append(" | managed allocated (covered threads)=").Append(ManagedAllocatedBytes.ToString(CultureInfo.InvariantCulture)).Append(" B");
            text.Append(" | native containers=").Append(NativeContainerBytes.ToString(CultureInfo.InvariantCulture)).Append(" B");
            text.Append(" | leases=").Append(LeaseBytes.ToString(CultureInfo.InvariantCulture)).Append(" B");
            text.Append(" | retained events=").Append(RetainedEventBytes.ToString(CultureInfo.InvariantCulture)).Append(" B/")
                .Append(RetainedEventCount.ToString(CultureInfo.InvariantCulture));
            text.Append(" | cache=").Append(CacheBytes.ToString(CultureInfo.InvariantCulture)).Append(" B/")
                .Append(CacheEntries.ToString(CultureInfo.InvariantCulture));
            text.Append(" | quarantine=").Append(QuarantineBytes.ToString(CultureInfo.InvariantCulture)).Append(" B/")
                .Append(QuarantineEntries.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    /// <summary>One named correctness gate of the benchmark, asserted rather than merely measured.</summary>
    public sealed class BenchmarkGateResult
    {
        public BenchmarkGateResult(string name, bool passed, string detail)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        /// <summary>`key=value;...` fragments the harness greps, so a pass flag always has its numbers beside it.</summary>
        public string Detail { get; }

        public override string ToString() => (Passed ? "pass " : "FAIL ") + Name + ": " + Detail;
    }

    /// <summary>
    /// Everything one workload measured: its declared shape, every raw sample, the counter totals, the memory
    /// categories and its gates. This is the raw per-sample document 08 asks a benchmark to write.
    /// </summary>
    public sealed class BenchmarkRunDocument
    {
        private readonly List<BenchmarkSample> samples = new List<BenchmarkSample>();
        private readonly List<BenchmarkGateResult> gates = new List<BenchmarkGateResult>();
        private readonly List<string> notes = new List<string>();

        public BenchmarkRunDocument(
            string workloadId,
            BenchmarkWorkloadKind kind,
            string dimension,
            int scopes,
            int targets,
            int durationSeconds,
            int warmupSeconds,
            int repetitionsRequested,
            uint seed,
            int runOrdinal)
        {
            WorkloadId = workloadId ?? throw new ArgumentNullException(nameof(workloadId));
            Kind = kind;
            Dimension = dimension ?? string.Empty;
            Scopes = scopes;
            Targets = targets;
            DurationSeconds = durationSeconds;
            WarmupSeconds = warmupSeconds;
            RepetitionsRequested = repetitionsRequested;
            Seed = seed;
            RunOrdinal = runOrdinal;
        }

        public string WorkloadId { get; }

        public BenchmarkWorkloadKind Kind { get; }

        public string Dimension { get; }

        public int Scopes { get; }

        public int Targets { get; }

        public int DurationSeconds { get; }

        public int WarmupSeconds { get; }

        /// <summary>Repetitions the configuration asked for; zero for a steady workload.</summary>
        public int RepetitionsRequested { get; }

        /// <summary>Repetitions the run actually executed. Never silently less than requested.</summary>
        public int RepetitionsExecuted { get; set; }

        public uint Seed { get; }

        public int RunOrdinal { get; }

        /// <summary>Committed logical steps the workload observed.</summary>
        public long StepsAdvanced { get; set; }

        /// <summary>Wall-clock measurement window this workload actually ran for, in microseconds.</summary>
        public long WindowMicroseconds { get; set; }

        public IReadOnlyList<BenchmarkSample> Samples => samples;

        public IReadOnlyList<BenchmarkGateResult> Gates => gates;

        public IReadOnlyList<string> Notes => notes;

        /// <summary>Totals of every counter the workload observed (deltas, aggregated by the schema policy).</summary>
        public TelemetryCounterSet Totals { get; private set; } = new TelemetryCounterSet();

        /// <summary>The memory categories the workload ended with; replaced by the runner once it has sampled them.</summary>
        public BenchmarkMemoryCategories Memory { get; set; } = BenchmarkMemoryCategories.FromCounters(
            new TelemetryCounterSet(), 0L, 0L, false, 0L, 0L);

        /// <summary>True when every gate of this workload passed and the run executed what it declared.</summary>
        public bool Passed
        {
            get
            {
                if (RepetitionsRequested != 0 && RepetitionsExecuted < RepetitionsRequested)
                {
                    return false;
                }

                if (gates.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < gates.Count; i++)
                {
                    if (!gates[i].Passed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void Add(BenchmarkSample sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            samples.Add(sample);
        }

        public void Add(BenchmarkGateResult gate)
        {
            if (gate == null)
            {
                throw new ArgumentNullException(nameof(gate));
            }

            gates.Add(gate);
        }

        public void Note(string note)
        {
            if (!string.IsNullOrEmpty(note))
            {
                notes.Add(note);
            }
        }

        /// <summary>Replaces the totals; the runner accumulates them from the samples' own counter deltas.</summary>
        public void SetTotals(TelemetryCounterSet totals)
        {
            Totals = totals ?? throw new ArgumentNullException(nameof(totals));
        }

        /// <summary>Every sample of one phase, in sample order.</summary>
        public List<long> DurationsOf(BenchmarkPhase phase)
        {
            var durations = new List<long>();
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Phase == phase)
                {
                    durations.Add(samples[i].Microseconds);
                }
            }

            return durations;
        }

        public string Describe() => WorkloadId + "{" + Kind.ToString() + ", scopes=" + Scopes
            + ", targets=" + Targets + ", run=" + RunOrdinal + ", samples=" + samples.Count
            + ", gates=" + gates.Count + ", passed=" + (Passed ? "true" : "false") + "}";

        public override string ToString() => Describe();
    }
}
