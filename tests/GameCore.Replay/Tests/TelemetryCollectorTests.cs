// GameCore.Replay tests — the telemetry collector, the build switch and the raw benchmark trace (GC-023).
//
// GC-023's definition of done is "counter instrumentation is cheap and disabled/retained explicitly by build
// config, and trace data can diagnose ordering and cost regressions without changing semantics". Each assertion
// below defends one of those three clauses:
//
//   * *enabled/retained explicitly*: retention is a declared policy, a frame is refused (and counted) past it, and
//     `TelemetrySchema.IsCompiledIn` reports whether this compilation counts at all.
//   * *cheap*: the counting helpers are `[Conditional]` methods, so a build without the symbol emits no call. The
//     disabled-shape proof over real production sources lives in `tools/check_release_telemetry_free.py`, which
//     compiles both configurations and compares them; this suite proves the enabled half it depends on (a counting
//     call site really counts when the symbol is on) and that counting is a plain `long` store.
//   * *diagnoses regressions without changing semantics*: the trace carries the counters, the correctness hashes do
//     not (proved in `ReplayDeterminismTests`).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation;
using NUnit.Framework;

namespace GameCore.Replay.Tests
{
    [TestFixture]
    public sealed class TelemetryCollectorTests
    {
        private sealed class FixedOwner : ITelemetryOwner
        {
            public FixedOwner(string key, TelemetryCounter counter, long value)
            {
                TelemetryOwner = key;
                Counter = counter;
                Value = value;
            }

            public string TelemetryOwner { get; }

            public TelemetryCounter Counter { get; }

            public long Value { get; }

            public int Writes { get; private set; }

            public void WriteTelemetry(TelemetryCounterSet into)
            {
                Writes++;
                into.Add(Counter, Value);
            }
        }

        [Test]
        public void AFrameCarriesOneSectionPerOwnerKeyInAscendingKeyOrder()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(8, 8));
            collector.Add(new FixedOwner("gamecore.b", TelemetryCounter.StepsAdvanced, 2L));
            collector.Add(new FixedOwner("gamecore.a", TelemetryCounter.ControlNodesVisited, 3L));

            TelemetryFrame frame = collector.Sample(0, AssemblyEpoch.First, new LogicalStepId(1UL));
            Assert.That(frame.Sections.Count, Is.EqualTo(2));
            Assert.That(frame.Sections[0].Owner, Is.EqualTo("gamecore.a"), "sections are in canonical key order");
            Assert.That(frame.Sections[1].Owner, Is.EqualTo("gamecore.b"));
            Assert.That(frame.CountersOf("gamecore.a")!.Get(TelemetryCounter.ControlNodesVisited), Is.EqualTo(3L));
            Assert.That(frame.Aggregate().Get(TelemetryCounter.StepsAdvanced), Is.EqualTo(2L));
            Assert.That(collector.OwnerCount, Is.EqualTo(2));
            Assert.That(collector.SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void TwoOwnersSharingAKeyMergeIntoOneSection()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(8, 8));
            collector.Add(new FixedOwner("gamecore.shared", TelemetryCounter.StructuralOperations, 2L));
            collector.Add(new FixedOwner("gamecore.shared", TelemetryCounter.StructuralOperations, 3L));

