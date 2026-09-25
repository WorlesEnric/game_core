// GameCore.Unity.Runtime EditMode tests - both temporal drivers (GC-009, TEST-011).
//
// These assertions run on a real owned Unity world through the real temporal driver, so they cover what the pure
// suite cannot: that an idle command-driven world advances zero logical steps while its ingress/output stages keep
// running, that fixed-step debt survives frames and unrun input is retained, that a registered plugin wake makes a
// command-driven world advance without a player command, and that pause applies each clock's declared policy
// without adding debt or moving the step (P-035 to P-038, O-26).
#nullable enable
using GameCore.Contracts;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Time;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests.Time
{
    [TestFixture]
    public sealed class TemporalDriverTests
    {
        private const ulong SessionSalt = 0x474354494D455457UL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525447UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
            TimeFixtureModule.DetachAll();
        }

        private static WorldId NextWorld()
        {
            sessionSequence++;
            return new WorldId(new Id128(SessionSalt, sessionSequence));
        }

        private static TimeFixtureModule CreateWorld(bool fixedStep, uint perStepCapacity, out UnityWorldHost host)
        {
            WorldId world = NextWorld();
            var operation = new OperationId(world, Issuer, 1UL);
            bool created = TimeFixtureRegistration.TryCreateWorld(
                world,
                operation,
                fixedStep,
                perStepCapacity,
                out UnityWorldHost? worldHost,
                out TimeFixtureModule? module,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(worldHost, Is.Not.Null);
            Assert.That(module, Is.Not.Null);
            host = worldHost!;
            return module!;
        }

        private static TimeFixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(TimeFixtureWorldState.TryRead(host.EntityWorld.EntityManager, out TimeFixtureTrail trail), Is.True);
            return trail;
        }

        // ---------------------------------------------------------------- idle command-driven world

        [Test]
        public void AnIdleCommandDrivenWorldSealsNoInputAndAdvancesZeroSteps()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            for (ulong frame = 1UL; frame <= 10UL; frame++)
            {
                TimeFrameReport report = driver.PumpFrame(frame * TimeFixtureRegistration.HostTicksPerSecond);
                Assert.That(report.StepsCommitted, Is.EqualTo(0UL), "host time is not simulation demand (P-036)");
                Assert.That(report.Seal.AdmittedCount, Is.EqualTo(0), "an empty queue seals nothing");
                Assert.That(report.DueWakes, Is.EqualTo(0));
            }

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "ten idle seconds advance nothing (TEST-011)");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(driver.Cutoff.SealCount, Is.EqualTo(0), "an idle world never seals a batch");
            Assert.That(driver.Cutoff.LastAssignedSequence, Is.EqualTo(0UL), "no admission sequence is invented");
            Assert.That(host.RetainedDebt, Is.EqualTo(TimeDebt.Zero), "a command-driven world has no time debt");

            TimeFixtureTrail trail = ReadTrail(host);
            Assert.That(trail.ProduceCount, Is.EqualTo(0), "no gameplay system runs while the world is idle");
            Assert.That(trail.ConsumeCount, Is.EqualTo(0));
            Assert.That(trail.CaptureCount, Is.EqualTo(10),
                "the output stage still runs on host frames even when the world is idle (04 section 3)");
        }

        [Test]
        public void OneAdmittedCommandAdvancesExactlyOneStepAndConsumesItsSeal()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var requestKey = new Id128(TimeFixtureKeys.Namespace, 0x9001UL);
            bool admitted = driver.TryAdmitCommand(
                requestKey,
                new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9101UL)), 1U),
                out AdmissionSequence sequence,
                out DiagnosticCode code);
            Assert.That(admitted, Is.True, code.ToString());
            Assert.That(sequence, Is.EqualTo(AdmissionSequence.First), "the host assigns sequence one");

            TimeFrameReport first = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(first.StepsCommitted, Is.EqualTo(1UL));
            Assert.That(first.Seal.AdmittedCommands, Is.EqualTo(1), "the sealed batch is the step's input");
            Assert.That(first.RestoredInput, Is.EqualTo(0), "a committed step consumes its batch");
            Assert.That(first.ConsumedSeal, Is.True);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));

            TimeFixtureTrail trail = ReadTrail(host);
            Assert.That(trail.ProduceCount, Is.EqualTo(1));
            Assert.That(trail.ConsumeCount, Is.EqualTo(1));

            TimeFrameReport second = driver.PumpFrame(2UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(second.StepsCommitted, Is.EqualTo(0UL), "the queue is empty again: no work, no step (O-14)");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
        }

        [Test]
        public void ACommandArrivingAfterTheCutoffWaitsForTheNextStep()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var firstKey = new Id128(TimeFixtureKeys.Namespace, 0x9002UL);
            var secondKey = new Id128(TimeFixtureKeys.Namespace, 0x9003UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9102UL)), 1U);

            Assert.That(driver.TryAdmitCommand(firstKey, schema, out AdmissionSequence first, out _), Is.True);
            Assert.That(driver.TryAdmitCommand(secondKey, schema, out AdmissionSequence second, out _), Is.True);
            Assert.That(second.Value, Is.GreaterThan(first.Value), "host admission order is monotonic (P-037, P-008)");
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(2));

            // Capacity is one unit per step: the first frame consumes the first admitted command and the second
            // command waits, which is the cutoff rule rather than a race (P-037).
            TimeFrameReport frameOne = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(frameOne.Seal.Admitted.Count, Is.EqualTo(1));
            Assert.That(frameOne.Seal.Admitted[0].RequestKey, Is.EqualTo(firstKey));
            Assert.That(frameOne.Seal.Deferred, Is.EqualTo(1), "the remainder is retained, not dropped");
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(1));

            var thirdKey = new Id128(TimeFixtureKeys.Namespace, 0x9004UL);
            Assert.That(driver.TryAdmitCommand(thirdKey, schema, out AdmissionSequence third, out _), Is.True);
            Assert.That(third.Value, Is.GreaterThan(second.Value));

            TimeFrameReport frameTwo = driver.PumpFrame(2UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(frameTwo.Seal.Admitted[0].RequestKey, Is.EqualTo(secondKey),
                "queued input keeps its host admission order");
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(1));

            TimeFrameReport frameThree = driver.PumpFrame(3UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(frameThree.Seal.Admitted[0].RequestKey, Is.EqualTo(thirdKey));
            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(3UL)), "three admitted commands ran three steps");
            Assert.That(driver.Cutoff.DeferredSealCount, Is.EqualTo(2));
            Assert.That(driver.Cutoff.MaxQueueDepth, Is.EqualTo(2), "the deepest backlog is observable");
        }

        [Test]
        public void ADuplicateRequestKeyIsRefusedWithItsOriginalSequence()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var requestKey = new Id128(TimeFixtureKeys.Namespace, 0x9005UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9103UL)), 1U);

            Assert.That(driver.TryAdmitCommand(requestKey, schema, out AdmissionSequence first, out _), Is.True);
            Assert.That(driver.TryAdmitCommand(requestKey, schema, out AdmissionSequence repeated, out DiagnosticCode code), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(repeated, Is.EqualTo(first), "a duplicate returns the recorded attempt, not a new execution (P-037)");
            Assert.That(driver.Cutoff.DuplicateCount, Is.EqualTo(1));

            driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "the duplicate never ran a second step");
            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(1));
        }

        [Test]
        public void AFullPendingQueueBackpressuresInsteadOfDroppingInput()
        {
            WorldId world = NextWorld();
            var operation = new OperationId(world, Issuer, 2UL);
            bool created = TimeFixtureRegistration.TryCreateWorld(
                world,
                operation,
                false,
                1U,
                out UnityWorldHost? host,
                out TimeFixtureModule? module,
                out WorldCreateResult result);
            Assert.That(created, Is.True, result.Code + ": " + result.Detail);

            var cutoff = new StepInputCutoff(2, 8);
            var driver = new WorldTimeDriver(host!, cutoff, new PluginClockRegistry(4), 1U);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9104UL)), 1U);

            for (ulong i = 0; i < 2UL; i++)
            {
                Assert.That(
                    driver.TryAdmitCommand(new Id128(TimeFixtureKeys.Namespace, 0x9010UL + i), schema, out _, out _),
                    Is.True);
            }

            bool admitted = driver.TryAdmitCommand(new Id128(TimeFixtureKeys.Namespace, 0x9018UL), schema, out _, out DiagnosticCode code);
            Assert.That(admitted, Is.False, "a full bounded queue refuses instead of silently dropping (P-042)");
            Assert.That(code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(cutoff.OverflowCount, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- fixed step

        [Test]
        public void FixedStepRetainsDebtAcrossFramesAndBoundsCatchUp()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: true, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            Assert.That(driver.StepDurationTicks, Is.EqualTo(100_000UL));

            TimeFrameReport opener = driver.PumpFrame(5_000_000UL);
            Assert.That(opener.StepsCommitted, Is.EqualTo(0UL), "host time before the world's first pump is not debt");

            // One second of host time at 10 ms per step is 100 steps; the declared catch-up limit admits four.
            TimeFrameReport burst = driver.PumpFrame(5_000_000UL + TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(burst.StepsCommitted, Is.EqualTo(4UL), "the per-pump catch-up limit bounds one frame");
            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(4UL)));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(9_600_000UL), "unadmitted time is retained exactly (P-036)");

            TimeFrameReport next = driver.PumpFrame(5_000_000UL + TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(next.StepsCommitted, Is.EqualTo(4UL), "retained debt still funds a bounded batch");
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(9_200_000UL));

            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(8), "each committed step ran the gameplay stages once");
        }

        [Test]
        public void AFixedFrameThatCommitsNoStepReturnsItsSealedInputToTheQueue()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: true, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var requestKey = new Id128(TimeFixtureKeys.Namespace, 0x9006UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9105UL)), 1U);

            driver.PumpFrame(0UL);
            Assert.That(driver.TryAdmitCommand(requestKey, schema, out _, out _), Is.True);

            // 5 ms is below the 10 ms step duration, so the frame commits no step and the batch is retained.
            TimeFrameReport below = driver.PumpFrame(50_000UL);
            Assert.That(below.StepsCommitted, Is.EqualTo(0UL));
            Assert.That(below.RestoredInput, Is.EqualTo(1), "a step that never ran must not consume its input (P-037)");
            Assert.That(below.Seal.SealedBatch, Is.True, "the batch was sealed and then returned");
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(1), "the command is still queued");
            Assert.That(driver.Cutoff.LastAssignedSequence, Is.EqualTo(1UL), "returning a batch does not renumber input");
            Assert.That(driver.RestoredFrameCount, Is.EqualTo(1));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(50_000UL), "the sub-step remainder is retained");
            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(0));

            TimeFrameReport crossing = driver.PumpFrame(150_000UL);
            Assert.That(crossing.StepsCommitted, Is.EqualTo(1UL), "retained debt plus new time funds exactly one step");
            Assert.That(crossing.Seal.Admitted[0].RequestKey, Is.EqualTo(requestKey),
                "the returned input is the batch the next step consumes");
            Assert.That(crossing.RestoredInput, Is.EqualTo(0));
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(0));
            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(1));
        }

        [Test]
        public void AFixedStepWorldDoesNotInflateCommandDemand()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: true, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9106UL)), 1U);
            for (ulong i = 0; i < 4UL; i++)
            {
                Assert.That(driver.TryAdmitCommand(new Id128(TimeFixtureKeys.Namespace, 0x9020UL + i), schema, out _, out _), Is.True);
            }

            driver.PumpFrame(0UL);
            TimeFrameReport burst = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);

            Assert.That(burst.StepsCommitted, Is.EqualTo(4UL), "a fixed-step world advances on elapsed time (P-036)");
            Assert.That(host.PendingDemand, Is.EqualTo(0UL),
                "a fixed-step world's demand count is never inflated by commands it advances by time");
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(3),
                "one sealed batch was consumed by the frame's first step and the rest stayed queued");
            Assert.That(burst.Seal.AdmittedCommands, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- plugin clocks and wakes

        [Test]
        public void ARegisteredWakeAdvancesACommandDrivenWorldWithoutAPlayerCommand()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var clockId = new Id128(TimeFixtureKeys.Namespace, 0xA001UL);
            var wakeId = new Id128(TimeFixtureKeys.Namespace, 0xA002UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0xA003UL)), 1U);

            Assert.That(
                driver.Clocks.TryRegister(
                    new PluginClockSpec(clockId, "fixture.clock", PluginClockKind.LogicalStep, WakePausePolicy.Defer, true),
                    out DiagnosticCode registerCode),
                Is.True,
                registerCode.ToString());

            Assert.That(
                driver.TryScheduleWake(clockId, wakeId, schema, 0UL, 0UL, out WakeRecord? wake, out DiagnosticCode scheduleCode),
                Is.True,
                scheduleCode.ToString());
            Assert.That(wake, Is.Not.Null);
            Assert.That(wake!.IsDue, Is.True, "a zero-delay wake is due at once");

            TimeFrameReport frame = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);

            Assert.That(frame.DueWakes, Is.EqualTo(1));
            Assert.That(frame.StepsCommitted, Is.EqualTo(1UL), "a registered wake is demand without a player command (P-036)");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
            Assert.That(wake.IsConsumed, Is.True, "a consumed wake never fires twice (P-037)");
            Assert.That(driver.Clocks.DueWakeCount, Is.EqualTo(0));
            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(1));

            TimeFrameReport idle = driver.PumpFrame(2UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(idle.StepsCommitted, Is.EqualTo(0UL), "once the wake is consumed the world is idle again");
        }

        [Test]
        public void ADomainClockWakeFiresOnlyWhenTheDomainAdvancesItsClock()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var clockId = new Id128(TimeFixtureKeys.Namespace, 0xB001UL);
            var wakeId = new Id128(TimeFixtureKeys.Namespace, 0xB002UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0xB003UL)), 1U);

            Assert.That(
                driver.Clocks.TryRegister(
                    new PluginClockSpec(clockId, "fixture.storyDay", PluginClockKind.DomainExplicit, WakePausePolicy.Defer, true),
                    out _),
                Is.True);
            Assert.That(
                driver.TryScheduleWake(clockId, wakeId, schema, 0UL, 100UL, out WakeRecord? wake, out DiagnosticCode code),
                Is.True,
                code.ToString());
            Assert.That(wake!.IsDue, Is.False, "the delayed wake waits for its clock");

            TimeFrameReport before = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(before.DueWakes, Is.EqualTo(0));
            Assert.That(before.StepsCommitted, Is.EqualTo(0UL),
                "no command, no wake and no elapsed simulation time means zero steps (P-036)");

            Assert.That(driver.AdvanceDomainClock(clockId, 60UL), Is.EqualTo(0), "60 of 100 ticks is not yet due");
            Assert.That(driver.AdvanceDomainClock(clockId, 40UL), Is.EqualTo(1), "the explicit advance makes it due");

            TimeFrameReport after = driver.PumpFrame(2UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(after.DueWakes, Is.EqualTo(1));
            Assert.That(after.StepsCommitted, Is.EqualTo(1UL));
            Assert.That(host.DomainSeconds, Is.EqualTo(0.0),
                "a domain clock advance is not inferred simulation seconds (P-038)");
        }

        [Test]
        public void PauseAppliesEachClocksDeclaredWakePolicyAndAddsNoDebt()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var deferClock = new Id128(TimeFixtureKeys.Namespace, 0xC001UL);
            var cancelClock = new Id128(TimeFixtureKeys.Namespace, 0xC002UL);
            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0xC003UL)), 1U);

            Assert.That(
                driver.Clocks.TryRegister(
                    new PluginClockSpec(deferClock, "fixture.defer", PluginClockKind.LogicalStep, WakePausePolicy.Defer, false),
                    out _),
                Is.True);
            Assert.That(
                driver.Clocks.TryRegister(
                    new PluginClockSpec(cancelClock, "fixture.cancel", PluginClockKind.LogicalStep, WakePausePolicy.Cancel, false),
                    out _),
                Is.True);
            Assert.That(
                driver.TryScheduleWake(deferClock, new Id128(TimeFixtureKeys.Namespace, 0xC011UL), schema, 0UL, 0UL, out _, out _),
                Is.True);
            Assert.That(
                driver.TryScheduleWake(cancelClock, new Id128(TimeFixtureKeys.Namespace, 0xC012UL), schema, 0UL, 0UL, out _, out _),
                Is.True);

            host.SetRunState(new OperationId(host.World, Issuer, 3UL), WorldLifecycleState.Paused);
            Assert.That(driver.OnWorldPaused(), Is.EqualTo(1), "only the cancelling clock loses its wake (P-038)");

            TimeFrameReport paused = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(paused.Paused, Is.True);
            Assert.That(paused.StepsCommitted, Is.EqualTo(0UL), "a paused world does not advance simulation (P-035)");
            Assert.That(paused.Seal.SealedBatch, Is.False, "a paused world seals nothing");
            Assert.That(paused.DueWakes, Is.EqualTo(0), "a paused world fires no clock");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First), "pause does not move the epoch (O-26)");
            Assert.That(host.RetainedDebt, Is.EqualTo(TimeDebt.Zero));
            Assert.That(driver.Clocks.CancelledCount, Is.EqualTo(1));
            Assert.That(driver.Clocks.PendingWakeCount, Is.EqualTo(0), "the cancelling clock has no pending wake left");
            Assert.That(ReadTrail(host).ProduceCount, Is.EqualTo(0));

            host.SetRunState(new OperationId(host.World, Issuer, 4UL), WorldLifecycleState.Running);
            driver.OnWorldResumed();

            TimeFrameReport resumed = driver.PumpFrame(5UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(resumed.StepsCommitted, Is.EqualTo(1UL),
                "the deferred wake survives the pause and schedules a step after resume (O-26)");
            Assert.That(driver.PauseCount, Is.EqualTo(1));
            Assert.That(driver.ResumeCount, Is.EqualTo(1));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
        }

        [Test]
        public void AFixedDurationClockAdvancesOnlyByCommittedStepTime()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: true, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var clockId = new Id128(TimeFixtureKeys.Namespace, 0xD001UL);
            Assert.That(
                driver.Clocks.TryRegister(
                    new PluginClockSpec(clockId, "fixture.duration", PluginClockKind.FixedDuration, WakePausePolicy.Defer, false),
                    out _),
                Is.True);
            Assert.That(
                driver.TryScheduleWake(
                    clockId,
                    new Id128(TimeFixtureKeys.Namespace, 0xD002UL),
                    new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0xD003UL)), 1U),
                    0UL,
                    500_000UL,
                    out WakeRecord? wake,
                    out DiagnosticCode code),
                Is.True,
                code.ToString());

            driver.PumpFrame(0UL);

            // One second of host time admits four 10 ms steps, so the clock advances 400 ms of simulation time: not
            // the second of host time and not the number of rendered frames (P-036, P-038).
            TimeFrameReport burst = driver.PumpFrame(TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(burst.StepsCommitted, Is.EqualTo(4UL));
            Assert.That(burst.SimulationTicks, Is.EqualTo(400_000UL), "clock time is committed simulation time (P-038)");
            Assert.That(wake!.IsDue, Is.False, "500 ms of clock time has not elapsed after four 10 ms steps");

            TimeFrameReport next = driver.PumpFrame(2UL * TimeFixtureRegistration.HostTicksPerSecond);
            Assert.That(next.StepsCommitted, Is.EqualTo(4UL));
            Assert.That(next.SimulationTicks, Is.EqualTo(400_000UL));
            Assert.That(wake.IsDue, Is.True, "the eighth committed step takes the clock past 500 ms");
            Assert.That(driver.Clocks.DueWakeCount, Is.EqualTo(1));
        }

        [Test]
        public void TeardownReportsWhatItDiscards()
        {
            TimeFixtureModule module = CreateWorld(fixedStep: false, perStepCapacity: 1U, out UnityWorldHost host);
            WorldTimeDriver driver = module.Time!;

            var schema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9107UL)), 1U);
            Assert.That(driver.TryAdmitCommand(new Id128(TimeFixtureKeys.Namespace, 0x9030UL), schema, out _, out _), Is.True);

            driver.Clear(out int discardedCommands, out int pendingWakes);

            Assert.That(discardedCommands, Is.EqualTo(1));
            Assert.That(pendingWakes, Is.EqualTo(0));
            Assert.That(driver.Cutoff.PendingCount, Is.EqualTo(0));
            Assert.That(driver.Clocks.ClockCount, Is.EqualTo(0));

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "clearing the driver moved no step");
        }
    }
}
