// GameCore.Unity.Runtime EditMode tests - job synchronization for component and non-component resources
// (GC-009, TEST-012 subset).
//
// The measurable claim is 04 section 4's: a producer remains a lifetime owner until its handle completes, a
// dependent read waits for its producer, structural playback waits for its producers, and a skipped entry never
// drops an inherited producer dependency. The fixture's producer deliberately schedules a real Burst job on a
// native container the ECS does not track and never writes that handle back to the system dependency, so the only
// thing that can make the consumer wait is this module's native dependency table plus the host stage fence.
#nullable enable
using GameCore.Contracts;
using GameCore.Unity.Runtime.Time;
using NUnit.Framework;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Time
{
    [TestFixture]
    public sealed class NativeDependencyTests
    {
        private const ulong SessionSalt = 0x47434E4154495645UL;

        private static readonly Id128 Issuer = new Id128(0x495353554552544EUL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
            TimeFixtureModule.DetachAll();
        }

        private static TimeFixtureModule CreateWorld(out UnityWorldHost host)
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = TimeFixtureRegistration.TryCreateWorld(
                world,
                operation,
                false,
                1U,
                out UnityWorldHost? worldHost,
                out TimeFixtureModule? module,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            host = worldHost!;
            return module!;
        }

        private static TimeFixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(TimeFixtureWorldState.TryRead(host.EntityWorld.EntityManager, out TimeFixtureTrail trail), Is.True);
            return trail;
        }

        [Test]
        public void ADependentReadWaitsForAnUnregisteredNonComponentProducer()
        {
            TimeFixtureModule module = CreateWorld(out UnityWorldHost host);
            host.NotifyCommandAdmitted(1U);

            host.PumpFrame(1_000_000UL);

            Assert.That(module.LastProducerHandle.Equals(default(JobHandle)), Is.False);
            Assert.That(module.LastConsumerIncoming.Equals(module.LastProducerHandle), Is.True,
                "the consumer's incoming fence must be exactly the producer's handle; without the native table it "
                + "would be the default handle and the read would race its producer (P-041)");

            TimeFixtureTrail trail = ReadTrail(host);
            Assert.That(trail.ProduceCount, Is.EqualTo(1));
            Assert.That(trail.ConsumeCount, Is.EqualTo(1));
            Assert.That(trail.ConsumedValue, Is.EqualTo(module.LastProducedValue),
                "the dependent read observed the producer's write, not a stale container value");
            Assert.That(trail.ConsumedValue, Is.EqualTo(101));
            Assert.That(host.Driver.IsFaulted, Is.False);
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "the step's fence completed the producer job");
        }

        [Test]
        public void TheStepFenceCarriesTheNonComponentHandle()
        {
            TimeFixtureModule module = CreateWorld(out UnityWorldHost host);
            host.NotifyCommandAdmitted(1U);

            host.PumpFrame(1_000_000UL);

            // The host stage fence of the producing stage holds the producer handle, so the dispatcher's publication
            // fence really completes it rather than only the ledger claiming completion (P-041, P-044).
            NativeDependencyTable native = module.Native;
            Assert.That(native.HasProduced(module.ResultSlot), Is.True);
            Assert.That(native.FenceOf(module.ResultSlot).Equals(module.LastProducerHandle), Is.True);
            Assert.That(native.ProducedCount, Is.GreaterThan(0));
            Assert.That(native.DependentReadCount, Is.GreaterThan(0));
            Assert.That(host.Driver.LastDrain.Succeeded, Is.True, "the declared buffer was consumed in the same step");
        }

        [Test]
        public void ADisabledProducerLeavesTheConsumerWithoutAWaitAndTheStepStillCommits()
        {
            TimeFixtureModule module = CreateWorld(out UnityWorldHost host);
            Assert.That(host.Systems.TryResolve(TimeFixtureKeys.ProduceSystem, out SystemDispatchTarget produce), Is.True);
            produce.ManagedSystem!.Enabled = false;

            host.NotifyCommandAdmitted(1U);
            host.PumpFrame(1_000_000UL);

            // The producer never ran, so no non-component handle exists for the consumer to wait for. The consumer is
            // told exactly that (a default incoming handle, counted as an unproduced read) instead of receiving a
            // stale or invented fence, and the declared buffer drains because nothing was produced (P-041, P-043).
            TimeFixtureTrail trail = ReadTrail(host);
            Assert.That(trail.ProduceCount, Is.EqualTo(0));
            Assert.That(trail.ConsumeCount, Is.EqualTo(1), "a producer that produced nothing does not stop its consumer");
            Assert.That(module.LastConsumerIncoming.Equals(default(JobHandle)), Is.True);
            Assert.That(module.Native.UnproducedReadCount, Is.EqualTo(1));
            Assert.That(module.Native.HasProduced(module.ResultSlot), Is.False);
            Assert.That(trail.ConsumedValue, Is.EqualTo(0), "the container was never written, and the consumer says so");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "the step still committed");
            Assert.That(host.Driver.IsFaulted, Is.False);
        }

        [Test]
        public void AnUnproducedSlotYieldsNoWaitAndIsCounted()
        {
            var native = new NativeDependencyTable(2);
            try
            {
                Assert.That(native.CombineIncoming(1).Equals(default(JobHandle)), Is.True,
                    "an unproduced slot contributes no stale handle");
                Assert.That(native.UnproducedReadCount, Is.EqualTo(1),
                    "reading an unpublished producer is counted rather than silently tolerated (P-041)");
                Assert.That(native.HasProduced(1), Is.False);
                Assert.That(native.StepFence.Equals(default(JobHandle)), Is.True);
            }
            finally
            {
                native.Dispose();
            }
        }

        [Test]
        public void StepAdmissionResetsTheTableAndKeepsThePublicationFenceHonest()
        {
            TimeFixtureModule module = CreateWorld(out UnityWorldHost host);
            NativeDependencyTable native = module.Native;
            WorldTimeDriver driver = module.Time!;

            // The temporal driver is the step admission point that resets an adopted resource table, so the checks
            // below are about the driver's per-step reset and not about the pump alone (P-041).
            host.NotifyCommandAdmitted(1U);
            driver.PumpFrame(1_000_000UL);
            JobHandle firstStepProducer = module.LastProducerHandle;

            host.NotifyCommandAdmitted(1U);
            driver.PumpFrame(2_000_000UL);
            JobHandle secondStepProducer = module.LastProducerHandle;

            Assert.That(secondStepProducer.Equals(firstStepProducer), Is.False,
                "each step produces a fresh handle; no step inherits the previous step's fence");
            Assert.That(native.FenceOf(module.ResultSlot).Equals(secondStepProducer), Is.True,
                "the slot holds only this step's handle");
            Assert.That(native.ProducedCount, Is.EqualTo(1), "the table's per-step counters were reset at admission");
            Assert.That(ReadTrail(host).ConsumedValue, Is.EqualTo(102));
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0));
        }

        [Test]
        public void APlaybackBindingCombinesExactlyItsProducerSlots()
        {
            var native = new NativeDependencyTable(2);
            try
            {
                JobHandle producer = new TimeFixtureNoOpJob().Schedule(default(JobHandle));
                native.Store(
                    0,
                    producer,
                    0,
                    TimeFixtureKeys.ProduceStage,
                    TimeFixtureKeys.ProduceSystem,
                    AssemblyEpoch.First,
                    LogicalStepId.Zero,
                    null,
                    null);

                Assert.That(native.CombineIncoming(0).Equals(producer), Is.True);
                Assert.That(native.CombineIncoming(new[] { 0, 1 }).Equals(producer), Is.True,
                    "an unproduced slot adds no handle to the combination");
                Assert.That(native.StepFence.Equals(producer), Is.True);
                Assert.That(native.CompleteStepFence(), Is.True);
                Assert.That(native.CompletionFailureCount, Is.EqualTo(0));
            }
            finally
            {
                native.Dispose();
            }
        }

        [Test]
        public void DisposingTheTableTwiceIsIdempotent()
        {
            var native = new NativeDependencyTable(1);
            native.Dispose();
            native.Dispose();
            Assert.That(native.IsCreated, Is.False);
        }
    }

    /// <summary>Minimal real job, used where only a genuine <see cref="JobHandle"/> is needed.</summary>
    public struct TimeFixtureNoOpJob : IJob
    {
        public void Execute()
        {
        }
    }
}