            TelemetryFrame frame = collector.Sample(0, AssemblyEpoch.First, new LogicalStepId(1UL));
            Assert.That(frame.Sections.Count, Is.EqualTo(1));
            Assert.That(frame.Sections[0].Counters.Get(TelemetryCounter.StructuralOperations), Is.EqualTo(5L),
                "two owners behind one key report one section, and cumulative work sums");
        }

        [Test]
        public void RetentionIsBoundedAndCountedRatherThanImplicit()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(2, 2));
            collector.Add(new FixedOwner("gamecore.a", TelemetryCounter.StepsAdvanced, 1L));
            for (int i = 0; i < 5; i++)
            {
                collector.Sample(0, AssemblyEpoch.First, new LogicalStepId((ulong)i));
            }

            Assert.That(collector.Frames.Count, Is.EqualTo(2));
            Assert.That(collector.SampleCount, Is.EqualTo(5));
            Assert.That(collector.DiscardedSampleCount, Is.EqualTo(3), "a refused sample is counted, never silent");
            Assert.That(collector.Describe(), Does.Contain("retained=2"));
        }

        [Test]
        public void RetentionOffRetainsNothingWhileTheOwnersStillWrite()
        {
            var owner = new FixedOwner("gamecore.a", TelemetryCounter.StepsAdvanced, 1L);
            var collector = new TelemetryCollector(TelemetryRetention.Off);
            collector.Add(owner);

            TelemetryFrame frame = collector.Sample(0, AssemblyEpoch.First, new LogicalStepId(1UL));
            Assert.That(owner.Writes, Is.EqualTo(1), "sampling still reads the owner when retention is off");
            Assert.That(frame.Sections.Count, Is.EqualTo(1), "the caller still receives the frame it asked for");
            Assert.That(collector.Frames.Count, Is.EqualTo(0));
            Assert.That(collector.Trace("off").Frames.Count, Is.EqualTo(0));
        }

        [Test]
        public void ATooWideFrameIsRefusedRatherThanTruncated()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(4, 1));
            collector.Add(new FixedOwner("gamecore.a", TelemetryCounter.StepsAdvanced, 1L));
            collector.Add(new FixedOwner("gamecore.b", TelemetryCounter.StepsAdvanced, 1L));

            collector.Sample(0, AssemblyEpoch.First, new LogicalStepId(1UL));
            Assert.That(collector.Frames.Count, Is.EqualTo(0), "a frame past the section bound is refused whole");
            Assert.That(collector.DiscardedSampleCount, Is.EqualTo(1));
        }

        [Test]
        public void SamplingExplicitOwnersDoesNotGrowRegistration()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(4, 4));
            var owners = new List<ITelemetryOwner>();
            for (int i = 0; i < 3; i++)
            {
                owners.Clear();
                owners.Add(new FixedOwner("gamecore.running", TelemetryCounter.StepsAdvanced, 1L));
                collector.Sample(0, AssemblyEpoch.First, new LogicalStepId((ulong)i), owners);
            }

            Assert.That(collector.OwnerCount, Is.EqualTo(0), "the explicit overload registers nothing");
            Assert.That(collector.Frames.Count, Is.EqualTo(3));
            Assert.That(collector.SectionCount, Is.EqualTo(3));
        }

        [Test]
        public void ADelegateOwnerExposesABareCounterSet()
        {
            var set = new TelemetryCounterSet();
            set.Add(TelemetryCounter.CacheEntries, 4L);
            var collector = new TelemetryCollector(new TelemetryRetention(1, 1));
            collector.Add("gamecore.cache", into => into.Merge(set));

            TelemetryFrame frame = collector.Sample(0, AssemblyEpoch.First, new LogicalStepId(1UL));
            Assert.That(frame.CountersOf("gamecore.cache")!.Get(TelemetryCounter.CacheEntries), Is.EqualTo(4L));
        }

        [Test]
        public void RegisteringTheSameOwnerTwiceIsIdempotentAndAnEmptyKeyIsRefused()
        {
            var owner = new FixedOwner("gamecore.a", TelemetryCounter.StepsAdvanced, 1L);
            var collector = new TelemetryCollector(new TelemetryRetention(1, 1));
            collector.Add(owner).Add(owner);
            Assert.That(collector.OwnerCount, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => collector.Add((ITelemetryOwner)null!));
            Assert.Throws<ArgumentException>(() => collector.Add(new FixedOwner(string.Empty, TelemetryCounter.StepsAdvanced, 1L)));
        }
    }

    [TestFixture]
    public sealed class TelemetryBuildSwitchTests
    {
        /// <summary>
        /// The plain-dotnet shadow build and the Unity qualification project define `GAMECORE_TELEMETRY`, so the
        /// counting call sites survive here. `tools/check_release_telemetry_free.py` proves the opposite shape by
        /// compiling the same production sources without the symbol, and it fails if this half ever stops being true
        /// (which is what stops the check from passing by inspecting nothing).
        /// </summary>
        [Test]
        public void ThisCompilationCounts()
        {
#if GAMECORE_TELEMETRY
            Assert.That(TelemetrySchema.IsCompiledIn, Is.True);
            Assert.That(TelemetryCounting.IsCompiledIn, Is.True);
#else
            Assert.That(TelemetrySchema.IsCompiledIn, Is.False);
            Assert.That(TelemetryCounting.IsCompiledIn, Is.False);
#endif
        }

        [Test]
        public void ACountingCallSiteReallyCountsWhenTheSymbolIsOn()
        {
            var set = new TelemetryCounterSet();
            // These call sites compile to nothing when the symbol is undefined (System.Diagnostics.Conditional
            // removes the call and its argument evaluation), so this assertion is the enabled half of the switch.
            TelemetryCounting.Count(set, TelemetryCounter.ControlNodesVisited);
            TelemetryCounting.Add(set, TelemetryCounter.CandidatesMatched, 5L);
            TelemetryCounting.Observe(set, TelemetryCounter.RequestHighWater, 3L);
            TelemetryCounting.Observe(set, TelemetryCounter.RequestHighWater, 1L);
            TelemetryCounting.Count(null, TelemetryCounter.StepsAdvanced);

#if GAMECORE_TELEMETRY
            Assert.That(set.Get(TelemetryCounter.ControlNodesVisited), Is.EqualTo(1L));
            Assert.That(set.Get(TelemetryCounter.CandidatesMatched), Is.EqualTo(5L));
            Assert.That(set.Get(TelemetryCounter.RequestHighWater), Is.EqualTo(3L), "a gauge keeps its high-water mark");
#else
            Assert.That(set.IsZero, Is.True, "without the symbol the call sites do not survive compilation");
#endif
        }

        [Test]
        public void TheRealDerivationReportsItsOwnCountersThroughTheSchema()
        {
            DerivationSnapshot snapshot = IntegerFixtureGenerator
                .Builder(new ReplayFixtureShape(2, 2, 1, 2, 1, 1, 1U))
                .Install(
                    IntegerFixtureGenerator.ProviderName(0, 0),
                    IntegerFixtureGenerator.Branch(0),
                    0,
                    IntegerFixtureGenerator.ProviderRules(IntegerFixtureGenerator.ProviderName(0, 0), 3),
                    InstallationState.Active)
                .Build(PropagationMode.Automatic, CompositionRevision.First, AssemblyEpoch.First)
                .ToSnapshot();

            DerivationResult result = DerivationEngine.Derive(
                snapshot,
                IntegerFixtureGenerator.ValueSource(),
                new DerivationOptions(null, null, null, null, true),
                null);
            Assert.That(result.Accepted, Is.True);

            var into = new TelemetryCounterSet();
            result.WriteTelemetry(into);

            Assert.That(result.Counters.ExaminedCandidates, Is.GreaterThan(0), "the engine examined candidates");
            Assert.That(result.Counters.StrataEvaluated, Is.GreaterThan(0), "the engine iterated a stratum");

#if GAMECORE_TELEMETRY
            Assert.That(into.Get(TelemetryCounter.CandidatesMatched), Is.GreaterThan(0L),
                "the owner reports its own counter through the fixed schema");
            Assert.That(into.Get(TelemetryCounter.StrataEvaluated), Is.GreaterThan(0L));
            Assert.That(into.Get(TelemetryCounter.ControlNodesVisited), Is.GreaterThan(0L),
                "the indexed engine visits control nodes while deriving");
#else
            // Without the symbol only the *telemetry-only* counters stay zero: the protocol counters (P-022's
            // budget accounting) are written in every shape, which the assertion after this block checks.
            Assert.That(into.Get(TelemetryCounter.StrataEvaluated), Is.EqualTo(0L));
            Assert.That(into.Get(TelemetryCounter.ControlNodesVisited), Is.EqualTo(0L));
#endif

            // The protocol counters are reported in both shapes: P-022's budget accounting is normative, not
            // optional instrumentation, so a build without the marker still reports what the engine examined.
            Assert.That(
                into.Get(TelemetryCounter.CandidatesMatched),
                Is.EqualTo((long)result.Counters.ExaminedCandidates),
                "the schema reports the engine's own examined-candidate count in every build shape");
        }

        [Test]
        public void ACarriedIncrementalDerivationDoesNoControlWork()
        {
            ReplayFixtureShape shape = new ReplayFixtureShape(2, 2, 1, 2, 1, 1, 1U);
            DerivationSnapshot snapshot = IntegerFixtureGenerator
                .Builder(shape)
                .Install(
                    IntegerFixtureGenerator.ProviderName(0, 0),
                    IntegerFixtureGenerator.Branch(0),
                    0,
                    IntegerFixtureGenerator.ProviderRules(IntegerFixtureGenerator.ProviderName(0, 0), 1),
                    InstallationState.Active)
                .Build(PropagationMode.Automatic, CompositionRevision.First, AssemblyEpoch.First)
                .ToSnapshot();

            DerivationOptions options = new DerivationOptions(null, null, null, null, true);
            DerivationResult first = DerivationEngine.Derive(
                snapshot, IntegerFixtureGenerator.ValueSource(), options, null);
            Assert.That(first.Accepted, Is.True);

            IncrementalDerivationOutcome carried = IncrementalDerivationEngine.Derive(
                snapshot,
                IntegerFixtureGenerator.ValueSource(),
                options,
                first,
                DerivationChangeSet.Diff(first.Snapshot, snapshot));
            Assert.That(carried.Invalidation.IsEmpty, Is.True, "an unchanged snapshot has nothing to invalidate");
            Assert.That(carried.Counters.CandidateEvaluations, Is.EqualTo(0));
            Assert.That(carried.Counters.ExaminedCandidates, Is.EqualTo(0));
            Assert.That(carried.Result.Counters.ControlNodesVisited, Is.EqualTo(0L),
                "a carried derivation examines no control node at all (TEST-023)");
            Assert.That(carried.Result.Counters.StrataEvaluated, Is.EqualTo(0L));
        }

        [Test]
        public void ServiceStringLookupsAreCountedWhereResolutionHappensAndNowhereElse()
        {
            // The one string-keyed resolution in production is the lease id a binding derives from a name. It is a
            // planning-time cost, so the counter must move when a resolution builds a binding, and the pure fixture's
            // 10,000-step replay must never reach it: a step does not resolve services.
            ReplayTrace trace = IntegerFixtureGenerator.Generate("services", 13U,
                new ReplayFixtureShape(2, 2, 1, 2, 24, 2, 1U));
            ReplayRun run = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Default, 1000000, 0)).Run(trace);

            Assert.That(run.Telemetry, Is.Not.Null);
            for (int i = 0; i < run.Telemetry!.Frames.Count; i++)
            {
                Assert.That(
                    run.Telemetry.Frames[i].Aggregate().Get(TelemetryCounter.ServiceStringLookups),
                    Is.EqualTo(0L),
                    "a step resolves no service by name (TEST-023's zero-string-resolution case)");
            }

            Assert.That(TelemetrySchema.Name(TelemetryCounter.ServiceStringLookups), Is.EqualTo("service-string-lookups"));
        }
    }

    [TestFixture]
    public sealed class BenchmarkTraceFormatTests
    {
        private static TelemetryTrace SampleTrace(int frames, TelemetryRetention retention)
        {
            var collector = new TelemetryCollector(retention);
            collector.Add("gamecore.execution.driver", into =>
            {
                into.Add(TelemetryCounter.StepsAdvanced, 1L);
                into.ObserveMax(TelemetryCounter.OutstandingCallbacks, 2L);
            });
            collector.Add("gamecore.observation.events", into =>
            {
                into.ObserveMax(TelemetryCounter.RetainedEventBytes, 128L);
                into.ObserveMax(TelemetryCounter.RetainedEventCount, 1L);
                into.ObserveMax(TelemetryCounter.QuarantineBytes, 0L);
                into.ObserveMax(TelemetryCounter.LeaseBytes, 64L);
                into.ObserveMax(TelemetryCounter.CacheBytes, 96L);
            });
            for (int i = 0; i < frames; i++)
            {
                collector.Sample(7, AssemblyEpoch.First, new LogicalStepId((ulong)i + 1UL));
            }

            return collector.Trace("raw-benchmark");
        }

        [Test]
        public void TheRawFormatRoundTripsEveryFrameHeader()
        {
            TelemetryTrace trace = SampleTrace(4, new TelemetryRetention(8, 8));
            string text = BenchmarkTrace.Write(trace);
            Assert.That(text.StartsWith("# " + BenchmarkTrace.Format, StringComparison.Ordinal), Is.True);
            Assert.That(text, Does.Not.Contain("2026"), "a trace embeds no timestamp");

            Assert.That(BenchmarkTrace.TryRead(text, out IReadOnlyList<BenchmarkTrace.RawFrame> frames, out string failure),
                Is.True, failure);
            Assert.That(frames.Count, Is.EqualTo(4));
            Assert.That(frames[0].Ordinal, Is.EqualTo(7));
            Assert.That(frames[0].Step, Is.EqualTo(1UL));
            Assert.That(frames[3].Step, Is.EqualTo(4UL));
            Assert.That(frames[0].Sections, Is.EqualTo(2));
            Assert.That(frames[0].Counters, Does.Contain("steps-advanced=1"));
            Assert.That(frames[0].Counters, Does.Contain("retained-event-bytes=128"));
        }

        [Test]
        public void TheMemorySplitIsReportedSeparatelyInOneFrame()
        {
            TelemetryTrace trace = SampleTrace(1, new TelemetryRetention(1, 4));
            TelemetryCounterSet aggregate = trace.Frames[0].Aggregate();
            Assert.That(aggregate.Get(TelemetryCounter.LeaseBytes), Is.EqualTo(64L));
            Assert.That(aggregate.Get(TelemetryCounter.RetainedEventBytes), Is.EqualTo(128L));
            Assert.That(aggregate.Get(TelemetryCounter.CacheBytes), Is.EqualTo(96L));
            Assert.That(aggregate.Get(TelemetryCounter.QuarantineBytes), Is.EqualTo(0L));
            Assert.That(aggregate.Get(TelemetryCounter.RetainedEventCount), Is.EqualTo(1L));

            string raw = BenchmarkTrace.Write(trace);
            Assert.That(raw, Does.Contain("lease-bytes=64"));
            Assert.That(raw, Does.Contain("cache-bytes=96"));
        }

        [Test]
        public void TheJsonDocumentIsHandRolledDeterministicAndStructurallyClosed()
        {
            TelemetryTrace trace = SampleTrace(2, new TelemetryRetention(4, 4));
            string first = BenchmarkTrace.WriteJson(trace);
            string second = BenchmarkTrace.WriteJson(trace);
            Assert.That(second, Is.EqualTo(first), "the document is deterministic");
            Assert.That(first, Does.StartWith("{\n"));
            Assert.That(first, Does.EndWith("}\n"));
            Assert.That(first, Does.Contain("\"frameCount\": 2"));
            Assert.That(first, Does.Contain("\"chainHash\": \"" + trace.ChainHash.ToHex() + "\""));
            Assert.That(first, Does.Contain("\"owner\": \"gamecore.execution.driver\""));
            Assert.That(Balanced(first, '{', '}'), Is.True, "the braces balance");
            Assert.That(Balanced(first, '[', ']'), Is.True, "the brackets balance");
        }

        [Test]
        public void AMalformedDocumentIsRefusedRatherThanPartlyParsed()
        {
            Assert.That(BenchmarkTrace.TryRead(string.Empty, out _, out string empty), Is.False);
            Assert.That(empty, Is.Not.Empty);
            Assert.That(BenchmarkTrace.TryRead("frame ordinal=0\n", out _, out string header), Is.False);
            Assert.That(header, Does.Contain("header"));
            Assert.That(
                BenchmarkTrace.TryRead("# " + BenchmarkTrace.Format + "\ngarbage here\n", out _, out string line),
                Is.False);
            Assert.That(line, Does.Contain("unexpected line"));
        }

        [Test]
        public void AnEmptyTraceIsStillAValidDocument()
        {
            var collector = new TelemetryCollector(new TelemetryRetention(2, 2));
            collector.Add("gamecore.a", into => into.Add(TelemetryCounter.StepsAdvanced, 1L));
            TelemetryTrace trace = collector.Trace("empty");
            string text = BenchmarkTrace.Write(trace);
            Assert.That(BenchmarkTrace.TryRead(text, out IReadOnlyList<BenchmarkTrace.RawFrame> frames, out _), Is.True);
            Assert.That(frames.Count, Is.EqualTo(0));
            Assert.That(text, Does.Contain("summary frames=0"));
        }

        private static bool Balanced(string text, char open, char close)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == open)
                {
                    depth++;
                }
                else if (character == close)
                {
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }
                }
            }

            return depth == 0 && !inString;
        }
    }
}
