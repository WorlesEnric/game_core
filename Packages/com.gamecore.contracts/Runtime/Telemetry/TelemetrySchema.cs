// GameCore.Contracts — the fixed compact telemetry schema (GC-023).
//
// Normative source: docs/game-core/08-validation-and-performance.md section "Instrumentation and reproducible
// performance method", which names the counters a conforming implementation exposes, and GC-023's brief, which
// requires *a fixed compact schema that each runtime owner exposes*, cheap, with enablement and retention decided
// explicitly by build configuration.
//
// Three decisions define this file, and each one is deliberate:
//
//   1. **Fixed numeric ids, one flat array.** `TelemetryCounter` is the whole schema: a stable integer per name,
//      so a section is one `long[]`, a sample costs one array store, and a trace compares ids rather than strings.
//      The 08 names come first, in the order 08 lists them (ids 0..19, with the two duration families expanded to
//      a total and a sample count and the high-water/overflow pair expanded to two ids). Ids 20..27 carry the
//      reporting split TEST-023 demands: retained event bytes/count, quarantine bytes/entries, lease bytes, cache
//      bytes/entries and discarded callbacks, so "memory counters distinguish leases/events/cache/quarantine" is a
//      property of the schema rather than of a report's prose. `TelemetryCounter.Count` is the array length.
//
//   2. **Counting is a call to a [Conditional] helper.** Every hot-path count in a runtime owner is
//      `TelemetryCounting.Count(counters, TelemetryCounter.X)`. `System.Diagnostics.Conditional` makes the compiler
//      *remove the call site and its argument evaluation* when `GAMECORE_TELEMETRY` is undefined, which is what
//      makes the disabled shape cost nothing: no branch, no store, no argument evaluation. The symbol reaches a
//      compilation only through the asmdef `versionDefines` entry on the qualification marker package
//      `com.gamecore.telemetry-qualification`, which only the validation project's manifest references; a shipping
//      project therefore compiles zero counting call sites, exactly as GC-017's `GAMECORE_FAULT_INJECTION` works.
//      `TelemetryCounting.IsCompiledIn` reports which shape the *calling* compilation took, so a test can assert
//      the enabled behaviour and the disabled behaviour from the same source.
//
//   3. **Descriptions are formatted on demand.** The set stores numbers only. `Describe()` and
//      `TelemetryCounterText.Name` produce text, and are never called from a per-step path.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>
    /// The fixed counter ids of the compact schema (GC-023). Numeric values are part of the wire/trace format:
    /// a frame compares these ids, so a name change is free and a value change is a format change.
    /// </summary>
    public enum TelemetryCounter
    {
        /// <summary>08 `ControlNodesVisited`: control-plane nodes examined. Counted even when a traversal answers
        /// from a cache or matches nothing (TEST-023).</summary>
        ControlNodesVisited = 0,

        /// <summary>08 `CandidatesMatched`: derivation candidates examined against a rule.</summary>
        CandidatesMatched = 1,

        /// <summary>08 `ContributionsAdded`.</summary>
        ContributionsAdded = 2,

        /// <summary>08 `ContributionsRetracted`.</summary>
        ContributionsRetracted = 3,

        /// <summary>08 `StrataEvaluated`: capability strata the engine iterated.</summary>
        StrataEvaluated = 4,

        /// <summary>08 `PlanPreparedBytes`: staged acquisitions plus scratch high-water of one plan.</summary>
        PlanPreparedBytes = 5,

        /// <summary>08 `ApplyDuration`: fenced apply duration, microseconds, summed over samples.</summary>
        ApplyDurationMicroseconds = 6,

        /// <summary>08 `AssemblyEpoch`: the epoch the sample describes (a gauge, aggregated by max).</summary>
        AssemblyEpoch = 7,

        /// <summary>08 `StepsAdvanced`: committed logical steps.</summary>
        StepsAdvanced = 8,

        /// <summary>08 `ServiceStringLookups`: service resolutions that hashed a name string (P-008).</summary>
        ServiceStringLookups = 9,

        /// <summary>08 per-stage duration, microseconds, summed over stages and samples.</summary>
        StageDurationMicroseconds = 10,

        /// <summary>Number of per-stage duration samples behind <see cref="StageDurationMicroseconds"/>.</summary>
        StageSampleCount = 11,

        /// <summary>08 job wait duration, microseconds, summed over samples.</summary>
        JobWaitDurationMicroseconds = 12,

        /// <summary>Number of job-wait samples behind <see cref="JobWaitDurationMicroseconds"/>.</summary>
        JobWaitSampleCount = 13,

        /// <summary>08 structural operations recorded or played back (P-041).</summary>
        StructuralOperations = 14,

        /// <summary>08 request high-water mark: the deepest pending queue observed (a gauge, max).</summary>
        RequestHighWater = 15,

        /// <summary>08 request overflow count: admissions refused because a bounded queue was full (P-043).</summary>
        RequestOverflow = 16,

        /// <summary>08 stale-result count: stale plans, handles, revisions and completions refused or discarded.</summary>
        StaleResults = 17,

        /// <summary>08 live leases.</summary>
        LiveLeases = 18,

        /// <summary>08 outstanding callbacks (live activations plus tracked jobs).</summary>
        OutstandingCallbacks = 19,

        /// <summary>08 retained event bytes; separate from <see cref="QuarantineBytes"/> and <see cref="LeaseBytes"/>.</summary>
        RetainedEventBytes = 20,

        /// <summary>Retained committed events behind <see cref="RetainedEventBytes"/>.</summary>
        RetainedEventCount = 21,

        /// <summary>08 quarantine bytes (P-048).</summary>
        QuarantineBytes = 22,

        /// <summary>Quarantined entries behind <see cref="QuarantineBytes"/>.</summary>
        QuarantineEntries = 23,

        /// <summary>Bytes held by live staged/asset leases: the "leases" half of the memory split.</summary>
        LeaseBytes = 24,

        /// <summary>Bytes held by retained derived caches: the "cache" half of the memory split (P-023/P-024).</summary>
        CacheBytes = 25,

        /// <summary>Entries behind <see cref="CacheBytes"/>.</summary>
        CacheEntries = 26,

        /// <summary>Callbacks discarded as stale (P-047), reported separately from all other stale work.</summary>
        DiscardedCallbacks = 27,

        /// <summary>Number of fenced-apply duration samples behind <see cref="ApplyDurationMicroseconds"/>; a zero
        /// count means no apply happened in this window, which is a different claim from "the apply took no time".</summary>
        ApplySampleCount = 28,

        /// <summary>Number of counters in the schema; the length of a counter set's array.</summary>
        Count = 29,
    }

    /// <summary>How one counter aggregates across the owners of a frame.</summary>
    public enum TelemetryAggregation
    {
        /// <summary>Cumulative work: the frame's value is the sum of its owners' values.</summary>
        Sum = 0,

        /// <summary>A gauge or a fact of the sample (live counts, bytes held, epoch, high-water): the maximum.</summary>
        Max = 1,
    }

    /// <summary>Static facts about the schema: names, aggregation policy, byte accounting and the build switch.</summary>
    public static class TelemetrySchema
    {
        /// <summary>Wire/trace format identifier of one frame.</summary>
        public const string FrameFormat = "gamecore.telemetry.frame/1";

        /// <summary>Wire/trace format identifier of one retained trace.</summary>
        public const string TraceFormat = "gamecore.telemetry.trace/1";

        /// <summary>The compile-time symbol that switches counting on (GC-023).</summary>
        public const string Symbol = "GAMECORE_TELEMETRY";

        /// <summary>Number of counters; identical to <see cref="TelemetryCounter.Count"/>.</summary>
        public const int CounterCount = (int)TelemetryCounter.Count;

        /// <summary>
        /// True when this *calling* compilation was built with <see cref="Symbol"/> defined. A property rather
        /// than a constant so a caller can branch on it without the compiler seeing unreachable code.
        /// </summary>
        public static bool IsCompiledIn
        {
            get
            {
#if GAMECORE_TELEMETRY
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Stable lower-case diagnostic name of one counter (formatting only; never a key).</summary>
        public static string Name(TelemetryCounter counter)
        {
            switch (counter)
            {
                case TelemetryCounter.ControlNodesVisited: return "control-nodes-visited";
                case TelemetryCounter.CandidatesMatched: return "candidates-matched";
                case TelemetryCounter.ContributionsAdded: return "contributions-added";
                case TelemetryCounter.ContributionsRetracted: return "contributions-retracted";
                case TelemetryCounter.StrataEvaluated: return "strata-evaluated";
                case TelemetryCounter.PlanPreparedBytes: return "plan-prepared-bytes";
                case TelemetryCounter.ApplyDurationMicroseconds: return "apply-us";
                case TelemetryCounter.AssemblyEpoch: return "assembly-epoch";
                case TelemetryCounter.StepsAdvanced: return "steps-advanced";
                case TelemetryCounter.ServiceStringLookups: return "service-string-lookups";
                case TelemetryCounter.StageDurationMicroseconds: return "stage-us";
                case TelemetryCounter.StageSampleCount: return "stage-samples";
                case TelemetryCounter.JobWaitDurationMicroseconds: return "job-wait-us";
                case TelemetryCounter.JobWaitSampleCount: return "job-wait-samples";
                case TelemetryCounter.StructuralOperations: return "structural-operations";
                case TelemetryCounter.RequestHighWater: return "request-high-water";
                case TelemetryCounter.RequestOverflow: return "request-overflow";
                case TelemetryCounter.StaleResults: return "stale-results";
                case TelemetryCounter.LiveLeases: return "live-leases";
                case TelemetryCounter.OutstandingCallbacks: return "outstanding-callbacks";
                case TelemetryCounter.RetainedEventBytes: return "retained-event-bytes";
                case TelemetryCounter.RetainedEventCount: return "retained-event-count";
                case TelemetryCounter.QuarantineBytes: return "quarantine-bytes";
                case TelemetryCounter.QuarantineEntries: return "quarantine-entries";
                case TelemetryCounter.LeaseBytes: return "lease-bytes";
                case TelemetryCounter.CacheBytes: return "cache-bytes";
                case TelemetryCounter.CacheEntries: return "cache-entries";
                case TelemetryCounter.DiscardedCallbacks: return "discarded-callbacks";
                case TelemetryCounter.ApplySampleCount: return "apply-samples";
                default: return "counter-" + ((int)counter).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Aggregation policy of one counter; see <see cref="TelemetryAggregation"/>.</summary>
        public static TelemetryAggregation AggregationOf(TelemetryCounter counter)
        {
            switch (counter)
            {
                case TelemetryCounter.AssemblyEpoch:
                case TelemetryCounter.RequestHighWater:
                case TelemetryCounter.LiveLeases:
                case TelemetryCounter.OutstandingCallbacks:
                case TelemetryCounter.RetainedEventBytes:
                case TelemetryCounter.RetainedEventCount:
                case TelemetryCounter.QuarantineBytes:
                case TelemetryCounter.QuarantineEntries:
                case TelemetryCounter.LeaseBytes:
                case TelemetryCounter.CacheBytes:
                case TelemetryCounter.CacheEntries:
                    return TelemetryAggregation.Max;
                default:
                    return TelemetryAggregation.Sum;
            }
        }

        /// <summary>Every counter id in canonical order; useful for a table-driven report or test.</summary>
        public static IReadOnlyList<TelemetryCounter> All { get; } = BuildAll();

        private static IReadOnlyList<TelemetryCounter> BuildAll()
        {
            var all = new TelemetryCounter[CounterCount];
            for (int i = 0; i < CounterCount; i++)
            {
                all[i] = (TelemetryCounter)i;
            }

            return Array.AsReadOnly(all);
        }
    }

    /// <summary>
    /// One runtime owner's counters, exposed through the fixed compact schema (GC-023). A production owner
    /// implements this so the integration seam can sample every owner through one type instead of a growing
    /// per-owner switch. `TelemetryOwner` is a stable lower-case key (reverse-domain style, no spaces) used as the
    /// section key of a frame, and must not depend on a runtime id.
    /// </summary>
    public interface ITelemetryOwner
    {
        /// <summary>Stable section key of this owner, e.g. <c>gamecore.derivation</c>.</summary>
        string TelemetryOwner { get; }

        /// <summary>
        /// Writes this owner's current counter values into <paramref name="into"/>. Implementations write gauges
        /// with <see cref="TelemetryCounterSet.Set"/> and cumulative work with <see cref="TelemetryCounterSet.Add"/>
        /// so a collector can aggregate several owners with the schema's own policy.
        /// </summary>
        void WriteTelemetry(TelemetryCounterSet into);
    }

    /// <summary>
    /// The three counting helpers every hot path calls. Each is <see cref="ConditionalAttribute"/> on
    /// <see cref="TelemetrySchema.Symbol"/>: with the symbol undefined the compiler removes the call, the receiver
    /// check and the argument evaluation, so a disabled build pays nothing at the call site (GC-023).
    /// </summary>
    public static class TelemetryCounting
    {
        /// <summary>True when counting call sites survive compilation in the calling assembly.</summary>
        public static bool IsCompiledIn => TelemetrySchema.IsCompiledIn;

        /// <summary>Adds one to a cumulative counter.</summary>
        [Conditional(TelemetrySchema.Symbol)]
        public static void Count(TelemetryCounterSet? counters, TelemetryCounter counter) =>
            counters?.Add(counter, 1L);

        /// <summary>Adds a delta to a cumulative counter.</summary>
        [Conditional(TelemetrySchema.Symbol)]
        public static void Add(TelemetryCounterSet? counters, TelemetryCounter counter, long delta) =>
            counters?.Add(counter, delta);

        /// <summary>Raises a gauge to at least <paramref name="value"/>.</summary>
        [Conditional(TelemetrySchema.Symbol)]
        public static void Observe(TelemetryCounterSet? counters, TelemetryCounter counter, long value) =>
            counters?.ObserveMax(counter, value);
    }

    /// <summary>
    /// Byte accounting shared by both sides of a memory report, so a producer and a collector cannot disagree
    /// about what "retained event bytes" means (TEST-023).
    /// </summary>
    public static class TelemetryBytes
    {
        /// <summary>Per-event bookkeeping the store retains besides the payload: cursor, schema, epoch, step and
        /// causal operation identity. A documented constant, not a native struct size (P-054).</summary>
        public const int RetainedEventOverhead = 96;

        /// <summary>Retained bytes of one committed event with the given payload length.</summary>
        public static long RetainedEvent(int payloadLength) =>
            payloadLength < 0 ? RetainedEventOverhead : payloadLength + RetainedEventOverhead;

        /// <summary>Per-section bookkeeping of a telemetry frame: owner key, counter count and the array header.</summary>
        public const int SectionOverhead = 32;
    }

    /// <summary>
    /// Explicit retention policy of the telemetry collector (GC-023). The compile-time switch decides whether
    /// counting happens at all; this decides whether frames are *kept*, which is what lets a qualification build
    /// run with instrumentation on and retention off (08: record the instrumented diagnostic build and the
    /// release-like measurement build separately).
    /// </summary>
    public readonly struct TelemetryRetention
    {
        public TelemetryRetention(int maxFrames, int maxSections)
        {
            if (maxFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFrames));
            }

            if (maxSections < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSections));
            }

            MaxFrames = maxFrames;
            MaxSections = maxSections;
        }

        /// <summary>Retain nothing: sampling still runs, no frame is kept.</summary>
        public static TelemetryRetention Off => new TelemetryRetention(0, 0);

        /// <summary>Reference budget of a diagnostic run: 10,000 frames, 32 owners.</summary>
        public static TelemetryRetention Default => new TelemetryRetention(10000, 32);

        /// <summary>Bounded frame retention; zero disables retention.</summary>
        public int MaxFrames { get; }

        /// <summary>Bounded number of owners one frame may carry.</summary>
        public int MaxSections { get; }

        /// <summary>True when frames are retained.</summary>
        public bool Enabled => MaxFrames > 0 && MaxSections > 0;

        public override string ToString() =>
            "TelemetryRetention(frames<=" + MaxFrames.ToString(CultureInfo.InvariantCulture)
            + ", sections<=" + MaxSections.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
