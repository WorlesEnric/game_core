// GC-028 conformance closure for P-043's fan-out rule.
//
// 00 P-043: "Duplicate producers are legal when registered; fan-out uses explicit immutable read ports."
//
// The bounded-buffer suite exercises producers, capacities, overflow, drain validation and the next-step carry,
// but no existing test declared or refused a read port. `StepMessageSchedule.TryAddReadPort` is the only place the
// kernel admits an explicit fan-out, so this file asserts its three normative properties:
//
//   * a read port is admitted only for a buffer that has a contract (an undeclared fan-out is refused, not
//     tolerated), and the refusal names the buffer;
//   * several readers may fan out from one buffer, and a read port is immutable: admitting one neither changes the
//     buffer's declared consuming owner nor its capacity;
//   * a second contract for the same buffer is refused, so a "second reader" can never be smuggled in as a second
//     buffer declaration (P-043).
//
// The types are engine-free (`GameCore.Execution.Messages`), so this runs in-process with no Unity; the same
// sources compile into GameCore.Unity.Runtime for the Unity suite.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>Explicit immutable read ports and one contract per buffer (P-043).</summary>
    [TestFixture]
    public sealed class ReadPortConformanceTests
    {
        private static readonly OwnerId Owner = new OwnerId(new Id128(0x525054434F4E4652UL, 0x10UL));

        private static readonly OwnerId OtherOwner = new OwnerId(new Id128(0x525054434F4E4652UL, 0x11UL));

        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x525054434F4E4652UL, 0x20UL)), 1U);

        private static readonly StageId OwnerStage = new StageId(new Id128(0x525054434F4E4652UL, 0x30UL));

        private static readonly StageId ConsumerStage = new StageId(new Id128(0x525054434F4E4652UL, 0x31UL));

        private static readonly StageId ReaderStage = new StageId(new Id128(0x525054434F4E4652UL, 0x32UL));

        private static readonly FactoryKey Producer = new FactoryKey(new Id128(0x525054434F4E4652UL, 0x40UL), 1U);

        private static readonly FactoryKey ReaderOne = new FactoryKey(new Id128(0x525054434F4E4652UL, 0x41UL), 1U);

        private static readonly FactoryKey ReaderTwo = new FactoryKey(new Id128(0x525054434F4E4652UL, 0x42UL), 1U);

        private static readonly BufferId Buffer = new BufferId(new Id128(0x525054434F4E4652UL, 0x50UL));

        private static readonly BufferId UndeclaredBuffer = new BufferId(new Id128(0x525054434F4E4652UL, 0x51UL));

        private static MessageBufferDescriptor Descriptor(int capacity = 4) =>
            new MessageBufferDescriptor(
                Buffer,
                Schema,
                new[] { Producer },
                Owner,
                OwnerStage,
                ConsumerStage,
                Producer,
                BufferLifetime.Step,
                capacity,
                byteCapacity: 64,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

        private static StepMessageSchedule ScheduleWithOneBuffer(int capacity = 4)
        {
            var schedule = new StepMessageSchedule { NextStepCapacity = 2 };
            Assert.That(schedule.TryDeclare(Descriptor(capacity), out string failure), Is.True, failure);
            return schedule;
        }

        [Test]
        public void AReadPortForADeclaredBufferIsAdmitted()
        {
            StepMessageSchedule schedule = ScheduleWithOneBuffer();

            Assert.That(schedule.ReadPortCount, Is.EqualTo(0), "a buffer starts with no reader but its owner");

            Assert.That(
                schedule.TryAddReadPort(new BufferReadPort(Buffer, ReaderOne, ReaderStage), out string failure),
                Is.True,
                failure);

            Assert.That(schedule.ReadPortCount, Is.EqualTo(1));
            Assert.That(schedule.ReadPorts[0].Buffer, Is.EqualTo(Buffer));
            Assert.That(schedule.ReadPorts[0].Reader, Is.EqualTo(ReaderOne));
            Assert.That(schedule.ReadPorts[0].Stage, Is.EqualTo(ReaderStage));
        }

        [Test]
        public void AReadPortForAnUndeclaredBufferIsRefusedAndNamesIt()
        {
            StepMessageSchedule schedule = ScheduleWithOneBuffer();

            Assert.That(
                schedule.TryAddReadPort(new BufferReadPort(UndeclaredBuffer, ReaderOne, ReaderStage), out string failure),
                Is.False,
                "a fan-out must name a buffer that has a contract (P-043)");
            Assert.That(failure, Is.Not.Empty);
            Assert.That(failure, Does.Contain(UndeclaredBuffer.ToString()),
                "the refusal names the buffer it could not resolve");
            Assert.That(schedule.ReadPortCount, Is.EqualTo(0), "a refused port is not retained");
        }

        [Test]
        public void SeveralReadersMayFanOutFromOneBufferWithoutBecomingItsOwner()
        {
            StepMessageSchedule schedule = ScheduleWithOneBuffer(capacity: 7);

            Assert.That(schedule.TryAddReadPort(new BufferReadPort(Buffer, ReaderOne, ReaderStage), out string first), Is.True, first);
            Assert.That(schedule.TryAddReadPort(new BufferReadPort(Buffer, ReaderTwo, ReaderStage), out string second), Is.True, second);

            Assert.That(schedule.ReadPortCount, Is.EqualTo(2), "fan-out to several readers is legal (P-043)");
            Assert.That(schedule.Descriptors.Count, Is.EqualTo(1),
                "a read port is not a second buffer contract");

            MessageBufferDescriptor declared = schedule.Descriptors[0];
            Assert.That(declared.Owner, Is.EqualTo(Owner),
                "fan-out never transfers the consuming owner (P-043)");
            Assert.That(declared.ConsumerStage, Is.EqualTo(ConsumerStage),
                "fan-out never moves the consuming stage");
            Assert.That(declared.Capacity, Is.EqualTo(7), "a read port does not resize the producer's buffer");
            Assert.That(declared.Cancellation, Is.EqualTo(BufferCancellationPolicy.Drain));
        }

        [Test]
        public void ASecondBufferContractForTheSameBufferIsRefusedRatherThanMerged()
        {
            StepMessageSchedule schedule = ScheduleWithOneBuffer();

            var rival = new MessageBufferDescriptor(
                Buffer,
                Schema,
                new[] { Producer },
                OtherOwner,
                OwnerStage,
                ConsumerStage,
                Producer,
                BufferLifetime.Step,
                capacity: 8,
                byteCapacity: 64,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            Assert.That(schedule.TryDeclare(rival, out string failure), Is.False,
                "one buffer has exactly one contract with one consuming owner (P-043)");
            Assert.That(failure, Is.Not.Empty);
            Assert.That(schedule.Descriptors.Count, Is.EqualTo(1));
            Assert.That(schedule.Descriptors[0].Owner, Is.EqualTo(Owner),
                "the first contract stays authoritative");
        }

        [Test]
        public void ADeclaredBufferRefusesADefaultZeroIdentityAndAConsumerlessLifetime()
        {
            var schedule = new StepMessageSchedule { NextStepCapacity = 2 };

            Assert.That(
                schedule.TryDeclare(
                    new MessageBufferDescriptor(
                        default(BufferId),
                        Schema,
                        new[] { Producer },
                        Owner,
                        OwnerStage,
                        ConsumerStage,
                        Producer,
                        BufferLifetime.Step,
                        capacity: 4,
                        byteCapacity: 64,
                        BufferOverflowPolicy.RejectBeforeMutation,
                        BufferCancellationPolicy.Drain),
                    out string noIdentity),
                Is.False,
                "a default zero buffer id is not a contract (P-043)");
            Assert.That(noIdentity, Is.Not.Empty);

            Assert.That(
                schedule.TryDeclare(
                    new MessageBufferDescriptor(
                        Buffer,
                        Schema,
                        new[] { Producer },
                        Owner,
                        OwnerStage,
                        default(StageId),
                        Producer,
                        BufferLifetime.Step,
                        capacity: 4,
                        byteCapacity: 64,
                        BufferOverflowPolicy.RejectBeforeMutation,
                        BufferCancellationPolicy.Drain),
                    out string noConsumer),
                Is.False,
                "commit validation needs the consuming stage (P-043)");
            Assert.That(noConsumer, Is.Not.Empty);
            Assert.That(schedule.BufferCount, Is.EqualTo(0));
        }
    }
}
