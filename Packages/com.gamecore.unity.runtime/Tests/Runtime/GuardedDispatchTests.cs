#nullable enable
using GameCore.Contracts;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests
{
    /// <summary>
    /// Guarded dispatch and failure-boundary tests (GC-005, TEST-016/TEST-018). A managed system that writes and
    /// then throws stops the remaining systems of the step, faults the world, publishes no step or snapshot, and
    /// leaves its pending job tracked until safe teardown completes it. The stock group loop would instead log the
    /// exception and continue, so this is the executable proof that dispatch does not use it (04 s4, P-031).
    /// </summary>
    [TestFixture]
    public sealed class GuardedDispatchTests
    {
        private const ulong SessionSalt = 0x4641554C54535445UL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525446UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        private static UnityWorldHost CreateFaultEnabledWorld()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);
            Assert.That(FixtureWorldState.SetFaultEnabled(host!.EntityWorld.EntityManager, true), Is.True);
            return host;
        }

        private static FixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(
                FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail),
                Is.True);
            return trail;
        }

        [Test]
        public void ManagedSystemThatWritesThenThrowsStopsTheStepAndFaultsTheWorld()
        {
            UnityWorldHost host = CreateFaultEnabledWorld();
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
            Assert.That(trail.FaultCount, Is.EqualTo(1));
            Assert.That(trail.ProjectCount, Is.EqualTo(0), "the next registered system must not run after the throw");
            Assert.That(
                FixtureWorldState.ReadCounter(host.EntityWorld.EntityManager),
                Is.EqualTo(111),
                "the failing system's writes are preserved: this is a fail-stop, not a rollback");

            Assert.That(host.Ledger.OutstandingJobCount, Is.GreaterThan(0), "the failing step's job stays tracked");
            Assert.That(host.Ledger.QuarantinedJobCount, Is.GreaterThan(0), "an unfinished job is retained behind quarantine (P-048)");
            Assert.That(host.Driver.RetainedJobs.Count, Is.GreaterThan(0));
        }

        [Test]
        public void AFaultedWorldAcceptsNoFurtherWork()
        {
            UnityWorldHost host = CreateFaultEnabledWorld();
            host.NotifyCommandAdmitted(1U);
            host.PumpFrame(1_000_000UL);

            int framesBefore = host.PumpCount;
            host.NotifyCommandAdmitted(1U);

            WorldPumpResult after = host.PumpFrame(2_000_000UL);

            Assert.That(after.Pumped, Is.False, "a faulted world is closed to admission (P-031)");
            Assert.That(after.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.PumpCount, Is.EqualTo(framesBefore), "no simulation resumes after a fault");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(ReadTrail(host).AcceptCount, Is.EqualTo(1));
            Assert.That(host.Driver.RefusedStepCount, Is.EqualTo(0), "the refusal happens before step admission");

            StepAdvanceResult advance = host.Driver.Advance(new StepAdvanceRequest(
                host.World,
                host.CurrentEpoch,
                host.CurrentStep,
                1UL,
                host.RetainedDebt));

            Assert.That(advance.Accepted, Is.False);
            Assert.That(advance.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(advance.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
        }

        [Test]
        public void PendingJobsStayTrackedUntilTeardownCompletesThem()
        {
            UnityWorldHost host = CreateFaultEnabledWorld();
            host.NotifyCommandAdmitted(1U);
            host.PumpFrame(1_000_000UL);

            int retained = host.Driver.RetainedJobs.Count;
            Assert.That(retained, Is.GreaterThan(0));
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(retained));

            var stop = new OperationId(host.World, Issuer, 2UL);
            OperationResult result = host.Stop(stop, "teardown after fault");

            Assert.That(result.Outcome, Is.EqualTo(Outcome.Published), result.Code + " " + result.CodeText);
            Assert.That(host.SettledJobCount, Is.EqualTo(retained), "teardown completes the retained jobs (P-047, P-048)");
            Assert.That(host.Driver.RetainedJobs.CompletionFailureCount, Is.EqualTo(0));
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "no job is abandoned while storage is released");
            Assert.That(host.Ledger.RetainedResourceCount, Is.EqualTo(0));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));
            Assert.That(host.IsEntityWorldCreated, Is.False);
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(0));
        }

        [Test]
        public void UnconsumedDeclaredBufferFaultsTheWorldInsteadOfPublishingPartialSuccess()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 3UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.CreateDrainFaultWorld(),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            host!.NotifyCommandAdmitted(1U);

            WorldPumpResult pump = host.PumpFrame(1_000_000UL);

            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Accepted, Is.False);
            Assert.That(host.Driver.FaultCode, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(host.Driver.LastDrain.Succeeded, Is.False);
            Assert.That(host.Driver.LastDrain.UnconsumedBuffer, Is.EqualTo(FixtureKeys.CounterBuffer));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "no success image for a step whose reliable data was not consumed (O-16)");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(ReadTrail(host).ProjectCount, Is.EqualTo(0));
        }

        [Test]
        public void ADisabledSystemForwardsItsFenceAndProducesNoNewWork()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 4UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(
                host!.Systems.TryResolve(FixtureKeys.SettleSystem, out SystemDispatchTarget settle),
                Is.True);
            settle.ManagedSystem!.Enabled = false;

            host.NotifyCommandAdmitted(1U);
            WorldPumpResult pump = host.PumpFrame(1_000_000UL);

            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Accepted, Is.True, "a skipped system is not a failure");
            Assert.That(host.Driver.IsFaulted, Is.False);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));

            FixtureTrail trail = ReadTrail(host);
            Assert.That(trail.SettleCount, Is.EqualTo(0), "a disabled system does not execute");
            Assert.That(trail.FaultCount, Is.EqualTo(1), "later systems still run in their compiled order");
            Assert.That(trail.ProjectCount, Is.EqualTo(1));
            Assert.That(host.StepGroup.Fences!.Combined, Is.EqualTo(default(JobHandle)), "the step fence is completed and cleared at commit");
            Assert.That(host.StepGroup.Fences.StageCount, Is.EqualTo(FixtureRegistration.StageCount));
        }

        [Test]
        public void DispatchThroughTheSeamRejectsAStaleEpoch()
        {
            UnityWorldHost host = CreateFaultEnabledWorld();

            var table = host.StepGroup.InstalledPlan.ToOrderedTable(new AssemblyEpoch(7UL));
            DispatchRunResult result = host.Driver.Dispatch(new StageDispatchRequest(
                host.World,
                new AssemblyEpoch(7UL),
                LogicalStepId.Zero,
                table));

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.StalePlan), "a table from another epoch never dispatches");
            Assert.That(result.DispatchedCount, Is.EqualTo(0));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(host.Driver.IsFaulted, Is.False, "a stale table is refused before any system runs");

            var wrongWorld = new WorldId(new Id128(SessionSalt, 0xDEADUL));
            DispatchRunResult foreign = host.Driver.Dispatch(new StageDispatchRequest(
                wrongWorld,
                host.CurrentEpoch,
                host.CurrentStep,
                host.StepGroup.InstalledPlan.ToOrderedTable(host.CurrentEpoch)));
            Assert.That(foreign.Completed, Is.False);
            Assert.That(foreign.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
        }

        [Test]
        public void AnInfrastructureGroupEntryDispatchesItsOwnBoundTable()
        {
            UnityWorldHost host = CreateFaultEnabledWorld();

            Assert.That(host.Systems.TryResolve(FixtureKeys.ProjectSystem, out SystemDispatchTarget project), Is.True);
            Assert.That(project.ManagedSystem, Is.Not.Null);

            // A nested adapter group with its own epoch-bound table, registered under its own generated key.
            var nestedKey = new FactoryKey(new Id128(FixtureKeys.Namespace, 0x4001UL), 1U);
            var nestedCatalog = new SystemDispatchCatalog();
            nestedCatalog.RegisterManaged(FixtureKeys.ProjectSystem, project.ManagedSystem!);

            var nestedPlan = new GuardedDispatchPlan(
                new[]
                {
                    new GuardedDispatchEntry(
                        FixtureKeys.ProjectStage,
                        FixtureKeys.ProjectSystem,
                        SystemDispatchKind.ManagedSystem,
                        0,
                        0,
                        null),
                },
                null,
                1);

            GameCoreOutputGroup nested = host.EntityWorld.CreateSystemManaged<GameCoreOutputGroup>();
            nested.Bind(nestedPlan, host.CurrentEpoch, nestedCatalog, host.Driver);

            var outerCatalog = new SystemDispatchCatalog();
            outerCatalog.RegisterInfrastructureGroup(nestedKey, nested);

            var outerPlan = new GuardedDispatchPlan(
                new[]
                {
                    new GuardedDispatchEntry(
                        FixtureKeys.OutputStage,
                        nestedKey,
                        SystemDispatchKind.InfrastructureGroup,
                        0,
                        0,
                        null),
                },
                null,
                1);

            host.IngressGroup.Bind(outerPlan, host.CurrentEpoch, outerCatalog, host.Driver);

            DispatchRunResult run = host.IngressGroup.DispatchOne(new StageDispatchRequest(
                host.World,
                host.CurrentEpoch,
                host.CurrentStep,
                outerPlan.ToOrderedTable(host.CurrentEpoch)));

            Assert.That(run.Completed, Is.True);
            Assert.That(run.DispatchedCount, Is.EqualTo(1));
            Assert.That(nested.DispatchRunCount, Is.EqualTo(1), "the nested group's own guarded table ran exactly once");
            Assert.That(nested.TotalDispatchedCount, Is.EqualTo(1));
            Assert.That(ReadTrail(host).ProjectCount, Is.EqualTo(1), "the nested group's system ran through the guarded dispatcher");
            Assert.That(host.Driver.IsFaulted, Is.False);
        }
    }
}
