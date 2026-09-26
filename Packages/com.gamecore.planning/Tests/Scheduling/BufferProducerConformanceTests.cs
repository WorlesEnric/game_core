// GC-028 conformance closure for P-043's producer/port rules (GC-009's compiler).
//
// 00 P-043 says: "Each buffer contract declares schema, lifetime, producer keys, exactly one consuming owner,
// consumption stage, order key, capacity, overflow behavior, and drain/cancel policy. Duplicate producers are
// legal when registered" and "Cycles between stages/ports require explicit next-step scheduling."
//
// The existing schedule-compiler suite covers one producer, a duplicate buffer *contract*, a missing consumer, a
// missing owner, a port naming an undeclared buffer and a port/contract direction mismatch. Two normative cases
// had no executable assertion:
//
//   * several registered producers of one buffer bind to one consumer in canonical order, independent of the
//     order the manifest declares them in (P-043 with P-008);
//   * a buffer-derived producer edge that closes a cycle with a declared stage edge is rejected as `Cycle`,
//     because a buffer edge is a real scheduling edge and not a licence to relax the stage graph (P-040, P-043).
//
// The fixtures are this package's own `ScheduleFixtures`, so this file adds no second declaration vocabulary.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Scheduling;
using GameCore.Planning.Tests.Scheduling;
using NUnit.Framework;

namespace GameCore.Planning.Scheduling.Tests
{
    /// <summary>Several registered producers, and a buffer edge that closes a stage cycle (P-043).</summary>
    [TestFixture]
    public sealed class BufferProducerConformanceTests
    {
        private static CompiledSchedule Compile(ScheduleDeclarations declarations)
        {
            ScheduleCompilation compilation = ScheduleCompiler.Compile(declarations);
            Assert.That(compilation.Succeeded, Is.True, compilation.Explain());
            Assert.That(compilation.Schedule, Is.Not.Null);
            return compilation.Schedule!;
        }

        /// <summary>One buffer, two producing stages and one consuming stage; the declaration order is a parameter.</summary>
        private static ScheduleDeclarations TwoProducers(bool declareProducersReversed)
        {
            StageId producerOne = ScheduleFixtures.Stage(1UL);
            StageId producerTwo = ScheduleFixtures.Stage(2UL);
            StageId consumer = ScheduleFixtures.Stage(3UL);
            FactoryKey producerOneSystem = ScheduleFixtures.Key(1UL);
            FactoryKey producerTwoSystem = ScheduleFixtures.Key(2UL);
            FactoryKey consumerSystem = ScheduleFixtures.Key(3UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producerOne,
                    new[] { ScheduleFixtures.System(producerOneSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producerOne) }),
                ScheduleFixtures.StageSpec(
                    producerTwo,
                    new[] { ScheduleFixtures.System(producerTwoSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producerTwo) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(consumerSystem) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            // Owner stage is the consumer: this buffer lives for the consuming stage's own lifetime.
            var producers = declareProducersReversed
                ? new[] { producerTwoSystem, producerOneSystem }
                : new[] { producerOneSystem, producerTwoSystem };

            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, producers, consumer, consumer),
            };

            return new ScheduleDeclarations(stages, buffers);
        }

        [Test]
        public void SeveralRegisteredProducersBindToTheOneConsumerInCanonicalOrder()
        {
            CompiledSchedule schedule = Compile(TwoProducers(declareProducersReversed: false));

            Assert.That(schedule.BufferBindings.Count, Is.EqualTo(1), "one buffer has exactly one contract");
            BufferBinding binding = schedule.BufferBindings[0];
            Assert.That(binding.Buffer, Is.EqualTo(ScheduleFixtures.Buffer(1UL)));
            Assert.That(binding.ConsumerStage, Is.EqualTo(ScheduleFixtures.Stage(3UL)),
                "duplicate producers never create a second consuming owner (P-043)");
            Assert.That(
                binding.Producers,
                Is.EqualTo(new[] { ScheduleFixtures.Key(1UL), ScheduleFixtures.Key(2UL) }),
                "every registered producer is bound, in canonical ascending key order");

            Assert.That(schedule.PlaybackPoints.Count, Is.EqualTo(1));
            SchedulePlaybackPoint point = schedule.PlaybackPoints[0];
            Assert.That(point.ProducerStageIndexes.Count, Is.EqualTo(2),
                "both producing stages order before the one consumer");
            Assert.That(point.ProducerSystems.Count, Is.EqualTo(2));
        }

        [Test]
        public void DeclarationOrderOfSeveralProducersChangesNeitherTheBindingNorTheHash()
        {
            CompiledSchedule declared = Compile(TwoProducers(declareProducersReversed: false));
            CompiledSchedule reversed = Compile(TwoProducers(declareProducersReversed: true));

            Assert.That(
                reversed.BufferBindings[0].Producers,
                Is.EqualTo(declared.BufferBindings[0].Producers),
                "producer order is canonical, not declaration order (P-008)");
            Assert.That(reversed.Hash, Is.EqualTo(declared.Hash),
                "the same semantic inputs produce the same schedule hash (P-027, TEST-022)");
        }

        [Test]
        public void ABufferEdgeThatClosesAStageCycleIsRejectedAsACycle()
        {
            StageId producer = ScheduleFixtures.Stage(1UL);
            StageId consumer = ScheduleFixtures.Stage(2UL);
            FactoryKey producerSystem = ScheduleFixtures.Key(1UL);
            BufferId buffer = ScheduleFixtures.Buffer(1UL);

            // The buffer's producer edge runs producer -> consumer, and the producing stage additionally declares
            // that it must run after the consuming stage. The two edges close a cycle the compiler must refuse.
            var stages = new List<StageSpec>
            {
                ScheduleFixtures.StageSpec(
                    producer,
                    new[] { ScheduleFixtures.System(producerSystem) },
                    requiredAfter: new[] { consumer },
                    ports: new[] { new BufferPort(buffer, PortDirection.Producer, producer) }),
                ScheduleFixtures.StageSpec(
                    consumer,
                    new[] { ScheduleFixtures.System(ScheduleFixtures.Key(2UL)) },
                    ports: new[] { new BufferPort(buffer, PortDirection.Consumer, consumer) }),
            };

            var buffers = new List<BufferSpec>
            {
                ScheduleFixtures.BufferSpec(buffer, new[] { producerSystem }, producer, consumer),
            };

            ScheduleCompilation compilation = ScheduleCompiler.Compile(new ScheduleDeclarations(stages, buffers));

            Assert.That(compilation.Succeeded, Is.False,
                "a buffer-derived edge participates in cycle detection (P-040, P-043): " + compilation.Explain());
            Assert.That(compilation.Schedule, Is.Null, "a rejected compilation never exposes a partial schedule");
            Assert.That(compilation.Code, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(compilation.CyclePath.Count, Is.GreaterThanOrEqualTo(3),
                "the witness path names the declarations that close the cycle");
            Assert.That(compilation.CyclePath[0], Is.EqualTo(compilation.CyclePath[compilation.CyclePath.Count - 1]),
                "a cycle path starts and ends at the same stage");

            bool sawStageCycleWitness = false;
            for (int i = 0; i < compilation.Witnesses.Count; i++)
            {
                if (compilation.Witnesses[i].Kind == ScheduleWitnessKind.StageCycle)
                {
                    sawStageCycleWitness = true;
                }

                Assert.That(compilation.Witnesses[i].Detail, Is.Not.Empty);
            }

            Assert.That(sawStageCycleWitness, Is.True, "the rejection names the stage cycle it found");
        }
    }
}
