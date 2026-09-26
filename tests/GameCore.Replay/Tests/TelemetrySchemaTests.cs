// GameCore.Replay tests — the telemetry schema, its build switch and its containers (GC-023).
//
// Sources in this folder also run as Unity EditMode tests (the package is testable) and as plain-dotnet tests
// (dotnet/tests/GameCore.Replay.Tests globs them), so nothing here may touch UnityEngine.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Replay.Tests
{
    [TestFixture]
    public sealed class TelemetrySchemaTests
    {
        [Test]
        public void TheSchemaNamesEveryCounterOfTheValidationDocument()
        {
            // The names of every id, in enum order. The first twenty are the counters 08 s3 lists, in the order it
            // lists them (with the two duration families expanded to a total and a sample count, and the
            // high-water/overflow pair to two ids); the rest are TEST-023's reporting split. A rename, a reorder or a
            // new id in the middle of the enum is a schema change, so this table is the mechanical half of that rule
            // and must stay aligned with `TelemetryCounter` member for member.
            string[] required =
            {
                "control-nodes-visited",
                "candidates-matched",
                "contributions-added",
                "contributions-retracted",
                "strata-evaluated",
                "plan-prepared-bytes",
                "apply-us",
                "assembly-epoch",
                "steps-advanced",
                "service-string-lookups",
                "stage-us",
                "stage-samples",
                "job-wait-us",
                "job-wait-samples",
                "structural-operations",
                "request-high-water",
                "request-overflow",
                "stale-results",
                "live-leases",
                "outstanding-callbacks",
                "retained-event-bytes",
                "retained-event-count",
                "quarantine-bytes",
                "quarantine-entries",
                "lease-bytes",
                "cache-bytes",
                "cache-entries",
                "discarded-callbacks",
                "apply-samples",
            };

            Assert.That(required.Length, Is.EqualTo(TelemetrySchema.CounterCount),
                "the name table must list every counter id exactly once");

            for (int i = 0; i < required.Length; i++)
            {
                Assert.That(TelemetrySchema.Name((TelemetryCounter)i), Is.EqualTo(required[i]),
                    "counter id " + i + " must keep the name 08 lists");
            }

            // The reporting split TEST-023 requires is present as ids, not as a report's prose.
            Assert.That(TelemetrySchema.Name(TelemetryCounter.LeaseBytes), Is.EqualTo("lease-bytes"));
            Assert.That(TelemetrySchema.Name(TelemetryCounter.CacheBytes), Is.EqualTo("cache-bytes"));
            Assert.That(TelemetrySchema.Name(TelemetryCounter.RetainedEventCount), Is.EqualTo("retained-event-count"));
            Assert.That(TelemetrySchema.Name(TelemetryCounter.QuarantineEntries), Is.EqualTo("quarantine-entries"));
            Assert.That(TelemetrySchema.Name(TelemetryCounter.DiscardedCallbacks), Is.EqualTo("discarded-callbacks"));
            Assert.That(TelemetrySchema.Name(TelemetryCounter.ApplySampleCount), Is.EqualTo("apply-samples"));
        }

        [Test]
        public void EveryIdIsContiguousAndTheCountMatchesTheEnum()
        {
            Assert.That((int)TelemetryCounter.Count, Is.EqualTo(TelemetrySchema.CounterCount));
            Assert.That(TelemetrySchema.All.Count, Is.EqualTo(TelemetrySchema.CounterCount));
            for (int i = 0; i < TelemetrySchema.CounterCount; i++)
            {
                Assert.That((int)TelemetrySchema.All[i], Is.EqualTo(i));
                Assert.That(TelemetrySchema.Name((TelemetryCounter)i).Length, Is.GreaterThan(0));
            }
        }

        [Test]
        public void GaugesAggregateByMaximumAndWorkBySum()
        {
            // A collector must not add two worlds' live lease counts together, and must not take the maximum of two
            // worlds' committed steps: the policy is part of the schema, so it is tested here rather than guessed at
            // a call site.
            TelemetryAggregation[] gauges =
            {
                TelemetryCounter.AssemblyEpoch,
                TelemetryCounter.RequestHighWater,
                TelemetryCounter.LiveLeases,
                TelemetryCounter.OutstandingCallbacks,
                TelemetryCounter.RetainedEventBytes,
                TelemetryCounter.RetainedEventCount,
                TelemetryCounter.QuarantineBytes,
                TelemetryCounter.QuarantineEntries,
                TelemetryCounter.LeaseBytes,
                TelemetryCounter.CacheBytes,
                TelemetryCounter.CacheEntries,
            };

            for (int i = 0; i < gauges.Length; i++)
            {
                Assert.That(TelemetrySchema.AggregationOf(gauges[i]), Is.EqualTo(TelemetryAggregation.Max),
                    gauges[i] + " is a gauge");
            }

            Assert.That(TelemetrySchema.AggregationOf(TelemetryCounter.StepsAdvanced), Is.EqualTo(TelemetryAggregation.Sum));
            Assert.That(TelemetrySchema.AggregationOf(TelemetryCounter.ControlNodesVisited), Is.EqualTo(TelemetryAggregation.Sum));
            Assert.That(
                TelemetrySchema.AggregationOf(TelemetryCounter.ApplyDurationMicroseconds),
                Is.EqualTo(TelemetryAggregation.Sum));
        }

        [Test]
        public void CountersAddSaturateAndObserveNeverDecreases()
        {
            var set = new TelemetryCounterSet();
            set.Add(TelemetryCounter.StepsAdvanced, 3L);
            set.Add(TelemetryCounter.StepsAdvanced, 4L);
            Assert.That(set.Get(TelemetryCounter.StepsAdvanced), Is.EqualTo(7L));
            Assert.That(set.IsZero, Is.False);

            set.Set(TelemetryCounter.LiveLeases, 5L);
            set.ObserveMax(TelemetryCounter.LiveLeases, 2L);
            Assert.That(set.Get(TelemetryCounter.LiveLeases), Is.EqualTo(5L), "a gauge never decreases");
            set.ObserveMax(TelemetryCounter.LiveLeases, 9L);
            Assert.That(set.Get(TelemetryCounter.LiveLeases), Is.EqualTo(9L));

            set.Set(TelemetryCounter.RequestHighWater, long.MaxValue - 1L);
            set.Add(TelemetryCounter.RequestHighWater, 10L);
            Assert.That(set.Get(TelemetryCounter.RequestHighWater), Is.EqualTo(long.MaxValue),
                "a saturated counter must not wrap into a small one");

            set.Reset();
            Assert.That(set.IsZero, Is.True);
        }

        [Test]
        public void MergingUsesTheSchemaAggregationPolicy()
        {
            var left = new TelemetryCounterSet();
            left.Add(TelemetryCounter.StepsAdvanced, 2L);
            left.Set(TelemetryCounter.LiveLeases, 2L);
            var right = new TelemetryCounterSet();
            right.Add(TelemetryCounter.StepsAdvanced, 5L);
            right.Set(TelemetryCounter.LiveLeases, 7L);

            left.Merge(right);
            Assert.That(left.Get(TelemetryCounter.StepsAdvanced), Is.EqualTo(7L), "work sums");
            Assert.That(left.Get(TelemetryCounter.LiveLeases), Is.EqualTo(7L), "a gauge takes the maximum");
        }

        [Test]
        public void DescriptionsAreFormattedOnDemandAndSkipZeros()
        {
            var set = new TelemetryCounterSet();
            Assert.That(set.Describe(), Is.EqualTo("0"));
            set.Set(TelemetryCounter.StepsAdvanced, 12L);
            set.Set(TelemetryCounter.LiveLeases, 3L);
            string text = set.Describe();
            Assert.That(text, Is.EqualTo("steps-advanced=12;live-leases=3"));
            Assert.That(set.ToString(), Does.Contain("TelemetryCounterSet{"));
        }

        [Test]
        public void CanonicalBytesAndHashesAreDeterministicAndValueSensitive()
        {
            var first = new TelemetryCounterSet();
            first.Add(TelemetryCounter.CandidatesMatched, 11L);
            var second = new TelemetryCounterSet();
            second.Add(TelemetryCounter.CandidatesMatched, 11L);
            var third = new TelemetryCounterSet();
            third.Add(TelemetryCounter.CandidatesMatched, 12L);

            Assert.That(second.Hash().Equals(first.Hash()), Is.True);
            Assert.That(third.Hash().Equals(first.Hash()), Is.False);

            var bytes = new List<byte>();
            first.AppendCanonical(bytes);
            var again = new List<byte>();
            first.AppendCanonical(again);
            Assert.That(again, Is.EqualTo(bytes));
        }

        [Test]
        public void AFrameIsIndependentOfOwnerRegistrationOrder()
        {
            var a = new TelemetryCounterSet();
            a.Add(TelemetryCounter.StepsAdvanced, 1L);
            var b = new TelemetryCounterSet();
            b.Add(TelemetryCounter.ControlNodesVisited, 2L);

            TelemetryFrame forward = new TelemetryFrame(
                3,
                AssemblyEpoch.First,
                new LogicalStepId(9UL),
                new[]
                {
                    new TelemetrySection("gamecore.a", a),
                    new TelemetrySection("gamecore.b", b),
                });
            TelemetryFrame reversed = new TelemetryFrame(
                3,
                AssemblyEpoch.First,
                new LogicalStepId(9UL),
                new[]
                {
                    new TelemetrySection("gamecore.b", b),
                    new TelemetrySection("gamecore.a", a),
                });

            Assert.That(reversed.Hash().Equals(forward.Hash()), Is.True,
                "a frame's hash must not depend on the order owners were registered in");

            TelemetryFrame changed = new TelemetryFrame(
                3,
                AssemblyEpoch.First,
                new LogicalStepId(9UL),
                new[]
                {
                    new TelemetrySection("gamecore.a", a),
                    new TelemetrySection("gamecore.b", a),
                });
            Assert.That(changed.Hash().Equals(forward.Hash()), Is.False);
        }

        [Test]
        public void ATraceChainCoversEveryFrameInOrder()
        {
            var set = new TelemetryCounterSet();
            set.Add(TelemetryCounter.StepsAdvanced, 1L);
            TelemetryFrame one = new TelemetryFrame(0, AssemblyEpoch.First, new LogicalStepId(1UL), new[]
            {
                new TelemetrySection("gamecore.execution.driver", set),
            });
            TelemetryFrame two = new TelemetryFrame(0, AssemblyEpoch.First, new LogicalStepId(2UL), new[]
            {
                new TelemetrySection("gamecore.execution.driver", set),
            });

            TelemetryTrace forward = new TelemetryTrace("t", new[] { one, two });
            TelemetryTrace reversed = new TelemetryTrace("t", new[] { two, one });
            Assert.That(forward.ChainHash.Equals(reversed.ChainHash), Is.False, "order is part of the chain");
            Assert.That(forward.ToString(), Does.Contain("frames=2"));
        }

        [Test]
        public void ADurationSeriesFoldsIntoTheTwoCountersItNames()
        {
            var series = new TelemetrySeries(
                TelemetryCounter.StageDurationMicroseconds, TelemetryCounter.StageSampleCount);
            var stageA = new Id128(1UL, 2UL);
            var stageB = new Id128(3UL, 4UL);
            series.Record(stageA, 10L);
            series.Record(stageA, 15L);
            series.Record(stageB, 7L);

            Assert.That(series.KeyCount, Is.EqualTo(2));
            Assert.That(series.TryGet(stageA, out long total, out int count, out long max), Is.True);
            Assert.That(total, Is.EqualTo(25L));
            Assert.That(count, Is.EqualTo(2));
            Assert.That(max, Is.EqualTo(15L), "the per-key maximum survived the second sample");

            var into = new TelemetryCounterSet();
            series.WriteInto(into);
            Assert.That(into.Get(TelemetryCounter.StageDurationMicroseconds), Is.EqualTo(32L));
            Assert.That(into.Get(TelemetryCounter.StageSampleCount), Is.EqualTo(3L));

            var bytes = new List<byte>();
            series.AppendCanonical(bytes);
            Assert.That(bytes.Count, Is.GreaterThan(0));
            Assert.That(series.Entries().Count, Is.EqualTo(2));
        }

        [Test]
        public void RetentionPolicyIsExplicit()
        {
            Assert.That(TelemetryRetention.Off.Enabled, Is.False);
            Assert.That(TelemetryRetention.Off.MaxFrames, Is.EqualTo(0));
            Assert.That(TelemetryRetention.Default.Enabled, Is.True);
            Assert.That(new TelemetryRetention(5, 2).Enabled, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryRetention(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryRetention(1, -1));
        }

        [Test]
        public void ByteAccountingIsDocumentedAndNotNativeLayout()
        {
            Assert.That(TelemetryBytes.RetainedEvent(0), Is.EqualTo(TelemetryBytes.RetainedEventOverhead));
            Assert.That(TelemetryBytes.RetainedEvent(100), Is.EqualTo(100 + TelemetryBytes.RetainedEventOverhead));
            Assert.That(TelemetrySchema.FrameFormat, Is.EqualTo("gamecore.telemetry.frame/1"));
            Assert.That(TelemetrySchema.TraceFormat, Is.EqualTo("gamecore.telemetry.trace/1"));
            Assert.That(TelemetrySchema.Symbol, Is.EqualTo("GAMECORE_TELEMETRY"));
        }

        [Test]
        public void DurationsConvertHostTicksWithoutReadingAClock()
        {
            Assert.That(TelemetryDurations.TicksToMicroseconds(1000L, 1000000L), Is.EqualTo(1000L));
            Assert.That(TelemetryDurations.TicksToMicroseconds(0L, 1000L), Is.EqualTo(0L));
            Assert.That(TelemetryDurations.TicksToMicroseconds(5L, 0L), Is.EqualTo(0L));
        }
    }
}
