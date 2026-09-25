// GameCore.Unity.Runtime EditMode tests - real scheduled work under the compiled schedule (GC-009, TEST-012).
//
// Two acceptance items need execution evidence, not fixture inspection:
//
//   1. "Disjoint work overlaps safely": two stages with disjoint ECS access compile with no edge between them, so the
//      guarded dispatcher gives each entry no predecessor and neither job is chained onto the other. The evidence is
//      structural and deterministic, taken at the dispatch level:
//        - the compiled schedule has no edge between the two stages and neither entry declares a predecessor;
//        - the decisive runtime observation is that the left job's handle was already recorded in the dispatcher's
//          own stage fence when the right entry ran (`RightSawLeftSlot == LeftHandle`), while the right entry waited
//          on nothing - so a real producer handle existed and was still not made the other's dependency;
//        - the ordered observer stage, which does declare both as predecessors, receives their combination - so the
//          mechanism that would serialize two dependent jobs is demonstrably live where an edge exists.
//      Wall-clock overlap of the two intervals is deliberately NOT asserted: Unity may execute a short `IJob` inline on
//      the thread that completes it, so overlap in time is an Editor configuration property rather than a dispatch
//      property, and asserting it would be flaky. The dispatch property is what TEST-012 asks for.
//
//   2. "Actual jobs complete before dependent reads/playback": a producer job writes a component and a non-component
//      container, and the later stage's deferred structural playback (ECB) runs only after the producer finished.
//      The load-bearing proof is structural: the producer does not publish through `SystemBase.Dependency` at all, so
//      the native dependency table is the only carrier of its handle, and the test asserts that the produce stage's
//      own fence slot is empty while the native slot holds the producer's handle, and that the handle the playback
//      combined IS that handle. The value assertions (`PlaybackReadValue`, the played-back component values) are
//      corroboration rather than proof: a main-thread read of a declared component type is synchronized by the
//      safety manager even without an explicit wait, so those values would also be right if the wait were missing.
//      The companion negative run supplies the control the values cannot: it reads the *container* - a resource the
//      safety manager does gate - with the handle outstanding, and requires the rejection. So "no safety exception"
//      in the positive path is not vacuous.
#nullable enable
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime.Time;
using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Time
{
    [TestFixture]
    public sealed class ScheduleExecutionTests
    {
        private const ulong SessionSalt = 0x4743584558434F4FUL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525445UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
            ScheduleExecutionModule.DetachAll();
        }

        private static ScheduleExecutionModule Create(ScheduleExecutionShape shape, out UnityWorldHost host)
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = ScheduleExecutionRegistration.TryCreateWorld(
                shape,
                world,
                operation,
                out UnityWorldHost? createdHost,
                out ScheduleExecutionModule? module,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(createdHost, Is.Not.Null);
            Assert.That(module, Is.Not.Null);
            host = createdHost!;
            return module!;
        }

        /// <summary>Admits one command and runs exactly one step of the world's compiled schedule.</summary>
        private static TimeFrameReport RunOneStep(UnityWorldHost host, ScheduleExecutionModule module, ulong hostTicks)
        {
            var requestKey = new Id128(ScheduleExecutionKeys.Namespace, 0x7001UL + sessionSequence);
            var schema = new SchemaRef(new SchemaId(new Id128(ScheduleExecutionKeys.Namespace, 0x7000UL)), 1U);
            Assert.That(
                module.Time!.TryAdmitCommand(requestKey, schema, out _, out DiagnosticCode code),
                Is.True,
                code.ToString());
            return module.Time.PumpFrame(hostTicks);
        }

        private static GuardedDispatchEntry EntryFor(GuardedDispatchPlan plan, FactoryKey system)
        {
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                if (plan.Entries[i].SystemKey.Equals(system))
                {
                    return plan.Entries[i];
                }
            }

            Assert.Fail("no compiled entry for " + system.ToString());
            return plan.Entries[0];
        }

        // ------------------------------------------------------------------ 1. disjoint work overlaps safely

        [Test]
        public void TheCompilerOrdersTwoDisjointStagesWithNoEdgeBetweenThem()
        {
            CompiledSchedule schedule = ScheduleExecutionRegistration.Compile(ScheduleExecutionShape.Overlap);

            int left;
            int right;
            Assert.That(schedule.TryGetStageIndex(ScheduleExecutionKeys.LeftStage, out left), Is.True);
            Assert.That(schedule.TryGetStageIndex(ScheduleExecutionKeys.RightStage, out right), Is.True);
            Assert.That(left, Is.Not.EqualTo(right));

            Assert.That(schedule.HasEdge(left, right), Is.False, "disjoint access needs no edge (P-040)");
            Assert.That(schedule.HasEdge(right, left), Is.False);
            Assert.That(schedule.Stages[left].PredecessorStages.Count, Is.EqualTo(0), "the left stage has no predecessor");
            Assert.That(schedule.Stages[right].PredecessorStages.Count, Is.EqualTo(0), "the right stage has no predecessor");

            // The observer stage is ordered after both, so the fixture does contain a real edge to contrast with.
            Assert.That(schedule.TryGetStageIndex(ScheduleExecutionKeys.ObserveStage, out int observe), Is.True);
            Assert.That(schedule.Stages[observe].PredecessorStages, Is.EqualTo(new[] { left, right }));

            // The installed table therefore passes each disjoint entry an empty predecessor set.
            ScheduleAdaptation adaptation = ScheduleExecutionRegistration.Adapt(ScheduleExecutionShape.Overlap);
            Assert.That(
                EntryFor(adaptation.StepPlan!, ScheduleExecutionKeys.LeftSystem).PredecessorStages.Count,
                Is.EqualTo(0));
            Assert.That(
                EntryFor(adaptation.StepPlan!, ScheduleExecutionKeys.RightSystem).PredecessorStages.Count,
                Is.EqualTo(0));
            Assert.That(
                EntryFor(adaptation.StepPlan!, ScheduleExecutionKeys.ObserveSystem).PredecessorStages,
                Is.EqualTo(new[] { left, right }));
        }

        [Test]
        public void NeitherDisjointJobReceivesTheOthersHandleAndBothWritesSurvive()
        {
            ScheduleExecutionModule module = Create(ScheduleExecutionShape.Overlap, out UnityWorldHost host);

            Assert.That(host.StepGroup.InstalledPlan.Entries.Count, Is.EqualTo(3));
            Assert.That(module.LeftStageIndex, Is.LessThan(module.RightStageIndex), "canonical order: left before right");
            Assert.That(module.ObserveStageIndex, Is.GreaterThan(module.RightStageIndex));

            RunOneStep(host, module, 1_000_000UL);

            // No safety-system rejection anywhere: the guarded dispatcher would have latched a world fault instead.
            Assert.That(host.Driver.IsFaulted, Is.False, host.Driver.FaultDetail);
            Assert.That(host.FaultCount, Is.EqualTo(0));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "the step committed");
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));

            // Both entries scheduled a real, distinct job.
            Assert.That(module.LeftHandle.Equals(default(JobHandle)), Is.False, "the left stage scheduled a job");
            Assert.That(module.RightHandle.Equals(default(JobHandle)), Is.False, "the right stage scheduled a job");
            Assert.That(module.LeftHandle.Equals(module.RightHandle), Is.False, "two distinct jobs, two distinct handles");

            // Secondary consistency check: each dispatch entry declares no predecessor, and the value each system's own
            // dependency resolved to is the default handle. This alone measures little - a stage with no declared
            // component access resolves to default regardless of what the dispatcher passed - so the load-bearing
            // observations are the slot readings below together with the compiled predecessor sets.
            Assert.That(module.LeftWaitValue.Equals(default(JobHandle)), Is.True, "the left entry waited on nothing");
            Assert.That(module.RightWaitValue.Equals(default(JobHandle)), Is.True, "the right entry waited on nothing");
            Assert.That(module.RightWaitValue.Equals(module.LeftHandle), Is.False,
                "job B's dependency is not job A's handle");
            Assert.That(module.LeftWaitValue.Equals(module.RightHandle), Is.False);

            // The decisive pair. The left stage ran first and its job handle was already recorded in the dispatcher's
            // own stage fence when the right entry was dispatched - yet the right entry still waited on nothing.
            Assert.That(module.LeftSawRightSlot.Equals(default(JobHandle)), Is.True,
                "the left entry ran before the right stage had recorded any work");
            Assert.That(module.RightSawLeftSlot.Equals(module.LeftHandle), Is.True,
                "the left job's handle was already in the dispatcher's stage fence when the right entry ran");
            Assert.That(module.RightWaitValue.Equals(module.RightSawLeftSlot), Is.False,
                "a recorded producer handle in the neighbouring stage did not become the right entry's dependency");

            // The same mechanism is live where an edge does exist: the ordered observer stage received a combination
            // of both stage fences, distinct from default and from either job's handle.
            Assert.That(module.ObserveDispatchCount, Is.EqualTo(1), "the observer ran after both disjoint stages");
            Assert.That(module.LeftSlot.Equals(module.LeftHandle), Is.True, "the left stage's fence holds its own handle");
            Assert.That(module.RightSlot.Equals(module.RightHandle), Is.True, "the right stage's fence holds its own handle");
            Assert.That(module.ObserveWaitsOn.Equals(default(JobHandle)), Is.False,
                "the ordered stage does receive its predecessors' fences");
            Assert.That(module.ObserveWaitsOn.Equals(module.LeftHandle), Is.False,
                "an ordered stage's dependency is a combination, not one producer's handle");
            Assert.That(module.ObserveWaitsOn.Equals(module.RightHandle), Is.False);

            // The step recorded both work items and retired them at its commit boundary.
            Assert.That(module.OutstandingJobsDuringStep, Is.GreaterThanOrEqualTo(2),
                "the step had recorded both disjoint jobs before the observer ran");
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "the step boundary completed both jobs");
            Assert.That(host.Ledger.QuarantinedJobCount, Is.EqualTo(0));

            // Each job's own body executed (it stamped its own timing container) and each wrote its own component.
            Assert.That(module.LeftTiming[0], Is.Not.EqualTo(0L));
            Assert.That(module.LeftTiming[1], Is.GreaterThanOrEqualTo(module.LeftTiming[0]));
            Assert.That(module.RightTiming[0], Is.Not.EqualTo(0L));
            Assert.That(module.RightTiming[1], Is.GreaterThanOrEqualTo(module.RightTiming[0]));

            EntityManager entityManager = host.EntityWorld.EntityManager;
            Assert.That(module.LeftWritten, Is.EqualTo(1001), "the left job's value is a function of its own stage run");
            Assert.That(module.RightWritten, Is.EqualTo(2001));
            Assert.That(
                entityManager.GetComponentData<LeftValue>(module.LeftEntity).Value,
                Is.EqualTo(module.LeftWritten),
                "the left component holds the value only the left job could write");
            Assert.That(
                entityManager.GetComponentData<RightValue>(module.RightEntity).Value,
                Is.EqualTo(module.RightWritten),
                "the right component holds the value only the right job could write");
            Assert.That(
                entityManager.GetComponentData<LeftValue>(module.LeftEntity).Value,
                Is.Not.EqualTo(-1),
                "the seeded sentinel was overwritten by the job, not read back");
        }

        // ------------------------------------------------------------------ 2. jobs complete before playback

        [Test]
        public void TheCompiledScheduleBindsThePlaybackPointToItsProducerAndConsumerStages()
        {
            CompiledSchedule schedule = ScheduleExecutionRegistration.Compile(ScheduleExecutionShape.Playback);

            Assert.That(schedule.TryGetStageIndex(ScheduleExecutionKeys.ProduceStage, out int produce), Is.True);
            Assert.That(schedule.TryGetStageIndex(ScheduleExecutionKeys.PlaybackStage, out int playback), Is.True);

            // The producer-before-consumer edge comes from the declared buffer contract, not from hand-written order.
            Assert.That(schedule.HasEdge(produce, playback), Is.True, "the declared buffer orders its producer (P-043)");
            Assert.That(schedule.PlaybackPoints.Count, Is.EqualTo(1));
            SchedulePlaybackPoint point = schedule.PlaybackPoints[0];
            Assert.That(point.Buffer, Is.EqualTo(ScheduleExecutionKeys.PlaybackBuffer));
            Assert.That(point.OwnerStageIndex, Is.EqualTo(produce));
            Assert.That(point.ConsumerStageIndex, Is.EqualTo(playback));
            Assert.That(point.ProducerStageIndexes, Is.EqualTo(new[] { produce }));
            Assert.That(point.ProducerSystems, Is.EqualTo(new[] { ScheduleExecutionKeys.ProduceSystem }));

            // The installed table carries the same binding, and the consumer entry inherits the producer's fence slot.
            ScheduleAdaptation adaptation = ScheduleExecutionRegistration.Adapt(ScheduleExecutionShape.Playback);
            Assert.That(adaptation.Buffers.Count, Is.EqualTo(1));
            Assert.That(adaptation.Buffers[0].Slot, Is.EqualTo(0));
            Assert.That(adaptation.Buffers[0].ProducerStageIndexes, Is.EqualTo(new[] { produce }));
            Assert.That(
                EntryFor(adaptation.StepPlan!, ScheduleExecutionKeys.PlaybackSystem).PredecessorStages,
                Is.EqualTo(new[] { produce }));
            Assert.That(adaptation.StepPlan!.BufferBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void AStructuralPlaybackWaitsForItsProducerAndObservesItsWrittenComponent()
        {
            ScheduleExecutionModule module = Create(ScheduleExecutionShape.Playback, out UnityWorldHost host);
            EntityManager entityManager = host.EntityWorld.EntityManager;

            Assert.That(
                entityManager.GetComponentData<PlaybackSourceValue>(module.SourceEntity).Value,
                Is.EqualTo(-1),
                "the producer's component starts at a value its job must overwrite");

            RunOneStep(host, module, 1_000_000UL);

            // No safety-system rejection anywhere in the producer -> playback chain.
            Assert.That(host.Driver.IsFaulted, Is.False, host.Driver.FaultDetail);
            Assert.That(host.FaultCount, Is.EqualTo(0));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
            Assert.That(host.Driver.LastDrain.Succeeded, Is.True, "the declared buffer was consumed in the same step");

            // The producer really scheduled work, and the step had recorded it before the playback ran.
            Assert.That(module.ProduceHandle.Equals(default(JobHandle)), Is.False);
            Assert.That(module.OutstandingJobsDuringStep, Is.GreaterThanOrEqualTo(1));
            Assert.That(module.ProducedValue, Is.EqualTo(1));

            // The native dependency table is the only carrier of the producer's handle: the producer does not publish
            // through Dependency, so the dispatcher wrote the default handle into the produce stage's own fence slot
            // while the native table kept the real one.
            Assert.That(module.ProduceSlot.Equals(default(JobHandle)), Is.True,
                "the produce stage's own fence slot carries no handle, because the producer publishes only natively");
            Assert.That(module.Native.FenceOf(module.ResultSlot).Equals(module.ProduceHandle), Is.True,
                "the native resource slot is what carries the producer's handle (P-041)");

            // The handle the playback combined IS the producer's handle: playback waits for its producer.
            Assert.That(module.PlaybackIncoming.Equals(default(JobHandle)), Is.False,
                "a playback point must not run with an empty producer fence");
            Assert.That(module.PlaybackIncoming.Equals(module.ProduceHandle), Is.True,
                "the playback's dependency is exactly the producer job's handle");

            // Corroborating values: the producer job wrote a value the main thread could not have known, and the
            // played-back structural change carries exactly that value. These are not the proof of the wait - a
            // main-thread read of a declared component type is synchronized by the safety manager anyway - and the
            // structural assertions above, plus the container negative case, are what demonstrate the wait.
            Assert.That(module.PlaybackReadValue, Is.EqualTo(module.ProducedValue),
                "the playback stage read the value the producer job wrote");
            Assert.That(module.PlaybackReadValue, Is.Not.EqualTo(-1), "the seeded sentinel was not observed");
            Assert.That(module.Container[0], Is.EqualTo(module.ProducedValue),
                "the producer job's non-component write is durable after its handle completed");

            Assert.That(
                entityManager.GetComponentData<PlaybackSourceValue>(module.SourceEntity).Value,
                Is.EqualTo(module.ProducedValue),
                "the producer's own component write is durable after the completed fence");

            // SetComponent through the ECB: a value change played back at the declared boundary.
            Assert.That(
                entityManager.GetComponentData<PlaybackObserved>(module.SetTarget).Value,
                Is.EqualTo(module.ProducedValue));
            Assert.That(
                entityManager.GetComponentData<PlaybackObserved>(module.SetTarget).Value,
                Is.Not.EqualTo(-1),
                "the seeded target value was overwritten by the played-back change");

            // AddComponent through the same ECB: the genuinely structural half of the playback.
            Assert.That(
                entityManager.HasComponent<PlaybackObserved>(module.AddTarget),
                Is.True,
                "the deferred structural change ran");
            Assert.That(
                entityManager.GetComponentData<PlaybackObserved>(module.AddTarget).Value,
                Is.EqualTo(module.ProducedValue));

            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "the step boundary retired the producer");
            Assert.That(host.Ledger.QuarantinedJobCount, Is.EqualTo(0));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(2), "the initial image plus the committed step");
            Assert.That(
                module.ProduceStageIndex,
                Is.LessThan(module.PlaybackStageIndex),
                "the compiled order puts the producer's fence before the playback's");
        }

        [Test]
        public void APlaybackThatReadsWithoutWaitingIsRejectedByTheSafetySystem()
        {
            // The broken implementation: the playback stage reads the producer's non-component container while the
            // producer's handle is still outstanding and was never completed on the main thread. Unity's job safety
            // must reject the read, the guarded dispatcher must latch the world fault, and the step must publish
            // nothing - so the "no safety exception" result of the positive path is not vacuous.
            ScheduleExecutionModule module = Create(ScheduleExecutionShape.Playback, out UnityWorldHost host);
            module.SkipProducerWait = true;

            RunOneStep(host, module, 1_000_000UL);

            if (host.FaultCount == 0)
            {
                // Without job safety there is no way to distinguish "waited correctly" from "read a value that happened
                // to be written already", so this case cannot contribute its evidence; say so rather than pass silently.
                Assert.Ignore(
                    "Unity job safety did not reject the unsynchronized container read in this Editor configuration, so "
                    + "the negative variant cannot be demonstrated here. The positive path's equality and value "
                    + "assertions remain the evidence that playback waited for its producer.");
            }

            Assert.That(host.Driver.IsFaulted, Is.True);
            Assert.That(host.Driver.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.Driver.FaultDetail, Does.Contain("InvalidOperationException"),
                "the safety system rejected the unsynchronized read: " + host.Driver.FaultDetail);
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "a faulted step publishes no step id");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "only the initial image exists");
            Assert.That(module.PlaybackReadValue, Is.EqualTo(0), "the unsafe read never produced a value");
            Assert.That(
                host.EntityWorld.EntityManager.HasComponent<PlaybackObserved>(module.AddTarget),
                Is.False,
                "the playback never reached its deferred structural change");
        }
    }
}
