#nullable enable
using GameCore.Contracts;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Tests
{
    /// <summary>
    /// Owned-world lifecycle tests (GC-005, TEST-009/TEST-011/TEST-018). Two worlds advance independently, a
    /// command-driven world executes zero steps over many host frames while ingress and presentation still run, no
    /// system is dispatched twice per step, pause changes host status only, and a world faults exactly once.
    /// </summary>
    [TestFixture]
    public sealed class WorldHostTests
    {
        private const ulong SessionSalt = 0x574F524C44544553UL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525445UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        private static WorldId NextSession()
        {
            sessionSequence++;
            return new WorldId(new Id128(SessionSalt, sessionSequence));
        }

        private static UnityWorldHost CreateWorld(WorldCreateRequest request, UnityWorldRegistration registration)
        {
            bool created = UnityWorldRegistry.TryCreate(
                request,
                registration,
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);
            Assert.That(host!.Lifecycle, Is.EqualTo(WorldLifecycleState.Running), "A created world is Running only after its initial publication (P-035).");
            return host;
        }

        private static UnityWorldHost CreateCommandWorld(bool includeFaultStage)
        {
            WorldId world = NextSession();
            var operation = new OperationId(world, Issuer, 1UL);
            return CreateWorld(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage));
        }

        private static UnityWorldHost CreateFixedWorld()
        {
            WorldId world = NextSession();
            var operation = new OperationId(world, Issuer, 1UL);
            return CreateWorld(
                FixtureRegistration.FixedStepRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.FixedStep, false));
        }

        private static FixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(
                FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail),
                Is.True,
                "the fixture world state must be seeded");
            return trail;
        }

        [Test]
        public void CreationPublishesEpochOneAndStepZero()
        {
            UnityWorldHost host = CreateCommandWorld(true);

            Assert.That(host.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First), "The initial assembly publishes epoch 1 (05 s2).");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "The initial assembly keeps step 0.");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1));
            Assert.That(host.Publications.Last, Is.Not.Null);
            Assert.That(host.Publications.Last!.Token.LogicalStepId, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.Publications.Last.Token.AssemblyEpoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedCreationOfTheSameSessionReturnsTheSameWorld()
        {
            UnityWorldHost host = CreateCommandWorld(true);
            WorldId world = host.World;
            var operation = new OperationId(world, Issuer, 2UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, true),
                out UnityWorldHost? again,
                out WorldCreateResult result);

            Assert.That(created, Is.True);
            Assert.That(result.Created, Is.True);
            Assert.That(again, Is.SameAs(host), "One world session has exactly one host (P-004, O-01).");
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(1));

            WorldCreateResult reentrant = host.Create(FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty));
            Assert.That(reentrant.Created, Is.True);
            Assert.That(reentrant.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));

            WorldId other = NextSession();
            WorldCreateResult mismatched = host.Create(FixtureRegistration.CommandDrivenRequest(other, new OperationId(other, Issuer, 3UL), ContentHash.Empty));
            Assert.That(mismatched.Created, Is.False);
            Assert.That(mismatched.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
        }

        [Test]
        public void TwoWorldsAdvanceIndependently()
        {
            UnityWorldHost first = CreateCommandWorld(true);
            UnityWorldHost second = CreateCommandWorld(true);

            Assert.That(first.World.Session, Is.Not.EqualTo(second.World.Session), "Each world owns a distinct session id (P-004).");
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(2));

            first.NotifyCommandAdmitted(1U);
            second.NotifyCommandAdmitted(1U);

            Assert.That(first.PumpFrame(1_000_000UL).Pumped, Is.True);
            Assert.That(second.PumpFrame(1_000_000UL).Pumped, Is.True);
            Assert.That(first.CurrentStep, Is.EqualTo(new LogicalStepId(1UL)));
            Assert.That(second.CurrentStep, Is.EqualTo(new LogicalStepId(1UL)));

            // Only the first world receives demand; the second must not advance on host frames alone.
            first.NotifyCommandAdmitted(1U);
            first.PumpFrame(2_000_000UL);
            second.PumpFrame(2_000_000UL);

            Assert.That(first.CurrentStep, Is.EqualTo(new LogicalStepId(2UL)));
            Assert.That(second.CurrentStep, Is.EqualTo(new LogicalStepId(1UL)), "A command-driven world advances only for its own demand (P-036).");
            Assert.That(second.PendingDemand, Is.EqualTo(0UL));

            Assert.That(ReadTrail(first).AcceptCount, Is.EqualTo(2));
            Assert.That(ReadTrail(second).AcceptCount, Is.EqualTo(1));
            Assert.That(ReadTrail(second).IngressCount, Is.EqualTo(2), "Ingress still runs on host frames of an idle world.");
            Assert.That(ReadTrail(second).OutputCount, Is.EqualTo(2), "Presentation still runs on host frames of an idle world.");
            Assert.That(ReadTrail(first).ProjectObservedJobValue, Is.EqualTo(222),
                "the project stage must observe the component job scheduled by the preceding stage");
            Assert.That(ReadTrail(second).ProjectObservedJobValue, Is.EqualTo(111));
        }

        [Test]
        public void CommandDrivenIdleExecutesZeroStepsOverManyFrames()
        {
            UnityWorldHost host = CreateCommandWorld(true);

            for (int frame = 1; frame <= 120; frame++)
            {
                WorldPumpResult result = host.PumpFrame((ulong)frame * 16_000UL);
                Assert.That(result.Pumped, Is.True);
                Assert.That(result.Sample.AdmittedSteps, Is.EqualTo(0UL));
                Assert.That(result.Sample.RetainedDebt, Is.EqualTo(TimeDebt.Zero), "An idle command-driven world never accrues debt.");
                Assert.That(result.Advance, Is.Null, "No step is requested without demand.");
            }

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "No demand means zero logical steps (P-036).");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(host.StepGroup.DispatchRunCount, Is.EqualTo(0), "An idle world never dispatches a gameplay stage.");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "Only the initial assembly image exists.");

            FixtureTrail trail = ReadTrail(host);
            Assert.That(trail.AcceptCount, Is.EqualTo(0));
            Assert.That(trail.SettleCount, Is.EqualTo(0));
            Assert.That(trail.TickCount, Is.EqualTo(0));
            Assert.That(trail.IngressCount, Is.EqualTo(120), "Ingress runs once per host frame.");
            Assert.That(trail.OutputCount, Is.EqualTo(120), "Output runs once per host frame.");
        }

        [Test]
        public void NoSystemIsDispatchedTwiceInOneStep()
        {
            UnityWorldHost host = CreateCommandWorld(true);
            int entriesPerStep = host.StepGroup.InstalledPlan.Entries.Count;
            Assert.That(entriesPerStep, Is.EqualTo(4), "accept, settle, fault and project stages");

            const int steps = 5;
            for (int step = 1; step <= steps; step++)
            {
                host.NotifyCommandAdmitted(1U);
                WorldPumpResult result = host.PumpFrame((ulong)step * 16_000UL);
                Assert.That(result.Advance, Is.Not.Null);
                Assert.That(result.Sample.AdmittedSteps, Is.EqualTo(1UL), "One command admits exactly one logical step.");
                Assert.That(host.StepGroup.LastDispatchedCount, Is.EqualTo(entriesPerStep), "Each registered system runs exactly once per step.");
            }

            Assert.That(host.StepGroup.TotalDispatchedCount, Is.EqualTo(steps * entriesPerStep));
            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(steps)));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(steps + 1), "One image per committed step plus the initial one.");

            FixtureTrail trail = ReadTrail(host);
            Assert.That(trail.AcceptCount, Is.EqualTo(steps));
            Assert.That(trail.SettleCount, Is.EqualTo(steps));
            Assert.That(trail.FaultCount, Is.EqualTo(steps));
            Assert.That(trail.ProjectCount, Is.EqualTo(steps));
            Assert.That(trail.IngressCount, Is.EqualTo(steps));
            Assert.That(trail.OutputCount, Is.EqualTo(steps));
            Assert.That(FixtureWorldState.ReadCounter(host.EntityWorld.EntityManager), Is.EqualTo(steps * 111));
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "Every recorded step job is complete after the step fence.");
        }

        [Test]
        public void PauseChangesHostStatusOnlyAndAddsNoDebt()
        {
            UnityWorldHost host = CreateFixedWorld();
            host.NotifyCommandAdmitted(0U);

            host.PumpFrame(0UL);
            host.PumpFrame(10_000_000UL);
            LogicalStepId afterFirstPump = host.CurrentStep;
            AssemblyEpoch epochBeforePause = host.CurrentEpoch;
            TimeDebt debtBeforePause = host.RetainedDebt;
            Assert.That(afterFirstPump.Value, Is.GreaterThan(0UL), "A fixed-step world advances on elapsed host time.");
            Assert.That(debtBeforePause.Ticks, Is.GreaterThan(0UL), "Unadmitted fixed-step time is retained as debt.");

            var pause = new OperationId(host.World, Issuer, 10UL);
            OperationResult paused = host.SetRunState(pause, WorldLifecycleState.Paused);
            Assert.That(paused.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(paused.PublishedSnapshot, Is.Not.Null);
            Assert.That(paused.PublishedSnapshot!.Value.LogicalStepId, Is.EqualTo(afterFirstPump), "Pausing changes no logical step (O-26).");

            host.PumpFrame(60_000_000UL);
            Assert.That(host.CurrentStep, Is.EqualTo(afterFirstPump), "A paused world does not advance.");
            Assert.That(host.CurrentEpoch, Is.EqualTo(epochBeforePause));
            Assert.That(host.RetainedDebt, Is.EqualTo(debtBeforePause), "The paused interval adds no simulation debt; retained debt stays.");

            OperationResult same = host.SetRunState(new OperationId(host.World, Issuer, 11UL), WorldLifecycleState.Paused);
            Assert.That(same.Outcome, Is.EqualTo(Outcome.NoChange), "The same run state is a no-op (O-26).");

            OperationResult resumed = host.SetRunState(new OperationId(host.World, Issuer, 12UL), WorldLifecycleState.Running);
            Assert.That(resumed.Outcome, Is.EqualTo(Outcome.Published));

            host.PumpFrame(60_010_000UL);
            Assert.That(host.CurrentStep.Value, Is.GreaterThan(afterFirstPump.Value), "Resume processes retained debt and new time under the usual limit.");
            Assert.That(host.CurrentEpoch, Is.EqualTo(epochBeforePause), "Pause and resume never change the assembly epoch.");
        }

        [Test]
        public void InvalidLifecycleRequestsAreRejectedWithoutMutation()
        {
            UnityWorldHost host = CreateCommandWorld(true);

            OperationResult bogus = host.SetRunState(new OperationId(host.World, Issuer, 20UL), WorldLifecycleState.Stopping);
            Assert.That(bogus.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(bogus.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));

            UnityWorldRegistry.TryGet(host.World, out UnityWorldHost? found);
            Assert.That(found, Is.SameAs(host));

            WorldCreateResult bad = host.Create(new WorldCreateRequest(
                host.World,
                host.Request.Definition,
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                ContentHash.Empty,
                new OperationId(host.World, Issuer, 21UL),
                FixtureRegistration.FixedStepConfiguration()));
            Assert.That(bad.Created, Is.False);
            Assert.That(bad.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion), "Changing the temporal model requires checkpoint/recreation (P-036).");
        }

        [Test]
        public void StopClosesIngressSettlesJobsAndDisposesStorage()
        {
            UnityWorldHost host = CreateCommandWorld(true);
            host.NotifyCommandAdmitted(1U);
            host.PumpFrame(1_000_000UL);
            World savedWorld = host.EntityWorld;

            var stop = new OperationId(host.World, Issuer, 30UL);
            OperationResult result = host.Stop(stop, "fixture stop");

            Assert.That(result.Outcome, Is.EqualTo(Outcome.Published), result.Code + " " + result.CodeText);
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));
            Assert.That(host.IsEntityWorldCreated, Is.False, "World storage is disposed only after its users settled (P-048).");
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(0));
            Assert.That(host.Ledger.RetainedResourceCount, Is.EqualTo(0));
            Assert.That(savedWorld.IsCreated, Is.False);

            OperationResult again = host.Stop(stop, "fixture stop again");
            Assert.That(again.Outcome, Is.EqualTo(Outcome.NoChange), "A repeated stop joins the same outcome (O-19).");
        }

        [Test]
        public void WakingACommandDrivenWorldAdvancesWithoutAPlayerCommand()
        {
            UnityWorldHost host = CreateCommandWorld(true);

            host.RequestWake(1U);
            Assert.That(host.PendingDemand, Is.EqualTo(1UL));

            host.PumpFrame(1_000_000UL);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "A registered wake admits one logical step (P-036).");
            Assert.That(host.PendingDemand, Is.EqualTo(0UL), "Consumed demand does not advance a second step.");
        }
    }
}
