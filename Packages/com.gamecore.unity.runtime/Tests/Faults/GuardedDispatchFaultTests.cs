// GameCore.Unity.Runtime.Tests — the guarded-dispatch fault boundary suite (GC-017, TEST-016 rows 6 and 7).
//
// Every case drives the real modules: the real `UnityWorldHost` over a real `Unity.Entities.World`, the real
// `UnityExecutionDriver`, the real `GuardedSystemGroup` dispatch loop and live ECS storage read back through
// `FixtureWorldState`. Nothing here asserts a managed model of the world.
//
// The cases this file owns:
//
//   * row 6 — an authoritative system throws after a partial update: the next registered stage never runs, the
//     world is `Faulted`, no step id and no step image publish, and the failing step's earlier writes are
//     preserved (a fail-stop, not a rollback). The contrast case proves the same exception is *swallowed* by
//     Unity's stock `ComponentSystemGroup` loop, which is why the guarded loop exists at all.
//   * row 7 — an adapter output system fails after a successful simulation commit: history is not rewound and
//     the adapter degradation follows its contract (`ApplyFault`, admission closed).
//   * the structural-playback boundary — the commit itself is refused after the step's systems already ran.
//
// The GC-017 latches exist only in a compilation that defines `GAMECORE_FAULT_INJECTION`, so every case begins by
// asserting `IsCompiledIn`; a false there means the symbol is missing rather than the boundary being unreachable.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Unity.Runtime.Tests.Faults
{
    /// <summary>
    /// Counters of the probe systems, held in the fixture world's own ECS storage so the assertions read live
    /// storage rather than a managed mirror (TEST-018).
    /// </summary>
    public struct FaultProbeCounters : IComponentData
    {
        /// <summary>Times the throwing probe wrote its counter and then threw.</summary>
        public int Throws;

        /// <summary>Times the counting probe ran to completion.</summary>
        public int Runs;
    }

    /// <summary>The deterministic failure of the probe systems; a distinct type so a test reads what it injected.</summary>
    public sealed class FaultProbeInjectedException : Exception
    {
        public FaultProbeInjectedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Plain stock <see cref="ComponentSystemGroup"/>, deliberately NOT a <see cref="GuardedSystemGroup"/>: it
    /// exists only so the suite can run Unity's own group loop and show what that loop does with a throwing
    /// system. Attribute sorting is switched off so the executed order is exactly the order the test adds its
    /// systems in; the loop under test is still the stock `UpdateAllSystems` one.
    /// </summary>
    [DisableAutoCreation]
    public partial class FaultProbeStockGroup : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            EnableSystemSorting = false;
        }
    }

    /// <summary>
    /// Probe system that increments an authoritative counter and then always throws, which is exactly TEST-016
    /// row 6's "authoritative system throws after a partial update".
    /// </summary>
    [DisableAutoCreation]
    public partial class FaultProbeThrowingSystem : SystemBase
    {
        private EntityQuery countersQuery;

        public FaultProbeThrowingSystem()
        {
        }

        /// <summary>Invocations of this instance; the stock and guarded halves hold separate instances.</summary>
        public int Executions { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            countersQuery = GetEntityQuery(ComponentType.ReadWrite<FaultProbeCounters>());
        }

        protected override void OnUpdate()
        {
            Executions++;
            if (countersQuery.IsEmpty)
            {
                throw new FaultProbeInjectedException("the probe world carries no counter entity");
            }

            Entity entity = countersQuery.GetSingletonEntity();
            FaultProbeCounters counters = EntityManager.GetComponentData<FaultProbeCounters>(entity);
            counters.Throws++;
            EntityManager.SetComponentData(entity, counters);
            throw new FaultProbeInjectedException(
                "FaultProbeThrowingSystem wrote authoritative state and then threw (deterministic fail-stop probe)");
        }
    }

    /// <summary>Probe system that increments its own counter and returns; it proves whether the loop continued.</summary>
    [DisableAutoCreation]
    public partial class FaultProbeCountingSystem : SystemBase
    {
        private EntityQuery countersQuery;

        public FaultProbeCountingSystem()
        {
        }

        public int Executions { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            countersQuery = GetEntityQuery(ComponentType.ReadWrite<FaultProbeCounters>());
        }

        protected override void OnUpdate()
        {
            Executions++;
            if (countersQuery.IsEmpty)
            {
                return;
            }

            Entity entity = countersQuery.GetSingletonEntity();
            FaultProbeCounters counters = EntityManager.GetComponentData<FaultProbeCounters>(entity);
            counters.Runs++;
            EntityManager.SetComponentData(entity, counters);
        }
    }

    /// <summary>
    /// Probe system for TEST-016 row 7: it is an adapter output boundary that runs (and records that it ran) on
    /// every host frame, and throws only once the fixture's deterministic fault flag is enabled. That is what lets
    /// the suite commit a step successfully first and then fail the output boundary on the next pump.
    /// </summary>
    [DisableAutoCreation]
    public partial class FaultProbeFlaggedOutputSystem : SystemBase
    {
        private EntityQuery countersQuery;

        public FaultProbeFlaggedOutputSystem()
        {
        }

        public int Executions { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            countersQuery = GetEntityQuery(ComponentType.ReadWrite<FaultProbeCounters>());
        }

        protected override void OnUpdate()
        {
            Executions++;
            if (countersQuery.IsEmpty)
            {
                return;
            }

            Entity entity = countersQuery.GetSingletonEntity();
            FaultProbeCounters counters = EntityManager.GetComponentData<FaultProbeCounters>(entity);
            counters.Runs++;
            EntityManager.SetComponentData(entity, counters);

            Entity flagEntity = FixtureWorldState.Find(EntityManager);
            if (flagEntity == Entity.Null
                || EntityManager.GetComponentData<FixtureFaultFlag>(flagEntity).Enabled == 0)
            {
                return;
            }

            throw new FaultProbeInjectedException(
                "the adapter output boundary failed after a successful simulation commit (TEST-016 row 7)");
        }
    }

    [TestFixture]
    public sealed class GuardedDispatchFaultTests
    {
        /// <summary>Session salt of this fixture; distinct from every other suite's, so worlds never collide.</summary>
        private const ulong SessionSalt = 0x4743303137464155UL;

        /// <summary>Key namespace of the probe systems; a distinct word from every fixture and package key.</summary>
        private const ulong ProbeNamespace = 0x474330313750524FUL;

        private static readonly Id128 Issuer = new Id128(0x4953535545524743UL, 1UL);

        /// <summary>Key of the always-throwing probe (the failing entry of the contrast pair).</summary>
        private static readonly FactoryKey ThrowingProbeKey =
            new FactoryKey(new Id128(ProbeNamespace, 0x5001UL), 1U);

        /// <summary>Key of the counting probe (the registered stage after the failing one).</summary>
        private static readonly FactoryKey CountingProbeKey =
            new FactoryKey(new Id128(ProbeNamespace, 0x5002UL), 1U);

        /// <summary>Key of the adapter-output probe of row 7 (flagged: it fails only once the fixture flag is set).</summary>
        private static readonly FactoryKey FlaggedOutputProbeKey =
            new FactoryKey(new Id128(ProbeNamespace, 0x5003UL), 1U);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        /// <summary>
        /// One owned command-driven fixture world with the probe counter component added to its seeded entity, so
        /// the probe systems have live storage to write.
        /// </summary>
        private static UnityWorldHost CreateWorld(bool includeFaultStage)
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);

            Entity entity = FixtureWorldState.Find(host!.EntityWorld.EntityManager);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), "the fixture world state must be seeded");
            host.EntityWorld.EntityManager.AddComponentData(entity, new FaultProbeCounters());
            return host;
        }

        private static FaultProbeCounters ReadCounters(UnityWorldHost host)
        {
            using (EntityQuery query = host.EntityWorld.EntityManager
                .CreateEntityQuery(ComponentType.ReadOnly<FaultProbeCounters>()))
            {
                Entity entity = query.GetSingletonEntity();
                return host.EntityWorld.EntityManager.GetComponentData<FaultProbeCounters>(entity);
            }
        }

        private static FixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(
                FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail),
                Is.True);
            return trail;
        }

        /// <summary>
        /// Binds one probe entry into the world's guarded output group, so a real guarded `GuardedSystemGroup`
        /// boundary — not a double — is the thing the case drives. The plan mirrors the fixture's own output plan
        /// shape (one entry in the output stage, no declared buffers).
        /// </summary>
        private static void BindOutputProbe<TProbe>(UnityWorldHost host, FactoryKey key, TProbe probe)
            where TProbe : ComponentSystemBase
        {
            var catalog = new SystemDispatchCatalog();
            catalog.RegisterManaged(key, probe);

            var plan = new GuardedDispatchPlan(
                new[]
                {
                    new GuardedDispatchEntry(
                        FixtureKeys.OutputStage,
                        key,
                        SystemDispatchKind.ManagedSystem,
                        0,
                        FixtureRegistration.OutputStageIndex,
                        null),
                },
                null,
                FixtureRegistration.StageCount);

            host.OutputGroup.Bind(plan, host.CurrentEpoch, catalog, host.Driver);
            Assert.That(host.OutputGroup.InstalledPlan.Entries.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Binds the two-entry probe table (throwing system, then counting system) into the world's guarded STEP
        /// group, so the failing entry is followed by a registered stage that must never run.
        /// </summary>
        private static void BindStepProbe(
            UnityWorldHost host,
            FaultProbeThrowingSystem throwing,
            FaultProbeCountingSystem counting)
        {
            var catalog = new SystemDispatchCatalog();
            catalog.RegisterManaged(ThrowingProbeKey, throwing);
            catalog.RegisterManaged(CountingProbeKey, counting);

            var plan = new GuardedDispatchPlan(
                new[]
                {
                    new GuardedDispatchEntry(
                        FixtureKeys.AcceptStage,
                        ThrowingProbeKey,
                        SystemDispatchKind.ManagedSystem,
                        0,
                        FixtureRegistration.AcceptStageIndex,
                        null),
                    new GuardedDispatchEntry(
                        FixtureKeys.SettleStage,
                        CountingProbeKey,
                        SystemDispatchKind.ManagedSystem,
                        1,
                        FixtureRegistration.SettleStageIndex,
                        null),
                },
                null,
                FixtureRegistration.StageCount);

            host.StepGroup.Bind(plan, host.CurrentEpoch, catalog, host.Driver);
            Assert.That(
                host.StepGroup.InstalledPlan.Entries.Count,
                Is.EqualTo(2),
                "the probe table must have a registered stage after the failing one");
        }

        /// <summary>
        /// TEST-016 row 6. The fixture's injected fault stage writes authoritative state, schedules a job and then
        /// throws; the guarded dispatcher must stop there — the next registered stage never runs, the world faults,
        /// no step id and no step image publish, and the writes the failing stage already made are preserved.
        /// </summary>
        [Test]
        public void AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing()
        {
            UnityWorldHost host = CreateWorld(includeFaultStage: true);

            Assert.That(
                host.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.gamecore.fault-qualification, so a false here means the symbol is missing.");

            Assert.That(FixtureWorldState.SetFaultEnabled(host.EntityWorld.EntityManager, true), Is.True);
            host.NotifyCommandAdmitted(1U);

            WorldPumpResult pump = host.PumpFrame(1_000_000UL);

            Assert.That(pump.Pumped, Is.True);
            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Accepted, Is.False, "a faulted step is never accepted");
            Assert.That(pump.Advance.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(pump.Advance.PublishedSnapshot, Is.Null, "a faulted step publishes no image (P-031)");

            Assert.That(host.Driver.IsFaulted, Is.True);
            Assert.That(host.Driver.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.Driver.FaultDetail, Does.Contain("FixtureInjectedFaultException"));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "no step id advances on a fault (P-031)");
            Assert.That(host.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First), "no epoch publishes on a fault (P-031)");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "only the initial assembly image exists");
            Assert.That(
                host.Publications.HasPublished(new SnapshotToken(host.World, AssemblyEpoch.First, LogicalStepId.First)),
                Is.False,
                "the failed step's image must never be observable");

            FixtureTrail trail = ReadTrail(host);
            Assert.That(trail.AcceptCount, Is.EqualTo(1), "systems before the fault ran");
            Assert.That(trail.SettleCount, Is.EqualTo(1));
            Assert.That(trail.FaultCount, Is.EqualTo(1), "the failing stage ran exactly once");
            Assert.That(trail.ProjectCount, Is.EqualTo(0), "the next registered stage must not run after the throw");
            Assert.That(
                FixtureWorldState.ReadCounter(host.EntityWorld.EntityManager),
                Is.EqualTo(111),
                "the failing stage's writes are preserved: this is a fail-stop, not a rollback");

            Assert.That(host.Ledger.OutstandingJobCount, Is.GreaterThan(0), "the failing step's job stays tracked");
            Assert.That(
                host.Ledger.QuarantinedJobCount,
                Is.GreaterThan(0),
                "an unfinished job is retained behind quarantine (P-048)");
            Assert.That(host.Driver.RetainedJobs.Count, Is.GreaterThan(0));

            // A faulted world admits nothing more, so no simulation resumes on the partially changed storage.
            int framesBefore = host.PumpCount;
            host.NotifyCommandAdmitted(1U);
            WorldPumpResult after = host.PumpFrame(2_000_000UL);
            Assert.That(after.Pumped, Is.False, "a faulted world is closed to admission (P-031)");
            Assert.That(after.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.PumpCount, Is.EqualTo(framesBefore), "no simulation resumes after a fault");
        }

        /// <summary>
        /// The contrast that makes the previous case meaningful, asserted against the real stock loop.
        ///
        /// Unity's own <see cref="ComponentSystemGroup.OnUpdate"/> runs its systems through
        /// <c>UpdateAllSystems</c>, whose loop wraps each system's `Update()` in
        /// <c>try { … } catch (Exception e) { Debug.LogException(e); }</c> and then continues with the next
        /// system (com.unity.entities 1.4.6, `Unity.Entities/ComponentSystemGroup.cs`). A guarded group does not:
        /// <see cref="GuardedSystemGroup"/> deliberately does not call `base.OnUpdate()`, and its `DispatchRun`
        /// returns <c>Completed == false</c> with the failing entry's index and key, retains the step's jobs by
        /// quarantine and latches the sink fault, so nothing after the failing entry runs.
        ///
        /// This case runs both loops over the SAME two probe classes and the SAME live counters component:
        ///   * stock group  → the thrower runs, its exception is logged, and the counting system still runs;
        ///   * guarded group → the thrower runs, `DispatchRun` stops at entry 0, and the counting system never runs;
        ///   * a second guarded update is refused (`RefusedDispatchCount`) instead of re-running anything.
        /// </summary>
        [Test]
        public void AStockGroupSwallowsTheSameExceptionAndTheGuardedGroupDoesNot()
        {
            UnityWorldHost host = CreateWorld(includeFaultStage: false);

            Assert.That(
                host.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.gamecore.fault-qualification, so a false here means the symbol is missing.");

            // 1. The stock loop: the throwing system's exception is logged, and the next system still runs.
            FaultProbeStockGroup stock = host.EntityWorld.CreateSystemManaged<FaultProbeStockGroup>();
            FaultProbeThrowingSystem stockThrower =
                host.EntityWorld.CreateSystemManaged<FaultProbeThrowingSystem>();
            FaultProbeCountingSystem stockCounter =
                host.EntityWorld.CreateSystemManaged<FaultProbeCountingSystem>();
            stock.AddSystemToUpdateList(stockThrower);
            stock.AddSystemToUpdateList(stockCounter);

            LogAssert.Expect(LogType.Exception, new Regex(".*FaultProbeInjectedException.*", RegexOptions.Singleline));
            stock.Update();

            FaultProbeCounters afterStock = ReadCounters(host);
            Assert.That(stockThrower.Executions, Is.EqualTo(1), "the stock loop ran the throwing system");
            Assert.That(
                stockCounter.Executions,
                Is.EqualTo(1),
                "the stock loop logs the exception and CONTINUES with the next system, which is exactly why a Core"
                + " failure can never be signalled by a stock group's log (TEST-018)");
            Assert.That(afterStock.Throws, Is.EqualTo(1));
            Assert.That(afterStock.Runs, Is.EqualTo(1));
            Assert.That(
                host.Driver.IsFaulted,
                Is.False,
                "the stock loop has no fault signal at all: the world never learns its system failed");

            // 2. The guarded loop over the same two probe classes. The guarded dispatcher stops at the failing
            //    entry, so the registered stage after it never runs.
            FaultProbeThrowingSystem guardedThrower =
                host.EntityWorld.CreateSystemManaged<FaultProbeThrowingSystem>();
            FaultProbeCountingSystem guardedCounter =
                host.EntityWorld.CreateSystemManaged<FaultProbeCountingSystem>();
            BindStepProbe(host, guardedThrower, guardedCounter);

            DispatchRunResult run = host.StepGroup.DispatchOne(new StageDispatchRequest(
                host.World,
                host.CurrentEpoch,
                host.CurrentStep,
                host.StepGroup.InstalledPlan.ToOrderedTable(host.CurrentEpoch)));

            Assert.That(run.Completed, Is.False, "the guarded loop does not swallow the failure");
            Assert.That(run.DispatchedCount, Is.EqualTo(0), "the failing entry is the first one");
            Assert.That(run.StoppedAtIndex, Is.EqualTo(0), "the failing entry's dispatch index is reported");
            Assert.That(run.FailingSystemKey.Equals(ThrowingProbeKey), Is.True, "the failing key is reported");
            Assert.That(run.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(
                run.UnreachedSystemKeys.Count,
                Is.EqualTo(1),
                "the registered stage after the failure is reported as unreached");
            Assert.That(run.UnreachedSystemKeys[0].Equals(CountingProbeKey), Is.True);

            FaultProbeCounters afterGuarded = ReadCounters(host);
            Assert.That(guardedThrower.Executions, Is.EqualTo(1), "the guarded loop ran the throwing system");
            Assert.That(
                guardedCounter.Executions,
                Is.EqualTo(0),
                "the guarded loop stops at the failing entry: the next registered system must not run");
            Assert.That(afterGuarded.Throws, Is.EqualTo(2), "the guarded probe's partial write happened");
            Assert.That(afterGuarded.Runs, Is.EqualTo(1), "the counting system ran only under the stock loop");
            Assert.That(host.Driver.IsFaulted, Is.True, "the guarded loop latches the fault the stock loop drops");

            // 3. The refusal: a second guarded update finds the sink latched and refuses instead of re-running.
            int refusedBefore = host.StepGroup.RefusedDispatchCount;
            host.StepGroup.Update();
            Assert.That(
                host.StepGroup.RefusedDispatchCount,
                Is.EqualTo(refusedBefore + 1),
                "a latched sink refuses further dispatch through the real OnUpdate path");
            FaultProbeCounters afterSecond = ReadCounters(host);
            Assert.That(afterSecond.Throws, Is.EqualTo(2), "a refused dispatch runs no system at all");
            Assert.That(afterSecond.Runs, Is.EqualTo(1));
        }

        /// <summary>
        /// TEST-016 row 7. One step commits successfully (a published image and an advanced step id), then the
        /// adapter output boundary fails on the next host frame: the pump reports the fault, the world is
        /// `Faulted`, admission closes — and none of the committed simulation history moves. The step id, the
        /// publication count and the retained image of the earlier step are all unchanged, so the failure is not
        /// concealing itself by rewinding or republishing.
        /// </summary>
        [Test]
        public void AFailingOutputGroupDoesNotRewindTheCommittedStep()
        {
            UnityWorldHost host = CreateWorld(includeFaultStage: false);

            Assert.That(
                host.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.gamecore.fault-qualification, so a false here means the symbol is missing.");

            FaultProbeFlaggedOutputSystem output =
                host.EntityWorld.CreateSystemManaged<FaultProbeFlaggedOutputSystem>();
            BindOutputProbe(host, FlaggedOutputProbeKey, output);

            // 1. A real step commits: the output probe runs on the same host frame and does not fail yet.
            host.NotifyCommandAdmitted(1U);
            WorldPumpResult first = host.PumpFrame(1_000_000UL);
            Assert.That(first.Advance, Is.Not.Null);
            Assert.That(first.Advance!.Accepted, Is.True, first.Advance.Code + ": " + first.Advance.Outcome);
            Assert.That(first.Advance.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(first.Advance.PublishedSnapshot, Is.Not.Null, "a committed step publishes its image");
            Assert.That(first.OutputDispatched, Is.True);

            LogicalStepId committedStep = host.CurrentStep;
            AssemblyEpoch committedEpoch = host.CurrentEpoch;
            Assert.That(committedStep, Is.EqualTo(LogicalStepId.First), "the step id advanced");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(2), "the initial assembly plus the step image");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(1));

            var committedToken = new SnapshotToken(host.World, committedEpoch, committedStep);
            Assert.That(host.Publications.HasPublished(committedToken), Is.True);
            Assert.That(
                host.Publications.TryGetImage(committedToken, out PublishedStepImage? before),
                Is.True);
            Assert.That(before, Is.Not.Null);
            ContentHash stateBefore = before!.StateHash;
            int eventsBefore = before.EventCount;
            FaultProbeCounters beforeFault = ReadCounters(host);
            Assert.That(beforeFault.Runs, Is.EqualTo(1), "the output boundary ran on the committed frame");

            // 2. The adapter output boundary fails on the NEXT host frame. No command is admitted, so no step is
            //    pending: the only thing that can move is the output dispatch itself.
            Assert.That(FixtureWorldState.SetFaultEnabled(host.EntityWorld.EntityManager, true), Is.True);
            WorldPumpResult second = host.PumpFrame(2_000_000UL);

            Assert.That(second.Pumped, Is.True, "the frame ran; it faulted");
            Assert.That(second.Advance, Is.Null, "no step was admitted on this frame");
            Assert.That(second.OutputDispatched, Is.False, "the adapter output boundary failed");
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.ApplyFault));

            Assert.That(host.Driver.IsFaulted, Is.True);
            Assert.That(host.Driver.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            // The first latch is the failing system's own invocation (the guarded group reports the entry and its
            // system key through `OnDispatchFaulted`), and the later `DispatchPeripheral` latch is a no-op because
            // the driver has already latched: so the detail names the probe exception and its boundary, not the
            // peripheral entry index.
            Assert.That(
                host.Driver.FaultDetail,
                Does.Contain("FaultProbeInjectedException"),
                "the world reports the failing system's own fault, which is what makes the output boundary the"
                + " failing one");
            Assert.That(
                host.Driver.FaultDetail,
                Does.Contain("adapter output boundary failed"),
                "the detail carries the boundary the probe names");
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));

            // 3. Nothing was rewound: the committed step, its epoch, its image count and its retained image are
            //    exactly what they were before the output failure.
            Assert.That(host.CurrentStep, Is.EqualTo(committedStep), "the committed step id is not rewound");
            Assert.That(host.CurrentEpoch, Is.EqualTo(committedEpoch));
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(1), "no step was committed by the failing frame");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(2), "no image was dropped or republished");
            Assert.That(
                host.Publications.HasPublished(committedToken),
                Is.True,
                "the earlier step's image is still the published one (P-031)");
            Assert.That(host.Publications.TryGetImage(committedToken, out PublishedStepImage? after), Is.True);
            Assert.That(after, Is.Not.Null);
            Assert.That(after!.StateHash.Equals(stateBefore), Is.True, "the retained image's state is unchanged");
            Assert.That(after.EventCount, Is.EqualTo(eventsBefore), "the retained image's events are unchanged");
            Assert.That(
                host.Ledger.OutstandingJobCount,
                Is.EqualTo(0),
                "the committed step's work was settled at its commit and is not resurrected by the later failure");

            // 4. The contract for a degraded adapter: admission is closed, so no simulation resumes.
            int framesBefore = host.PumpCount;
            WorldPumpResult third = host.PumpFrame(3_000_000UL);
            Assert.That(third.Pumped, Is.False, "a faulted world admits nothing (P-031)");
            Assert.That(third.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.PumpCount, Is.EqualTo(framesBefore));
            Assert.That(
                ReadCounters(host).Runs,
                Is.EqualTo(2),
                "the output boundary ran on both frames before the freeze");
        }

        /// <summary>
        /// The structural-playback boundary: the step's own systems all ran and its declared buffers were drained,
        /// but the boundary is reached before the commit, so the step is never accepted, never advances
        /// <c>LogicalStepId</c> and never publishes an image. The world faults, so nothing after it resumes either.
        ///
        /// What "keeps the quarantine" means at this boundary is exact, and this case asserts exactly that: the
        /// step's scheduled work is recorded in the world's ledger (<c>JobCount</c>), and because the boundary is
        /// reached *after* the step's effective fence and <c>CompleteStepJobs</c>, that work is settled rather than
        /// left outstanding or retained by quarantine — the fault leaves no dangling handle behind a faulted world.
        /// The complementary case above shows the retention shape: a system that throws *during* dispatch is the
        /// one whose pending work stays tracked and quarantined (<c>QuarantinedJobCount &gt; 0</c>).
        /// </summary>
        [Test]
        public void AStructuralPlaybackFaultStopsTheStepCommitAndKeepsTheQuarantine()
        {
            UnityWorldHost host = CreateWorld(includeFaultStage: false);

            Assert.That(
                host.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.gamecore.fault-qualification, so a false here means the symbol is missing.");

            host.Faults.Arm(FaultBoundary.StructuralPlayback);
            Assert.That(host.Faults.IsArmed(FaultBoundary.StructuralPlayback), Is.True);

            host.NotifyCommandAdmitted(1U);
            WorldPumpResult pump = host.PumpFrame(1_000_000UL);

            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Accepted, Is.False, "the step's commit was refused");
            Assert.That(pump.Advance.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(pump.Advance.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(pump.Advance.PublishedSnapshot, Is.Null, "no step image publishes for a refused commit");
            Assert.That(pump.Pumped, Is.True);
            Assert.That(pump.Code, Is.EqualTo(DiagnosticCode.ApplyFault));

            Assert.That(host.Driver.IsFaulted, Is.True);
            Assert.That(host.Driver.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.Driver.FaultDetail, Does.Contain("structural-playback"));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));

            // The step itself never became visible: no step id, no epoch, no image for that step.
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "the step id did not advance");
            Assert.That(host.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "only the initial assembly image exists");
            Assert.That(
                host.Publications.HasPublished(new SnapshotToken(host.World, AssemblyEpoch.First, LogicalStepId.First)),
                Is.False,
                "the step the boundary refused has no published image");

            // Every registered stage of the step ran and the declared buffer was drained: the fault is in the
            // commit, not in a system, which is what makes this boundary a postwrite one.
            FixtureTrail trail = ReadTrail(host);
            Assert.That(trail.AcceptCount, Is.EqualTo(1));
            Assert.That(trail.SettleCount, Is.EqualTo(1));
            Assert.That(trail.ProjectCount, Is.EqualTo(1), "the whole step ran; only its commit stopped");
            Assert.That(host.Driver.LastDrain.Succeeded, Is.True, "the declared step buffer was consumed in this step");

            // The boundary is reached exactly once, with its operation and its injection recorded in the latch's
            // trace, so the evidence names the boundary rather than a boolean.
            Assert.That(host.Faults.ReachCountOf(FaultBoundary.StructuralPlayback), Is.EqualTo(1));
            IReadOnlyList<FaultRecord> records = host.Faults.Trace.Of(FaultBoundary.StructuralPlayback);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Injected, Is.True);
            Assert.That(records[0].Detail, Does.Contain("structural-playback"));
            Assert.That(records[0].ToLine(), Does.Contain(FaultBoundaryText.Of(FaultBoundary.StructuralPlayback)));

            // The step's work was tracked in the ledger, and its job was settled by the step's effective fence
            // before the commit was refused, so nothing is left dangling behind a faulted world.
            Assert.That(host.Ledger.JobCount, Is.GreaterThan(0), "the step recorded its scheduled work");
            Assert.That(
                host.Ledger.OutstandingJobCount,
                Is.EqualTo(0),
                "the effective fence completed the step's job before the boundary, so no job is left outstanding");
            Assert.That(host.Driver.RetainedJobs.Count, Is.EqualTo(0));

            // Nothing resumes: the faulted world refuses the next frame and runs no further step.
            int framesBefore = host.PumpCount;
            host.NotifyCommandAdmitted(1U);
            WorldPumpResult after = host.PumpFrame(2_000_000UL);
            Assert.That(after.Pumped, Is.False);
            Assert.That(after.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.PumpCount, Is.EqualTo(framesBefore));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
        }
    }
}
