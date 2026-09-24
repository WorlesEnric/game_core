#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>
    /// Dispatch-table tests (GC-005, TEST-013/TEST-018). The compiled order installed at the assembly fence is the
    /// executed order, an ill-formed table never reaches execution, and a produced buffer whose consumer stage did
    /// not run fails commit validation (P-039 to P-041, P-043).
    /// </summary>
    [TestFixture]
    public sealed class GuardedDispatchPlanTests
    {
        private static FactoryKey Key(ulong ordinal)
            => new FactoryKey(new Id128(0x4743464958545552UL, ordinal), 1U);

        private static StageId Stage(ulong ordinal)
            => new StageId(new Id128(0x4743464958545552UL, 0x1000UL + ordinal));

        private static GuardedDispatchEntry Entry(int index, int stageIndex, params int[] predecessors)
        {
            return new GuardedDispatchEntry(
                Stage((ulong)stageIndex),
                Key((ulong)index),
                SystemDispatchKind.ManagedSystem,
                index,
                stageIndex,
                predecessors);
        }

        private static GuardedDispatchPlan ThreeStagePlan()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                Entry(0, 0),
                Entry(1, 1, 0),
                Entry(2, 2, 1),
            };

            return new GuardedDispatchPlan(entries, null, 3);
        }

        [Test]
        public void WellFormedPlanProjectsOntoTheEpochBoundTable()
        {
            GuardedDispatchPlan plan = ThreeStagePlan();
            Assert.That(plan.TryValidate(out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(plan.IsEmpty, Is.False);
            Assert.That(plan.StageCount, Is.EqualTo(3));

            var epoch = new AssemblyEpoch(7UL);
            OrderedDispatchTable table = plan.ToOrderedTable(epoch);
            Assert.That(table.Epoch, Is.EqualTo(epoch));
            Assert.That(table.Entries.Count, Is.EqualTo(3));
            Assert.That(table.IsWellFormed(), Is.True);
            Assert.That(table.Entries[1].Stage, Is.EqualTo(Stage(1)));
            Assert.That(table.Entries[1].DispatchIndex, Is.EqualTo(1));
        }

        [Test]
        public void NonAscendingDispatchIndicesAreRejected()
        {
            var entries = new List<GuardedDispatchEntry> { Entry(1, 0), Entry(1, 1, 0) };
            var plan = new GuardedDispatchPlan(entries, null, 2);

            Assert.That(plan.TryValidate(out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.AmbiguousOrder));
            Assert.That(detail, Does.Contain("ascending"));
        }

        [Test]
        public void DuplicateSystemKeysAreRejected()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                new GuardedDispatchEntry(Stage(0), Key(9), SystemDispatchKind.ManagedSystem, 0, 0, null),
                new GuardedDispatchEntry(Stage(1), Key(9), SystemDispatchKind.ManagedSystem, 1, 1, new[] { 0 }),
            };

            var plan = new GuardedDispatchPlan(entries, null, 2);
            Assert.That(plan.TryValidate(out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("twice"));
        }

        [Test]
        public void ForwardOrSelfStageEdgesAreRejected()
        {
            var entries = new List<GuardedDispatchEntry> { Entry(0, 1, 1) };
            var plan = new GuardedDispatchPlan(entries, null, 2);

            Assert.That(plan.TryValidate(out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(detail, Does.Contain("backward"));
        }

        [Test]
        public void StageIndexesOutsideTheDeclaredCountAreRejected()
        {
            var entries = new List<GuardedDispatchEntry> { Entry(0, 4) };
            var plan = new GuardedDispatchPlan(entries, null, 2);

            Assert.That(plan.TryValidate(out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.AmbiguousOrder));
            Assert.That(detail, Does.Contain("stage count"));
        }

        [Test]
        public void ProducerWithoutItsConsumerStageFailsDrainValidation()
        {
            var buffer = new BufferId(new Id128(0x4743464958545552UL, 0x2000UL));
            var binding = new BufferBinding(buffer, new[] { Key(1) }, Stage(2));
            var entries = new List<GuardedDispatchEntry> { Entry(0, 0), Entry(1, 1, 0), Entry(2, 2, 1) };
            var plan = new GuardedDispatchPlan(entries, new[] { binding }, 3);

            var dispatched = new HashSet<FactoryKey> { Key(0), Key(1) };
            var stageDispatched = new[] { true, true, false };

            DrainValidation drains = plan.ValidateDrains(dispatched, stageDispatched);
            Assert.That(drains.Succeeded, Is.False, "A produced step buffer whose consumer stage did not run must fail the commit.");
            Assert.That(drains.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(drains.UnconsumedBuffer, Is.EqualTo(buffer));

            stageDispatched[2] = true;
            Assert.That(plan.ValidateDrains(dispatched, stageDispatched).Succeeded, Is.True);

            stageDispatched[2] = false;
            var noProducer = new HashSet<FactoryKey> { Key(0) };
            Assert.That(plan.ValidateDrains(noProducer, stageDispatched).Succeeded, Is.True, "A buffer whose producers never ran needs no consumer.");
        }

        [Test]
        public void EmptyPlanIsAValidNoOp()
        {
            Assert.That(GuardedDispatchPlan.Empty.IsEmpty, Is.True);
            Assert.That(GuardedDispatchPlan.Empty.TryValidate(out _, out _), Is.True);
            Assert.That(GuardedDispatchPlan.Empty.ToOrderedTable(AssemblyEpoch.First).Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void IdSequenceIsDeterministicAndNeverWraps()
        {
            var first = new IdSequence(0x53455155454E4345UL);
            var second = new IdSequence(0x53455155454E4345UL);

            for (ulong i = 1UL; i <= 4UL; i++)
            {
                Id128 a = first.Next();
                Id128 b = second.Next();
                Assert.That(a, Is.EqualTo(b), "The sequence is reproducible from its salt alone (P-008).");
                Assert.That(a.High, Is.EqualTo(0x53455155454E4345UL));
                Assert.That(a.Low, Is.EqualTo(i));
            }

            var exhausted = new IdSequence(1UL, ulong.MaxValue);
            Assert.Throws<InvalidOperationException>(() => exhausted.Next());
        }
    }
}
